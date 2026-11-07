using EnterprisePaymentSystem.Application.DTOs;
using EnterprisePaymentSystem.Application.Helpers;
using EnterprisePaymentSystem.Application.Interfaces;
using EnterprisePaymentSystem.Application.Services;
using EnterprisePaymentSystem.Domain.Entities;
using EnterprisePaymentSystem.Domain.Enums;
using EnterprisePaymentSystem.Domain.Exceptions;
using EnterprisePaymentSystem.Infrastructure.Data;
using EnterprisePaymentSystem.Infrastructure.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace EnterprisePaymentSystem.UnitTests;

public class RepositoryAndServiceTests
{
    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();
    private readonly Guid _userA = Guid.NewGuid();

    private ApplicationDbContext CreateInMemoryDbContext(string dbName, Guid? currentTenantId)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;

        var mockCurrentUserService = new Mock<ICurrentUserService>();
        mockCurrentUserService.Setup(s => s.TenantId).Returns(currentTenantId);
        mockCurrentUserService.Setup(s => s.UserId).Returns(_userA);
        mockCurrentUserService.Setup(s => s.FullName).Returns("Suresh Kumar");
        mockCurrentUserService.Setup(s => s.Role).Returns(nameof(UserRole.Supervisor));

        return new ApplicationDbContext(options, mockCurrentUserService.Object);
    }

    [Fact]
    public async Task MultiTenancy_GlobalQueryFilter_ShouldIsolateDataBetweenTenants()
    {
        var dbName = Guid.NewGuid().ToString();

        // 1. Seed as Admin (no tenant filter)
        using (var setupContext = CreateInMemoryDbContext(dbName, null))
        {
            var userA = new User { Id = _userA, TenantId = _tenantA, FullName = "User A", Email = "a@test.com", PhoneNumber = "111", SixDigitUserId = "111111" };
            var userB = new User { Id = Guid.NewGuid(), TenantId = _tenantB, FullName = "User B", Email = "b@test.com", PhoneNumber = "222", SixDigitUserId = "222222" };
            await setupContext.Users.AddRangeAsync(userA, userB);

            var paymentA = new PaymentRequest
            {
                Id = Guid.NewGuid(),
                TenantId = _tenantA,
                UserId = _userA,
                Title = "Tenant A Cement Delivery",
                Amount = 500m,
                ExpenseYear = 2026,
                StartDate = new DateOnly(2026, 1, 1),
                EndDate = new DateOnly(2026, 1, 1),
                Status = PaymentStatus.Pending
            };

            var paymentB = new PaymentRequest
            {
                Id = Guid.NewGuid(),
                TenantId = _tenantB,
                UserId = userB.Id,
                Title = "Tenant B Sariya Delivery",
                Amount = 1500m,
                ExpenseYear = 2026,
                StartDate = new DateOnly(2026, 2, 1),
                EndDate = new DateOnly(2026, 2, 1),
                Status = PaymentStatus.Pending
            };

            await setupContext.PaymentRequests.AddRangeAsync(paymentA, paymentB);
            await setupContext.SaveChangesAsync();
        }

        // 2. Query as Tenant A
        using (var contextA = CreateInMemoryDbContext(dbName, _tenantA))
        {
            var repoA = new PaymentRepository(contextA);
            var listA = await repoA.GetFilteredPaymentsAsync(2026);

            listA.Should().HaveCount(1);
            listA.First().Title.Should().Be("Tenant A Cement Delivery");
        }

        // 3. Query as Tenant B
        using (var contextB = CreateInMemoryDbContext(dbName, _tenantB))
        {
            var repoB = new PaymentRepository(contextB);
            var listB = await repoB.GetFilteredPaymentsAsync(2026);

            listB.Should().HaveCount(1);
            listB.First().Title.Should().Be("Tenant B Sariya Delivery");
        }
    }

    [Fact]
    public async Task PaymentService_OptimisticConcurrency_ShouldThrowOnVersionMismatch()
    {
        var dbName = Guid.NewGuid().ToString();
        var paymentId = Guid.NewGuid();

        // 1. Seed payment
        using (var setupContext = CreateInMemoryDbContext(dbName, _tenantA))
        {
            var user = new User { Id = _userA, TenantId = _tenantA, FullName = "Test Supervisor", Email = "test@user.com", PhoneNumber = "111", SixDigitUserId = "111111" };
            await setupContext.Users.AddAsync(user);

            var payment = new PaymentRequest
            {
                Id = paymentId,
                TenantId = _tenantA,
                UserId = _userA,
                Title = "Original Expense",
                Amount = 300m,
                ExpenseYear = 2026,
                StartDate = new DateOnly(2026, 3, 1),
                EndDate = new DateOnly(2026, 3, 1),
                Status = PaymentStatus.Pending,
                Version = 1
            };
            await setupContext.PaymentRequests.AddAsync(payment);
            await setupContext.SaveChangesAsync();
        }

        // 2. Execute update with mismatched version
        using (var context = CreateInMemoryDbContext(dbName, _tenantA))
        {
            var repo = new PaymentRepository(context);

            var mockCurrentUserService = new Mock<ICurrentUserService>();
            mockCurrentUserService.Setup(s => s.TenantId).Returns(_tenantA);
            mockCurrentUserService.Setup(s => s.UserId).Returns(_userA);
            mockCurrentUserService.Setup(s => s.FullName).Returns("Test Supervisor");

            var service = new PaymentService(
                repo,
                mockCurrentUserService.Object,
                new PaymentCalculationHelper(),
                new AuditTrailHelper(),
                new DateRangeHelper()
            );

            var staleRequest = new UpdatePaymentRequest(
                Title: "Updated",
                Description: "Updated desc",
                Amount: 400m,
                Currency: "INR",
                Category: "Cement",
                CategoryId: null,
                SiteId: null,
                ExpenseYear: 2026,
                StartDate: new DateOnly(2026, 3, 1),
                EndDate: new DateOnly(2026, 3, 1),
                InvoiceNumber: null,
                ReceiptAttachmentPath: null,
                Version: 99 // Incorrect version!
            );

            var act = async () => await service.UpdateAsync(paymentId, staleRequest);
            await act.Should().ThrowAsync<ConcurrencyConflictException>();
        }
    }

    [Fact]
    public async Task PaymentService_PeerApproval_ShouldThrowIfAdminApprovesOwnRequest()
    {
        var dbName = Guid.NewGuid().ToString();
        var adminId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();

        // 1. Seed payment created by this Admin
        using (var setupContext = CreateInMemoryDbContext(dbName, _tenantA))
        {
            var adminUser = new User { Id = adminId, TenantId = _tenantA, FullName = "Admin User 1", Email = "admin1@test.com", PhoneNumber = "111", SixDigitUserId = "111111", Role = UserRole.Admin };
            await setupContext.Users.AddAsync(adminUser);

            var payment = new PaymentRequest
            {
                Id = paymentId,
                TenantId = _tenantA,
                UserId = adminId,
                Title = "Heavy Machinery Advance",
                Amount = 50000m,
                ExpenseYear = 2026,
                StartDate = new DateOnly(2026, 4, 1),
                EndDate = new DateOnly(2026, 4, 1),
                Status = PaymentStatus.Pending,
                Version = 1
            };
            await setupContext.PaymentRequests.AddAsync(payment);
            await setupContext.SaveChangesAsync();
        }

        // 2. Try to approve as the SAME Admin
        using (var context = CreateInMemoryDbContext(dbName, _tenantA))
        {
            var repo = new PaymentRepository(context);

            var mockCurrentUserService = new Mock<ICurrentUserService>();
            mockCurrentUserService.Setup(s => s.TenantId).Returns(_tenantA);
            mockCurrentUserService.Setup(s => s.UserId).Returns(adminId); // SAME ADMIN!
            mockCurrentUserService.Setup(s => s.FullName).Returns("Admin User 1");
            mockCurrentUserService.Setup(s => s.Role).Returns(nameof(UserRole.Admin));

            var service = new PaymentService(
                repo,
                mockCurrentUserService.Object,
                new PaymentCalculationHelper(),
                new AuditTrailHelper(),
                new DateRangeHelper()
            );

            var approveReq = new ApprovePaymentRequest(Comments: "Self approval attempt", OverrideCategoryId: null, NewCategoryName: null, OverrideSiteId: null, Version: 1);

            var act = async () => await service.ApproveAsync(paymentId, approveReq);
            await act.Should().ThrowAsync<DomainException>()
                .WithMessage("*Peer approval required*");
        }

        // 3. Approve as a DIFFERENT Admin (Multi-Admin Peer Approval)
        using (var context = CreateInMemoryDbContext(dbName, _tenantA))
        {
            var repo = new PaymentRepository(context);

            var otherAdminId = Guid.NewGuid(); // DIFFERENT ADMIN!
            var mockCurrentUserService = new Mock<ICurrentUserService>();
            mockCurrentUserService.Setup(s => s.TenantId).Returns(_tenantA);
            mockCurrentUserService.Setup(s => s.UserId).Returns(otherAdminId);
            mockCurrentUserService.Setup(s => s.FullName).Returns("Admin User 2");
            mockCurrentUserService.Setup(s => s.Role).Returns(nameof(UserRole.Admin));

            var service = new PaymentService(
                repo,
                mockCurrentUserService.Object,
                new PaymentCalculationHelper(),
                new AuditTrailHelper(),
                new DateRangeHelper()
            );

            var approveReq = new ApprovePaymentRequest(Comments: "Approved by Peer Admin", OverrideCategoryId: null, NewCategoryName: null, OverrideSiteId: null, Version: 1);

            var result = await service.ApproveAsync(paymentId, approveReq);
            result.Status.Should().Be(PaymentStatus.Approved);
        }
    }
}
