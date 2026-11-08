using EnterprisePaymentSystem.Application.Interfaces;
using EnterprisePaymentSystem.Domain.Entities;
using EnterprisePaymentSystem.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EnterprisePaymentSystem.Infrastructure.Data;

public static class DbInitializer
{
    public static readonly Guid AcmeTenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid GlobexTenantId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    public static readonly Guid AcmeAdminId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    public static readonly Guid AcmeAccountantId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    public static readonly Guid AcmeSupervisorId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    public static readonly Guid SiteAId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    public static readonly Guid SiteBId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    public static async Task SeedAsync(ApplicationDbContext context, IPasswordHasher hasher, ILogger logger)
    {
        await context.Database.EnsureCreatedAsync();

        if (await context.Tenants.IgnoreQueryFilters().AnyAsync())
        {
            // Ensure OfficeWorker user exists in Khan Enterprises even if already seeded
            var hasOfficeWorker = await context.Users.IgnoreQueryFilters().AnyAsync(u => u.Role == UserRole.OfficeWorker);
            if (!hasOfficeWorker)
            {
                var tenant = await context.Tenants.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.Name == "Khan Enterprises")
                             ?? await context.Tenants.IgnoreQueryFilters().FirstOrDefaultAsync();
                if (tenant != null)
                {
                    var pwdHash = hasher.HashPassword("Password123!");
                    var officeWorker = new User
                    {
                        Id = Guid.NewGuid(),
                        TenantId = tenant.Id,
                        Email = "officeworker@khanenterprises.com",
                        FullName = "Neha Sharma",
                        PhoneNumber = "+919877001122",
                        SixDigitUserId = "400004",
                        Role = UserRole.OfficeWorker,
                        Department = "Administration & Support",
                        PasswordHash = pwdHash,
                        IsActive = true,
                        CreatedAtUtc = DateTime.UtcNow.AddMonths(-6)
                    };
                    context.Users.Add(officeWorker);

                    var p1 = new PaymentRequest
                    {
                        Id = Guid.NewGuid(),
                        TenantId = tenant.Id,
                        UserId = officeWorker.Id,
                        Category = "Office Supplies & IT",
                        Title = "Stationery, Printer Toners & Office Paper",
                        Description = "Monthly administrative supplies for head office",
                        Amount = 14500.00m,
                        Currency = "INR",
                        ExpenseYear = 2026,
                        StartDate = new DateOnly(2026, 1, 5),
                        EndDate = new DateOnly(2026, 1, 20),
                        Status = PaymentStatus.Approved,
                        ApprovalComments = "Approved against GST invoice",
                        CreatedAtUtc = new DateTime(2026, 1, 21, 10, 0, 0, DateTimeKind.Utc),
                        Version = 2
                    };
                    p1.AuditLogs.Add(new PaymentAuditLog
                    {
                        TenantId = tenant.Id,
                        PaymentRequestId = p1.Id,
                        Action = "Approved",
                        NewStatus = PaymentStatus.Approved,
                        PerformedByUserId = AcmeAdminId,
                        PerformedByUserName = "Shaquib Khan",
                        Comments = "Approved by Executive Management."
                    });

                    var p2 = new PaymentRequest
                    {
                        Id = Guid.NewGuid(),
                        TenantId = tenant.Id,
                        UserId = officeWorker.Id,
                        Category = "Travel & Client Meeting",
                        Title = "Client Coordination & Site Travel Reimbursement",
                        Description = "Cab and fuel allowance for Gurugram Pier 14 inspection visit",
                        Amount = 6800.00m,
                        Currency = "INR",
                        ExpenseYear = 2026,
                        StartDate = new DateOnly(2026, 2, 2),
                        EndDate = new DateOnly(2026, 2, 10),
                        Status = PaymentStatus.Pending,
                        CreatedAtUtc = new DateTime(2026, 2, 11, 14, 0, 0, DateTimeKind.Utc),
                        Version = 1
                    };
                    p2.AuditLogs.Add(new PaymentAuditLog
                    {
                        TenantId = tenant.Id,
                        PaymentRequestId = p2.Id,
                        Action = "SubmittedForApproval",
                        NewStatus = PaymentStatus.Pending,
                        PerformedByUserId = officeWorker.Id,
                        PerformedByUserName = "Neha Sharma",
                        Comments = "Submitted travel expense claim."
                    });

                    // Historical (2025) payment request to test past year / historical filtering
                    var p3 = new PaymentRequest
                    {
                        Id = Guid.NewGuid(),
                        TenantId = tenant.Id,
                        UserId = officeWorker.Id,
                        Category = "Office Utilities",
                        Title = "Annual High-Speed Broadband & Cloud Telephony",
                        Description = "FY 2025 Office Internet & VoIP line setup",
                        Amount = 24000.00m,
                        Currency = "INR",
                        ExpenseYear = 2025,
                        StartDate = new DateOnly(2025, 4, 1),
                        EndDate = new DateOnly(2025, 4, 30),
                        Status = PaymentStatus.Completed,
                        ApprovalComments = "Paid via corporate banking",
                        CreatedAtUtc = new DateTime(2025, 5, 2, 11, 0, 0, DateTimeKind.Utc),
                        Version = 3
                    };
                    p3.AuditLogs.Add(new PaymentAuditLog
                    {
                        TenantId = tenant.Id,
                        PaymentRequestId = p3.Id,
                        Action = "Completed",
                        NewStatus = PaymentStatus.Completed,
                        PerformedByUserId = AcmeAdminId,
                        PerformedByUserName = "Shaquib Khan",
                        Comments = "Paid via bank transfer."
                    });

                    context.PaymentRequests.AddRange(p1, p2, p3);
                    await context.SaveChangesAsync();
                    logger.LogInformation("Seeded OfficeWorker user Neha Sharma (ID: 400004) with current and historical payment records.");
                }
            }

            logger.LogInformation("Database already seeded. Skipping initial data seed.");
            return;
        }

