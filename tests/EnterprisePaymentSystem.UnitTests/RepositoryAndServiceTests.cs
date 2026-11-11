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

    [Fact]
    public async Task PaymentService_ServerSidePagination_ShouldReturnRequestedPageAndAccurateFullSummary()
    {
        var dbName = Guid.NewGuid().ToString();
        var adminId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();

        using (var setupContext = CreateInMemoryDbContext(dbName, _tenantA))
        {
            var admin = new User { Id = adminId, TenantId = _tenantA, FullName = "Admin Khan", Email = "admin@khan.com", PhoneNumber = "9999999999", SixDigitUserId = "000001", Role = UserRole.Admin };
            var worker = new User { Id = otherUserId, TenantId = _tenantA, FullName = "Worker Ali", Email = "ali@khan.com", PhoneNumber = "8888888888", SixDigitUserId = "000002", Role = UserRole.Supervisor };
            await setupContext.Users.AddRangeAsync(admin, worker);

            var requests = new List<PaymentRequest>();
            var baseTime = new DateTime(2026, 6, 1, 10, 0, 0, DateTimeKind.Utc);
            for (int i = 1; i <= 45; i++)
            {
                var status = i <= 15
                    ? PaymentStatus.Pending
                    : (i <= 35 ? PaymentStatus.Approved : PaymentStatus.Completed);

                requests.Add(new PaymentRequest
                {
                    Id = Guid.NewGuid(),
                    TenantId = _tenantA,
                    UserId = i % 2 == 0 ? adminId : otherUserId,
                    Title = $"Expense #{i:D2}",
                    Description = "Site work",
                    Category = i % 3 == 0 ? "Cement" : "Steel",
                    Amount = 100m,
                    ExpenseYear = 2026,
                    StartDate = new DateOnly(2026, 5, 1),
                    EndDate = new DateOnly(2026, 5, 1),
                    Status = status,
                    CreatedAtUtc = baseTime.AddMinutes(i),
                    Version = 1
                });
            }

            await setupContext.PaymentRequests.AddRangeAsync(requests);
            await setupContext.SaveChangesAsync();
        }

        using (var context = CreateInMemoryDbContext(dbName, _tenantA))
        {
            var repo = new PaymentRepository(context);
            var mockCurrentUserService = new Mock<ICurrentUserService>();
            mockCurrentUserService.Setup(s => s.TenantId).Returns(_tenantA);
            mockCurrentUserService.Setup(s => s.UserId).Returns(adminId);
            mockCurrentUserService.Setup(s => s.Role).Returns(nameof(UserRole.Admin));

            var service = new PaymentService(
                repo,
                mockCurrentUserService.Object,
                new PaymentCalculationHelper(),
                new AuditTrailHelper(),
                new DateRangeHelper()
            );

            // Page 1 (20 items)
            var page1 = await service.GetListAsync(new PaymentFilterQuery(Year: 2026, Page: 1, PageSize: 20));
            page1.Items.Should().HaveCount(20);
            page1.Page.Should().Be(1);
            page1.PageSize.Should().Be(20);
            page1.TotalCount.Should().Be(45);
            page1.TotalPages.Should().Be(3);
            page1.Summary.GrandTotalAmount.Should().Be(4500m);
            page1.Summary.TotalPendingAmount.Should().Be(1500m);
            page1.Summary.TotalApprovedAmount.Should().Be(2000m);
            page1.Summary.TotalCompletedAmount.Should().Be(1000m);
            // Default pie breakdown sums Approved + Completed (30 items * 100 = 3000)
            page1.Summary.CategoryBreakdowns.Sum(c => c.TotalAmount).Should().Be(3000m);

            // Page 3 (remaining 5 items)
            var page3 = await service.GetListAsync(new PaymentFilterQuery(Year: 2026, Page: 3, PageSize: 20));
            page3.Items.Should().HaveCount(5);
            page3.Page.Should().Be(3);
            page3.TotalCount.Should().Be(45);
            page3.Summary.GrandTotalAmount.Should().Be(4500m);

            // Status filter changes Pie Chart CategoryBreakdowns dynamically
            var pendingOnly = await service.GetListAsync(new PaymentFilterQuery(Year: 2026, Status: PaymentStatus.Pending, Page: 1, PageSize: 20));
            pendingOnly.TotalCount.Should().Be(15);
            pendingOnly.Items.Should().HaveCount(15);
            pendingOnly.Summary.CategoryBreakdowns.Sum(c => c.TotalAmount).Should().Be(1500m);
        }
    }

    [Fact]
    public async Task PaymentService_DescriptionRules_OptionalForEmployees_RequiredForAdmin()
    {
        var dbName = Guid.NewGuid().ToString();
        var supervisorId = Guid.NewGuid();
        var adminId = Guid.NewGuid();

        using (var context = CreateInMemoryDbContext(dbName, _tenantA))
        {
            var repo = new PaymentRepository(context);

            // 1. Supervisor creates standard expense without description -> Allowed
            var supervisorUser = new Mock<ICurrentUserService>();
            supervisorUser.Setup(s => s.TenantId).Returns(_tenantA);
            supervisorUser.Setup(s => s.UserId).Returns(supervisorId);
            supervisorUser.Setup(s => s.FullName).Returns("Site Supervisor");
            supervisorUser.Setup(s => s.Role).Returns(nameof(UserRole.Supervisor));

            var supervisorService = new PaymentService(
                repo,
                supervisorUser.Object,
                new PaymentCalculationHelper(),
                new AuditTrailHelper(),
                new DateRangeHelper()
            );

            var empCreated = await supervisorService.CreateAsync(new CreatePaymentRequest(
                Title: "Sand Unloading",
                Description: null,
                Amount: 2500m,
                Currency: "INR",
                Category: "Aggregates",
                CategoryId: null,
                SiteId: null,
                LabourerId: null,
                ExpenseYear: 2026,
                StartDate: new DateOnly(2026, 4, 1),
                EndDate: new DateOnly(2026, 4, 1),
                InvoiceNumber: null,
                ReceiptAttachmentPath: null
            ));

            empCreated.Description.Should().BeEmpty();
            empCreated.RequestedAmount.Should().Be(2500m);
            empCreated.Amount.Should().Be(2500m);
            empCreated.ApprovedAmount.Should().BeNull();

            // 2. Admin creates standard expense without description -> Blocked
            var adminUser = new Mock<ICurrentUserService>();
            adminUser.Setup(s => s.TenantId).Returns(_tenantA);
            adminUser.Setup(s => s.UserId).Returns(adminId);
            adminUser.Setup(s => s.FullName).Returns("Admin User");
            adminUser.Setup(s => s.Role).Returns(nameof(UserRole.Admin));

            var adminService = new PaymentService(
                repo,
                adminUser.Object,
                new PaymentCalculationHelper(),
                new AuditTrailHelper(),
                new DateRangeHelper()
            );

            var act = async () => await adminService.CreateAsync(new CreatePaymentRequest(
                Title: "Admin Direct Entry",
                Description: "   ",
                Amount: 3000m,
                Currency: "INR",
                Category: "Equipment",
                CategoryId: null,
                SiteId: null,
                LabourerId: null,
                ExpenseYear: 2026,
                StartDate: new DateOnly(2026, 4, 2),
                EndDate: new DateOnly(2026, 4, 2),
                InvoiceNumber: null,
                ReceiptAttachmentPath: null
            ));

            await act.Should().ThrowAsync<DomainException>()
                .WithMessage("*Description is required when an Admin*");
        }
    }

    [Fact]
    public async Task PaymentService_Approve_AllowsEditingAmountAndDescription_BlocksExceedingRequested()
    {
        var dbName = Guid.NewGuid().ToString();
        var employeeId = Guid.NewGuid();
        var adminId = Guid.NewGuid();

        Guid paymentId;

        using (var context = CreateInMemoryDbContext(dbName, _tenantA))
        {
            var repo = new PaymentRepository(context);
            var empUser = new Mock<ICurrentUserService>();
            empUser.Setup(s => s.TenantId).Returns(_tenantA);
            empUser.Setup(s => s.UserId).Returns(employeeId);
            empUser.Setup(s => s.FullName).Returns("Site Supervisor");
            empUser.Setup(s => s.Role).Returns(nameof(UserRole.Supervisor));

            var empService = new PaymentService(
                repo,
                empUser.Object,
                new PaymentCalculationHelper(),
                new AuditTrailHelper(),
                new DateRangeHelper()
            );

            var created = await empService.CreateAsync(new CreatePaymentRequest(
                Title: "Timber Shuttering",
                Description: "Initial estimate by site team",
                Amount: 5000m,
                Currency: "INR",
                Category: "Materials",
                CategoryId: null,
                SiteId: null,
                LabourerId: null,
                ExpenseYear: 2026,
                StartDate: new DateOnly(2026, 5, 10),
                EndDate: new DateOnly(2026, 5, 10),
                InvoiceNumber: null,
                ReceiptAttachmentPath: null
            ));
            paymentId = created.Id;
        }

        using (var context = CreateInMemoryDbContext(dbName, _tenantA))
        {
            var repo = new PaymentRepository(context);
            var adminUser = new Mock<ICurrentUserService>();
            adminUser.Setup(s => s.TenantId).Returns(_tenantA);
            adminUser.Setup(s => s.UserId).Returns(adminId);
            adminUser.Setup(s => s.FullName).Returns("Admin Approver");
            adminUser.Setup(s => s.Role).Returns(nameof(UserRole.Admin));

            var adminService = new PaymentService(
                repo,
                adminUser.Object,
                new PaymentCalculationHelper(),
                new AuditTrailHelper(),
                new DateRangeHelper()
            );

            // Attempt > requested amount -> throws
            var exceedAct = async () => await adminService.ApproveAsync(paymentId, new ApprovePaymentRequest(
                Comments: null,
                OverrideCategoryId: null,
                NewCategoryName: null,
                OverrideSiteId: null,
                Version: 1,
                ApprovedAmount: 5500m,
                UpdatedDescription: "Increased"
            ));
            await exceedAct.Should().ThrowAsync<DomainException>()
                .WithMessage("*cannot exceed the requested amount*");

            // Attempt <= 0 -> throws
            var zeroAct = async () => await adminService.ApproveAsync(paymentId, new ApprovePaymentRequest(
                Comments: null,
                OverrideCategoryId: null,
                NewCategoryName: null,
                OverrideSiteId: null,
                Version: 1,
                ApprovedAmount: 0m
            ));
            await zeroAct.Should().ThrowAsync<DomainException>()
                .WithMessage("*greater than zero*");

            // Approve with edited lower amount (4500 <= 5000) & edited description -> succeeds
            var approved = await adminService.ApproveAsync(paymentId, new ApprovePaymentRequest(
                Comments: "Negotiated discount applied",
                OverrideCategoryId: null,
                NewCategoryName: null,
                OverrideSiteId: null,
                Version: 1,
                ApprovedAmount: 4500m,
                UpdatedDescription: "Verified vendor challan after 500 discount"
            ));

            approved.Status.Should().Be(PaymentStatus.Approved);
            approved.RequestedAmount.Should().Be(5000m);
            approved.Amount.Should().Be(4500m);
            approved.ApprovedAmount.Should().Be(4500m);
            approved.Description.Should().Be("Verified vendor challan after 500 discount");

            var page = await adminService.GetListAsync(new PaymentFilterQuery(Year: 2026, Page: 1, PageSize: 20));
            page.Summary.TotalApprovedAmount.Should().Be(4500m);
        }
    }

    [Fact]
    public async Task PaymentService_Approve_LegacyVersionZero_ShouldSucceedWithMatchingVersionZero()
    {
        var dbName = Guid.NewGuid().ToString();
        var employeeId = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();

        using (var context = CreateInMemoryDbContext(dbName, _tenantA))
        {
            await context.PaymentRequests.AddAsync(new PaymentRequest
            {
                Id = paymentId,
                TenantId = _tenantA,
                UserId = employeeId,
                Title = "Tempo Freight Charges",
                Description = "Legacy seed row",
                RequestedAmount = 0m,
                Amount = 4200m,
                Currency = "INR",
                Category = "Transport & Logistics",
                ExpenseYear = 2026,
                StartDate = new DateOnly(2026, 11, 5),
                EndDate = new DateOnly(2026, 11, 5),
                Status = PaymentStatus.Pending,
                Version = 0u
            });
            await context.SaveChangesAsync();
        }

        using (var context = CreateInMemoryDbContext(dbName, _tenantA))
        {
            var repo = new PaymentRepository(context);
            var adminUser = new Mock<ICurrentUserService>();
            adminUser.Setup(s => s.TenantId).Returns(_tenantA);
            adminUser.Setup(s => s.UserId).Returns(adminId);
            adminUser.Setup(s => s.FullName).Returns("Admin Approver");
            adminUser.Setup(s => s.Role).Returns(nameof(UserRole.Admin));

            var service = new PaymentService(
                repo,
                adminUser.Object,
                new PaymentCalculationHelper(),
                new AuditTrailHelper(),
                new DateRangeHelper()
            );

            var result = await service.ApproveAsync(paymentId, new ApprovePaymentRequest(
                Comments: "Verified freight challan",
                OverrideCategoryId: null,
                NewCategoryName: null,
                OverrideSiteId: null,
                Version: 0u,
                ApprovedAmount: 3200m,
                UpdatedDescription: "Edited freight note"
            ));

            result.Status.Should().Be(PaymentStatus.Approved);
            result.RequestedAmount.Should().Be(4200m);
            result.ApprovedAmount.Should().Be(3200m);
            result.Amount.Should().Be(3200m);
            result.Description.Should().Be("Edited freight note");
            result.Version.Should().Be(1u);
        }
    }
}
