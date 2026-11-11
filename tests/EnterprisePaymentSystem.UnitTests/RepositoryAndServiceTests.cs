using System.Security.Claims;
using ClosedXML.Excel;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using EnterprisePaymentSystem.Api.Controllers;
using EnterprisePaymentSystem.Application.DTOs;
using EnterprisePaymentSystem.Application.Helpers;
using EnterprisePaymentSystem.Application.Interfaces;
using EnterprisePaymentSystem.Application.Services;
using EnterprisePaymentSystem.Application.Validators;
using EnterprisePaymentSystem.Domain.Entities;
using EnterprisePaymentSystem.Domain.Enums;
using EnterprisePaymentSystem.Domain.Exceptions;
using EnterprisePaymentSystem.Infrastructure.Data;
using EnterprisePaymentSystem.Infrastructure.Repositories;
using EnterprisePaymentSystem.Infrastructure.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
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

    [Fact]
    public void PdfBreakdownExportService_ShouldGenerateValidBreakdownPdf()
    {
        var service = new PdfBreakdownExportService();
        var summary = new PaymentPagedSummaryDto(
            GrandTotalAmount: 125000m,
            TotalApprovedAmount: 65000m,
            TotalCompletedAmount: 40000m,
            TotalPendingAmount: 15000m,
            TotalInfoRequestedAmount: 5000m,
            TotalRejectedAmount: 0m,
            TotalApprovedCount: 3,
            TotalCompletedCount: 2,
            TotalPendingCount: 2,
            TotalInfoRequestedCount: 1,
            TotalRejectedCount: 0,
            CategoryBreakdowns: new[]
            {
                new CategoryBreakdownDto("Material Supply", 3, 60000m),
                new CategoryBreakdownDto("Labour Wages", 2, 45000m)
            }
        );

        var bytes = service.GenerateBreakdownPdf(new PdfBreakdownContext(
            JobReference: "ABCD1234",
            TenantName: "Khan Enterprises",
            ReportTitle: "Q2 FY 2026 Breakdown Audit",
            PeriodLabel: "2026-04-01 to 2026-06-30 (FY 2026)",
            FilterSummary: "Status: Approved • Site: Metro Phase II",
            PreparedByName: "Shaquib Khan",
            PreparedByEmail: "admin@khanenterprises.com",
            SiteName: "Metro Phase II",
            StatusFilter: "Approved",
            CategoryFilter: null,
            ExcelFileName: "FY2026_Expenses.xlsx",
            TotalRecords: 8,
            TotalRequestedAmount: 132000m,
            TotalApprovedAmount: 105000m,
            Summary: summary,
            GeneratedAtUtc: DateTime.UtcNow
        ));

        bytes.Should().NotBeNull();
        bytes.Length.Should().BeGreaterThan(1500);
        // Starts with "%PDF-"
        bytes[0].Should().Be(0x25);
        bytes[1].Should().Be(0x50);
        bytes[2].Should().Be(0x44);
        bytes[3].Should().Be(0x46);
    }

    [Fact]
    public async Task AuditReportJobProcessor_ProcessAuditPackJobAsync_ShouldPersistPdfExcelAndDispatchEmail()
    {
        var dbName = Guid.NewGuid().ToString();
        var siteId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        using (var setupContext = CreateInMemoryDbContext(dbName, _tenantA))
        {
            await setupContext.Tenants.AddAsync(new Tenant
            {
                Id = _tenantA,
                Name = "Khan Enterprises",
                SubscriptionPlan = "Enterprise",
                Currency = "INR",
                IsActive = true
            });

            await setupContext.Sites.AddAsync(new Site
            {
                Id = siteId,
                TenantId = _tenantA,
                Name = "Expressway Package 4",
                Location = "Pune",
                IsActive = true
            });

            await setupContext.Users.AddAsync(new User
            {
                Id = employeeId,
                TenantId = _tenantA,
                FullName = "Site Engineer",
                Email = "engineer@khanenterprises.com",
                PhoneNumber = "+919000000001",
                SixDigitUserId = "300001"
            });

            // Inside Q1 range -> should match
            await setupContext.PaymentRequests.AddAsync(new PaymentRequest
            {
                Id = Guid.NewGuid(),
                TenantId = _tenantA,
                UserId = employeeId,
                SiteId = siteId,
                Title = "Cement Bags Batch #1",
                Description = "500 bags OPC 53 grade",
                Category = "Material Supply",
                RequestedAmount = 5000m,
                Amount = 4500m,
                Currency = "INR",
                ExpenseYear = 2026,
                StartDate = new DateOnly(2026, 2, 10),
                EndDate = new DateOnly(2026, 2, 12),
                Status = PaymentStatus.Approved,
                Version = 2u
            });

            // Outside Q1 range (Q3) -> must be excluded by period filter
            await setupContext.PaymentRequests.AddAsync(new PaymentRequest
            {
                Id = Guid.NewGuid(),
                TenantId = _tenantA,
                UserId = employeeId,
                SiteId = siteId,
                Title = "Bitumen Supply",
                Category = "Material Supply",
                RequestedAmount = 9000m,
                Amount = 9000m,
                Currency = "INR",
                ExpenseYear = 2026,
                StartDate = new DateOnly(2026, 8, 1),
                EndDate = new DateOnly(2026, 8, 2),
                Status = PaymentStatus.Approved,
                Version = 1u
            });

            await setupContext.AuditReportJobs.AddAsync(new AuditReportJob
            {
                Id = jobId,
                TenantId = _tenantA,
                RequestedByUserId = _userA,
                RequestedByName = "Shaquib Khan",
                RequestedByEmail = "admin@khanenterprises.com",
                ReportTitle = "Q1 Expressway Audit Pack",
                PeriodLabel = "2026-01-01 to 2026-03-31 (FY 2026)",
                FilterSummary = "Site: Expressway Package 4",
                Year = 2026,
                StartDate = new DateOnly(2026, 1, 1),
                EndDate = new DateOnly(2026, 3, 31),
                SiteId = siteId,
                Status = AuditReportStatus.Queued,
                DownloadToken = "secure-test-token-123"
            });

            await setupContext.SaveChangesAsync();
        }

        using (var workerContext = CreateInMemoryDbContext(dbName, null))
        {
            var repo = new PaymentRepository(workerContext);
            var pdfService = new PdfBreakdownExportService();
            var excelService = new ExcelExportService();
            var emailMock = new Mock<IEmailService>();
            emailMock
                .Setup(e => e.SendAuditReportReadyEmailAsync(It.IsAny<AuditEmailNotification>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new EmailDispatchResult(true, "Email sent to admin@khanenterprises.com"));

            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Email:FrontendUrl"] = "https://portal.khan.test"
                })
                .Build();

            var processor = new AuditReportJobProcessor(
                workerContext,
                repo,
                pdfService,
                excelService,
                emailMock.Object,
                config,
                NullLogger<AuditReportJobProcessor>.Instance);

            var jobRequest = new ExportJobRequest(
                JobId: jobId.ToString("N"),
                TenantId: _tenantA,
                TenantName: "Khan Enterprises",
                Year: 2026,
                RequestedByUserId: _userA,
                RequestedByEmail: "admin@khanenterprises.com",
                QueuedAtUtc: DateTime.UtcNow,
                IsAuditPack: true,
                AuditReportJobId: jobId,
                AuditFilter: new AuditExportFilter(
                    Year: 2026,
                    StartDate: new DateOnly(2026, 1, 1),
                    EndDate: new DateOnly(2026, 3, 31),
                    SiteId: siteId
                ),
                RequestedByName: "Shaquib Khan",
                BaseApiUrl: "https://api.khan.test",
                FrontendUrl: "https://portal.khan.test"
            );

            await processor.ProcessAuditPackJobAsync(jobRequest);

            var persisted = await workerContext.AuditReportJobs.IgnoreQueryFilters().FirstAsync(j => j.Id == jobId);
            persisted.Status.Should().Be(AuditReportStatus.Completed);
            persisted.TotalRecords.Should().Be(1);
            persisted.TotalRequestedAmount.Should().Be(5000m);
            persisted.TotalApprovedAmount.Should().Be(4500m);
            persisted.PdfBytes.Should().NotBeNull();
            persisted.PdfBytes!.Take(4).Should().Equal(new byte[] { 0x25, 0x50, 0x44, 0x46 }); // %PDF
            persisted.ExcelBytes.Should().NotBeNull();
            persisted.ExcelBytes!.Take(2).Should().Equal(new byte[] { 0x50, 0x4B }); // PK zip (.xlsx)
            persisted.EmailDispatched.Should().BeTrue();
            persisted.EmailDeliveryNote.Should().Contain("admin@khanenterprises.com");

            emailMock.Verify(e => e.SendAuditReportReadyEmailAsync(
                It.Is<AuditEmailNotification>(n =>
                    n.RecipientEmail == "admin@khanenterprises.com" &&
                    n.TotalRecords == 1 &&
                    n.PdfDownloadUrl.Contains("download/pdf?token=secure-test-token-123") &&
                    n.ExcelDownloadUrl.Contains("download/xlsx?token=secure-test-token-123")),
                It.IsAny<CancellationToken>()), Times.Once);
        }
    }

    [Fact]
    public async Task ChunkedAuditStreaming_ShouldStreamAllRowsWithTies_AndGenerateValidOpenXmlWorkbook()
    {
        var dbName = Guid.NewGuid().ToString();
        var employeeId = Guid.NewGuid();
        var sharedTimestamp = new DateTime(2026, 6, 15, 10, 0, 0, DateTimeKind.Utc);

        using (var setupContext = CreateInMemoryDbContext(dbName, _tenantA))
        {
            await setupContext.Users.AddAsync(new User
            {
                Id = employeeId,
                TenantId = _tenantA,
                FullName = "Site Engineer",
                Email = "site@khan.test",
                PhoneNumber = "9999999999",
                SixDigitUserId = "300002"
            });

            // Insert 5 rows where 3 share the exact same CreatedAtUtc (tests cursor tie-breaking)
            var list = new List<PaymentRequest>();
            for (int i = 0; i < 5; i++)
            {
                list.Add(new PaymentRequest
                {
                    Id = Guid.NewGuid(),
                    TenantId = _tenantA,
                    UserId = employeeId,
                    Title = $"Expense Item {i + 1}",
                    Category = "Material Supply",
                    RequestedAmount = 1000m,
                    Amount = 900m,
                    Currency = "INR",
                    ExpenseYear = 2026,
                    StartDate = new DateOnly(2026, 6, 1 + i),
                    EndDate = new DateOnly(2026, 6, 1 + i),
                    Status = PaymentStatus.Approved,
                    CreatedAtUtc = i < 3 ? sharedTimestamp : sharedTimestamp.AddMinutes(-i)
                });
            }
            await setupContext.PaymentRequests.AddRangeAsync(list);
            await setupContext.SaveChangesAsync();
        }

        using (var context = CreateInMemoryDbContext(dbName, null))
        {
            var repo = new PaymentRepository(context);
            var excelService = new ExcelExportService();

            var filter = new AuditExportFilter(Year: 2026);
            var summary = await repo.GetAuditExportSummaryAsync(_tenantA, filter);
            summary.TotalRecords.Should().Be(5);
            summary.TotalRequestedAmount.Should().Be(5000m);
            summary.TotalApprovedAmount.Should().Be(4500m);

            var chunkSizes = new List<int>();
            var allRows = new List<AuditExpenseRowDto>();
            await foreach (var chunk in repo.StreamAuditExpenseChunksAsync(_tenantA, filter, chunkSize: 2))
            {
                chunkSizes.Add(chunk.Count);
                allRows.AddRange(chunk);
            }

            chunkSizes.Should().Equal(new[] { 2, 2, 1 });
            allRows.Select(r => r.Id).Distinct().Should().HaveCount(5);

            var xlsxBytes = await excelService.GenerateChunkedAuditExpensesExcelAsync(
                repo.StreamAuditExpenseChunksAsync(_tenantA, filter, chunkSize: 2),
                "Khan Enterprises",
                2026,
                "Full Year FY 2026",
                "All Matching Expenses");

            using var workbookStream = new MemoryStream(xlsxBytes);
            using (var openXmlDoc = SpreadsheetDocument.Open(workbookStream, false))
            {
                var validator = new OpenXmlValidator();
                var validationErrors = validator.Validate(openXmlDoc)
                    .Select(e => $"{e.Path?.XPath}: {e.Description}")
                    .ToList();
                validationErrors.Should().BeEmpty();
            }

            using var workbook = new XLWorkbook(new MemoryStream(xlsxBytes));
            var ws = workbook.Worksheet("Expenses 2026");
            ws.Cell("A1").GetString().Should().Contain("Khan Enterprises");
            ws.Cell("B4").GetString().Should().Be("Employee");
            ws.Cell("M4").GetString().Should().Be("Status");
            ws.AutoFilter.IsEnabled.Should().BeTrue();
            ws.AutoFilter.Range.RangeAddress.ToStringRelative().Should().Be("A4:N9");
            ws.SheetView.SplitRow.Should().Be(4);
            ws.Cell("H5").GetDouble().Should().Be(1000d);
            ws.Cell("I5").GetDouble().Should().Be(900d);
            ws.Cell("G10").GetString().Should().Be("Filtered Totals:");
            ws.Cell("H10").FormulaA1.Should().Be("SUBTOTAL(109,H5:H9)");
            ws.Cell("I10").FormulaA1.Should().Be("SUBTOTAL(109,I5:I9)");

            // Verify EmployeeId filter isolates matching employee cleanly
            var filteredByOtherEmployee = await repo.GetAuditExportSummaryAsync(
                _tenantA,
                new AuditExportFilter(Year: 2026, EmployeeId: Guid.NewGuid()));
            filteredByOtherEmployee.TotalRecords.Should().Be(0);

            var filteredByTargetEmployee = await repo.GetAuditExportSummaryAsync(
                _tenantA,
                new AuditExportFilter(Year: 2026, EmployeeId: employeeId));
            filteredByTargetEmployee.TotalRecords.Should().Be(5);
        }
    }

    [Fact]
    public async Task ReportsController_DownloadPurgesBinaryFromDb_OnRelationalSqliteAndInMemory()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"ke_test_{Guid.NewGuid():N}.db");
        var jobId = Guid.NewGuid();
        var adminId = Guid.NewGuid();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;

        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(u => u.TenantId).Returns(_tenantA);
        currentUser.Setup(u => u.UserId).Returns(adminId);
        currentUser.Setup(u => u.Role).Returns(nameof(UserRole.Admin));
        currentUser.Setup(u => u.FullName).Returns("Shaquib Khan");
        currentUser.Setup(u => u.Email).Returns("shaquib@khan.test");

        try
        {
            using (var setupCtx = new ApplicationDbContext(options, currentUser.Object))
            {
                await setupCtx.Database.EnsureCreatedAsync();
                await setupCtx.Tenants.AddAsync(new Tenant
                {
                    Id = _tenantA,
                    Name = "Khan Enterprises",
                    Currency = "INR",
                    SubscriptionPlan = "Enterprise",
                    IsActive = true
                });
                await setupCtx.AuditReportJobs.AddAsync(new AuditReportJob
                {
                    Id = jobId,
                    TenantId = _tenantA,
                    RequestedByUserId = adminId,
                    RequestedByName = "Shaquib Khan",
                    RequestedByEmail = "shaquib@khan.test",
                    ReportTitle = "Relational Purge Audit",
                    PeriodLabel = "Full Year FY 2026",
                    FilterSummary = "Employee: Neha • Status: Approved",
                    Year = 2026,
                    PdfFileName = "FY2026_Breakdown.pdf",
                    ExcelFileName = "FY2026_Expenses.xlsx",
                    PdfBytes = new byte[] { 0x25, 0x50, 0x44, 0x46 },
                    ExcelBytes = new byte[] { 0x50, 0x4B, 0x03, 0x04 },
                    DownloadToken = "rel-tok-123",
                    Status = AuditReportStatus.Completed,
                    TotalRecords = 3,
                    TotalRequestedAmount = 3000m,
                    TotalApprovedAmount = 3000m
                });
                await setupCtx.SaveChangesAsync();
            }

            using (var dlCtx = new ApplicationDbContext(options, currentUser.Object))
            {
                var config = new ConfigurationBuilder().AddInMemoryCollection().Build();
                var controller = new ReportsController(
                    new Mock<IBackgroundExportQueue>().Object,
                    currentUser.Object,
                    dlCtx,
                    new EmailService(config, new Mock<IHttpClientFactory>().Object, NullLogger<EmailService>.Instance),
                    config,
                    new ExportReportRequestValidator(),
                    new AdminAuditPackRequestValidator());

                controller.ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext
                    {
                        User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, "Admin") }, "Test"))
                    }
                };

                var xlsxResult = await controller.DownloadAdminAuditFile(jobId, "xlsx");
                xlsxResult.Should().BeOfType<FileContentResult>();
                controller.Response.Headers["X-Binary-Purged"].ToString().Should().Be("true");
                controller.Response.Headers.CacheControl.ToString().Should().Contain("no-store");
            }

            using (var verifyCtx = new ApplicationDbContext(options, currentUser.Object))
            {
                var persisted = await verifyCtx.AuditReportJobs.AsNoTracking().FirstAsync(j => j.Id == jobId);
                persisted.ExcelBytes.Should().BeNull();
                persisted.ExcelDownloadedAtUtc.Should().NotBeNull();
                persisted.PdfBytes.Should().NotBeNull();
                persisted.ExcelFileName.Should().Be("FY2026_Expenses.xlsx");
                persisted.FilterSummary.Should().Be("Employee: Neha • Status: Approved");
            }
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [Fact]
    public async Task ReportsController_DownloadPurgesBinaryFromDb_AndRegenerateRequeuesSavedFilters()
    {
        var dbName = Guid.NewGuid().ToString();
        var jobId = Guid.NewGuid();
        var siteId = Guid.NewGuid();
        var adminId = Guid.NewGuid();

        using (var setupContext = CreateInMemoryDbContext(dbName, _tenantA))
        {
            await setupContext.Tenants.AddAsync(new Tenant
            {
                Id = _tenantA,
                Name = "Khan Enterprises",
                Currency = "INR",
                SubscriptionPlan = "Enterprise",
                IsActive = true
            });

            await setupContext.AuditReportJobs.AddAsync(new AuditReportJob
            {
                Id = jobId,
                TenantId = _tenantA,
                RequestedByUserId = adminId,
                RequestedByName = "Shaquib Khan",
                RequestedByEmail = "shaquib@khan.test",
                ReportTitle = "Q2 Highway Audit Pack",
                PeriodLabel = "2026-04-01 to 2026-06-30 (FY 2026)",
                FilterSummary = "Site: Highway • Status: Approved",
                Year = 2026,
                StartDate = new DateOnly(2026, 4, 1),
                EndDate = new DateOnly(2026, 6, 30),
                StatusFilter = "Approved",
                SiteId = siteId,
                SiteName = "Highway",
                PdfFileName = "Site_Highway_Breakdown.pdf",
                ExcelFileName = "Site_Highway_Expenses.xlsx",
                PdfBytes = new byte[] { 0x25, 0x50, 0x44, 0x46 },
                ExcelBytes = new byte[] { 0x50, 0x4B, 0x03, 0x04 },
                DownloadToken = "tok-123",
                Status = AuditReportStatus.Completed,
                TotalRecords = 12,
                TotalRequestedAmount = 50000m,
                TotalApprovedAmount = 45000m
            });
            await setupContext.SaveChangesAsync();
        }

        using (var context = CreateInMemoryDbContext(dbName, _tenantA))
        {
            var currentUser = new Mock<ICurrentUserService>();
            currentUser.Setup(u => u.TenantId).Returns(_tenantA);
            currentUser.Setup(u => u.UserId).Returns(adminId);
            currentUser.Setup(u => u.Role).Returns(nameof(UserRole.Admin));
            currentUser.Setup(u => u.FullName).Returns("Shaquib Khan");
            currentUser.Setup(u => u.Email).Returns("shaquib@khan.test");

            var queueMock = new Mock<IBackgroundExportQueue>();
            ExportJobRequest? queuedReq = null;
            queueMock
                .Setup(q => q.QueueExportJobAsync(It.IsAny<ExportJobRequest>(), It.IsAny<CancellationToken>()))
                .Callback<ExportJobRequest, CancellationToken>((r, _) => queuedReq = r)
                .Returns(ValueTask.CompletedTask);

            var config = new ConfigurationBuilder().AddInMemoryCollection().Build();
            var httpFactoryMock = new Mock<IHttpClientFactory>();
            var emailService = new EmailService(config, httpFactoryMock.Object, NullLogger<EmailService>.Instance);

            var controller = new ReportsController(
                queueMock.Object,
                currentUser.Object,
                context,
                emailService,
                config,
                new ExportReportRequestValidator(),
                new AdminAuditPackRequestValidator());

            var httpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                {
                    new Claim(ClaimTypes.Role, "Admin")
                }, "TestAuth"))
            };
            httpContext.Request.Scheme = "https";
            httpContext.Request.Host = new HostString("api.khan.test");
            controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

            // 1. Download PDF -> returns bytes & clears ONLY PdfBytes from DB
            var pdfResult = await controller.DownloadAdminAuditFile(jobId, "pdf");
            var fileResult = pdfResult.Should().BeOfType<FileContentResult>().Subject;
            fileResult.FileDownloadName.Should().Be("Site_Highway_Breakdown.pdf");
            fileResult.FileContents.Should().Equal(new byte[] { 0x25, 0x50, 0x44, 0x46 });

            var afterDownload = await context.AuditReportJobs.AsNoTracking().FirstAsync(j => j.Id == jobId);
            afterDownload.PdfBytes.Should().BeNull();
            afterDownload.PdfDownloadedAtUtc.Should().NotBeNull();
            afterDownload.ExcelBytes.Should().NotBeNull();
            afterDownload.PdfFileName.Should().Be("Site_Highway_Breakdown.pdf");
            afterDownload.StartDate.Should().Be(new DateOnly(2026, 4, 1));
            afterDownload.StatusFilter.Should().Be("Approved");

            // 2. Second PDF download returns 410 Gone
            var secondPdf = await controller.DownloadAdminAuditFile(jobId, "pdf");
            var goneResult = secondPdf.Should().BeOfType<ObjectResult>().Subject;
            goneResult.StatusCode.Should().Be(410);

            // 3. Regenerate re-queues using saved date range & filter metadata
            var regenResult = await controller.RegenerateAdminAuditPack(jobId);
            regenResult.Result.Should().BeOfType<AcceptedResult>();

            queuedReq.Should().NotBeNull();
            queuedReq!.IsAuditPack.Should().BeTrue();
            queuedReq.AuditFilter!.StartDate.Should().Be(new DateOnly(2026, 4, 1));
            queuedReq.AuditFilter.EndDate.Should().Be(new DateOnly(2026, 6, 30));
            queuedReq.AuditFilter.Status.Should().Be(PaymentStatus.Approved);
            queuedReq.AuditFilter.SiteId.Should().Be(siteId);

            // 4. Unconfigured EmailService reports honest Dispatched=false + IsConfigured=false
            emailService.GetConfigurationStatus().IsConfigured.Should().BeFalse();
        }
    }
}