        logger.LogInformation("Seeding Construction Multi-Tenant system with Admin, Accountant, Supervisor roles, dynamic categories & sites...");

        // 1. Tenants (Construction enterprise)
        var acmeTenant = new Tenant
        {
            Id = AcmeTenantId,
            Name = "Khan Enterprises",
            SubscriptionPlan = "Enterprise Unlimited",
            Currency = "INR",
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow.AddMonths(-6)
        };

        var globexTenant = new Tenant
        {
            Id = GlobexTenantId,
            Name = "Apex Builders & Contractors",
            SubscriptionPlan = "Enterprise Unlimited",
            Currency = "INR",
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow.AddMonths(-4)
        };

        context.Tenants.AddRange(acmeTenant, globexTenant);

        // 2. Dynamic Categories for BuildCon
        var catCement = new ExpenseCategory { Id = Guid.NewGuid(), TenantId = AcmeTenantId, Name = "Cement" };
        var catSariya = new ExpenseCategory { Id = Guid.NewGuid(), TenantId = AcmeTenantId, Name = "Sariya (Rebar)" };
        var catSalary = new ExpenseCategory { Id = Guid.NewGuid(), TenantId = AcmeTenantId, Name = "Labour Wages" };
        var catTransport = new ExpenseCategory { Id = Guid.NewGuid(), TenantId = AcmeTenantId, Name = "Transport & Logistics" };
        var catEquipment = new ExpenseCategory { Id = Guid.NewGuid(), TenantId = AcmeTenantId, Name = "Equipment & Machinery" };

        context.Categories.AddRange(catCement, catSariya, catSalary, catTransport, catEquipment);

        // 3. Project Sites
        var siteA = new Site
        {
            Id = SiteAId,
            TenantId = AcmeTenantId,
            Name = "Metro Phase 4 - Station 12",
            Location = "Sector 62, Noida",
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow.AddMonths(-5)
        };

        var siteB = new Site
        {
            Id = SiteBId,
            TenantId = AcmeTenantId,
            Name = "Skyline Heights Tower B",
            Location = "Whitefield, Bengaluru",
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow.AddMonths(-3)
        };

        context.Sites.AddRange(siteA, siteB);

        // 4. Users (Admin, Accountant, Supervisor) with 6-digit IDs and Phone numbers
        var defaultPasswordHash = hasher.HashPassword("Password123!");

        var adminUser = new User
        {
            Id = AcmeAdminId,
            TenantId = AcmeTenantId,
            Email = "khanshaquib21@gmail.com",
            FullName = "Shaquib Khan",
            PhoneNumber = "+919876543210",
            SixDigitUserId = "749201",
            Role = UserRole.Admin,
            Department = "Executive Management",
            PasswordHash = defaultPasswordHash,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow.AddMonths(-6)
        };

        var accountantUser = new User
        {
            Id = AcmeAccountantId,
            TenantId = AcmeTenantId,
            Email = "accountant@buildcon.com",
            FullName = "Pooja Verma",
            PhoneNumber = "+919811223344",
            SixDigitUserId = "200002",
            Role = UserRole.Accountant,
            Department = "Finance & Accounts",
            PasswordHash = defaultPasswordHash,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow.AddMonths(-6)
        };

        var supervisorUser = new User
        {
            Id = AcmeSupervisorId,
            TenantId = AcmeTenantId,
            Email = "supervisor@buildcon.com",
            FullName = "Suresh Kumar",
            PhoneNumber = "+919988776655",
            SixDigitUserId = "300003",
            Role = UserRole.Supervisor,
            Department = "Site Operations",
            PasswordHash = defaultPasswordHash,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow.AddMonths(-6)
        };

        context.Users.AddRange(adminUser, accountantUser, supervisorUser);

        // 5. Labourers managed by Suresh
        var labourer1 = new Labourer
        {
            Id = Guid.NewGuid(),
            TenantId = AcmeTenantId,
            SupervisorId = AcmeSupervisorId,
            Name = "Ramesh Yadav",
            DailyWageRate = 650.00m,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow.AddMonths(-2)
        };

        var labourer2 = new Labourer
        {
            Id = Guid.NewGuid(),
            TenantId = AcmeTenantId,
            SupervisorId = AcmeSupervisorId,
            Name = "Mohan Lal",
            DailyWageRate = 700.00m,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow.AddMonths(-2)
        };

        context.Labourers.AddRange(labourer1, labourer2);

        // 6. Measurement Book for Metro Site
        var mb1 = new MeasurementBook
        {
            Id = Guid.NewGuid(),
            TenantId = AcmeTenantId,
            SiteId = SiteAId,
            Month = 1,
            Year = 2026,
            TotalEstimatedValue = 500000.00m, // ₹5,00,000 estimated MB value
            UploadedAt = new DateTime(2026, 1, 31, 18, 0, 0, DateTimeKind.Utc)
        };
        context.MeasurementBooks.Add(mb1);

        // 7. Payment Requests
        var paymentMaterial = new PaymentRequest
        {
            Id = Guid.NewGuid(),
            TenantId = AcmeTenantId,
            UserId = AcmeSupervisorId,
            SiteId = SiteAId,
            CategoryId = catCement.Id,
            Category = "Cement",
            Title = "Ultratech Cement - 200 Bags for Metro Station Slab",
            Description = "Urgent cement delivery for foundation slab cast",
            Amount = 78000.00m,
            Currency = "INR",
            ExpenseYear = 2026,
            StartDate = new DateOnly(2026, 1, 10),
            EndDate = new DateOnly(2026, 1, 12),
            Status = PaymentStatus.Approved,
            InvoiceNumber = "ULTRA-8891",
            ApprovalComments = "Approved against structural engineering quote.",
            CreatedAtUtc = new DateTime(2026, 1, 12, 10, 0, 0, DateTimeKind.Utc),
            Version = 2
        };
        paymentMaterial.AuditLogs.Add(new PaymentAuditLog
        {
            TenantId = AcmeTenantId,
            PaymentRequestId = paymentMaterial.Id,
            Action = "SubmittedForApproval",
            NewStatus = PaymentStatus.Pending,
            PerformedByUserId = AcmeSupervisorId,
            PerformedByUserName = "Suresh Kumar",
            Comments = "Cement delivery claim"
        });
        paymentMaterial.AuditLogs.Add(new PaymentAuditLog
        {
            TenantId = AcmeTenantId,
            PaymentRequestId = paymentMaterial.Id,
            Action = "Approved",
            PreviousStatus = PaymentStatus.Pending,
            NewStatus = PaymentStatus.Approved,
            PerformedByUserId = AcmeAdminId,
            PerformedByUserName = "Rajesh Sharma",
            Comments = "Approved against structural engineering quote."
        });

        var paymentWage = new PaymentRequest
        {
            Id = Guid.NewGuid(),
            TenantId = AcmeTenantId,
            UserId = AcmeSupervisorId,
            SiteId = SiteAId,
            LabourerId = labourer1.Id,
            CategoryId = catSalary.Id,
            Category = "Labour Wages",
            Title = "Weekly Wages - Ramesh Yadav (12 Days)",
            Description = "Bar-bending and rebar tying wage payout",
            Amount = 7800.00m,
            Currency = "INR",
            ExpenseYear = 2026,
            StartDate = new DateOnly(2026, 1, 1),
            EndDate = new DateOnly(2026, 1, 15),
            Status = PaymentStatus.Pending,
            CreatedAtUtc = new DateTime(2026, 1, 16, 9, 30, 0, DateTimeKind.Utc),
            Version = 1
        };
        paymentWage.AuditLogs.Add(new PaymentAuditLog
        {
            TenantId = AcmeTenantId,
            PaymentRequestId = paymentWage.Id,
            Action = "SubmittedForApproval",
            NewStatus = PaymentStatus.Pending,
            PerformedByUserId = AcmeSupervisorId,
            PerformedByUserName = "Suresh Kumar",
            Comments = "Wage payout muster roll"
        });

        context.PaymentRequests.AddRange(paymentMaterial, paymentWage);

        await context.SaveChangesAsync();
        logger.LogInformation("Database successfully seeded for Construction Payment Tracking!");
    }
}
