using EnterprisePaymentSystem.Application.Interfaces;
using EnterprisePaymentSystem.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace EnterprisePaymentSystem.Infrastructure.Data;

public class ApplicationDbContext : DbContext, IApplicationDbContext
{
    private readonly ICurrentUserService _currentUserService;

    public ApplicationDbContext(
        DbContextOptions<ApplicationDbContext> options,
        ICurrentUserService currentUserService) : base(options)
    {
        _currentUserService = currentUserService;
    }

    public Guid? CurrentTenantId => _currentUserService?.TenantId;

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<User> Users => Set<User>();
    public DbSet<PaymentRequest> PaymentRequests => Set<PaymentRequest>();
    public DbSet<PaymentAuditLog> PaymentAuditLogs => Set<PaymentAuditLog>();
    public DbSet<ExpenseCategory> Categories => Set<ExpenseCategory>();
    public DbSet<Site> Sites => Set<Site>();
    public DbSet<Labourer> Labourers => Set<Labourer>();
    public DbSet<MeasurementBook> MeasurementBooks => Set<MeasurementBook>();
    public DbSet<PeriodAccessRequest> PeriodAccessRequests => Set<PeriodAccessRequest>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Global Query Filters for Multi-Tenancy Isolation
        modelBuilder.Entity<PaymentRequest>().HasQueryFilter(e =>
            CurrentTenantId == null || e.TenantId == CurrentTenantId);

        modelBuilder.Entity<PaymentAuditLog>().HasQueryFilter(e =>
            CurrentTenantId == null || e.TenantId == CurrentTenantId);

        modelBuilder.Entity<User>().HasQueryFilter(e =>
            CurrentTenantId == null || e.TenantId == CurrentTenantId);

        modelBuilder.Entity<ExpenseCategory>().HasQueryFilter(e =>
            CurrentTenantId == null || e.TenantId == CurrentTenantId);

        modelBuilder.Entity<Site>().HasQueryFilter(e =>
            CurrentTenantId == null || e.TenantId == CurrentTenantId);

        modelBuilder.Entity<Labourer>().HasQueryFilter(e =>
            (CurrentTenantId == null || e.TenantId == CurrentTenantId) && !e.IsDeleted);

        modelBuilder.Entity<MeasurementBook>().HasQueryFilter(e =>
            CurrentTenantId == null || e.TenantId == CurrentTenantId);

        modelBuilder.Entity<PeriodAccessRequest>().HasQueryFilter(e =>
            CurrentTenantId == null || e.TenantId == CurrentTenantId);

        // Tenant Configuration
        modelBuilder.Entity<Tenant>(b =>
        {
            b.HasKey(t => t.Id);
            b.Property(t => t.Name).IsRequired().HasMaxLength(150);
            b.Property(t => t.SubscriptionPlan).HasMaxLength(50);
            b.Property(t => t.Currency).HasMaxLength(3);
        });

        // User Configuration
        modelBuilder.Entity<User>(b =>
        {
            b.HasKey(u => u.Id);
            b.Property(u => u.Email).IsRequired().HasMaxLength(150);
            b.Property(u => u.FullName).IsRequired().HasMaxLength(100);
            b.Property(u => u.PhoneNumber).IsRequired().HasMaxLength(20);
            b.Property(u => u.SixDigitUserId).IsRequired().HasMaxLength(6);
            b.Property(u => u.PasswordHash).IsRequired();
            b.Property(u => u.Department).HasMaxLength(100);

            b.HasIndex(u => new { u.TenantId, u.Email }).IsUnique();
            b.HasIndex(u => new { u.TenantId, u.SixDigitUserId }).IsUnique();
        });

        // ExpenseCategory Configuration
        modelBuilder.Entity<ExpenseCategory>(b =>
        {
            b.HasKey(c => c.Id);
            b.Property(c => c.Name).IsRequired().HasMaxLength(100);
            b.HasIndex(c => new { c.TenantId, c.Name }).IsUnique();
        });

        // Site Configuration
        modelBuilder.Entity<Site>(b =>
        {
            b.HasKey(s => s.Id);
            b.Property(s => s.Name).IsRequired().HasMaxLength(150);
            b.Property(s => s.Location).HasMaxLength(250);
            b.HasIndex(s => new { s.TenantId, s.Name });
        });

        // Labourer Configuration
        modelBuilder.Entity<Labourer>(b =>
        {
            b.HasKey(l => l.Id);
            b.Property(l => l.Name).IsRequired().HasMaxLength(100);
            b.Property(l => l.DailyWageRate).HasPrecision(14, 2);
            b.HasOne(l => l.Supervisor)
             .WithMany(u => u.ManagedLabourers)
             .HasForeignKey(l => l.SupervisorId)
             .OnDelete(DeleteBehavior.Restrict);
            b.HasOne(l => l.Site)
             .WithMany()
             .HasForeignKey(l => l.SiteId)
             .IsRequired(false)
             .OnDelete(DeleteBehavior.SetNull);
            b.HasOne(l => l.CreatedByUser)
             .WithMany()
             .HasForeignKey(l => l.CreatedByUserId)
             .IsRequired(false)
             .OnDelete(DeleteBehavior.SetNull);
        });

        // MeasurementBook Configuration
        modelBuilder.Entity<MeasurementBook>(b =>
        {
            b.HasKey(m => m.Id);
            b.Property(m => m.TotalEstimatedValue).HasPrecision(14, 2);
            b.HasOne(m => m.Site)
             .WithMany(s => s.MeasurementBooks)
             .HasForeignKey(m => m.SiteId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // PaymentRequest Configuration
        modelBuilder.Entity<PaymentRequest>(b =>
        {
            b.HasKey(p => p.Id);
            b.Property(p => p.Title).IsRequired().HasMaxLength(150);
            b.Property(p => p.Description).HasMaxLength(1000);
            b.Property(p => p.Amount).HasPrecision(14, 2);
            b.Property(p => p.Currency).IsRequired().HasMaxLength(3);
            b.Property(p => p.Category).IsRequired().HasMaxLength(100);

            b.Property(p => p.Version).IsConcurrencyToken();

            b.HasIndex(p => new { p.TenantId, p.ExpenseYear });
            b.HasIndex(p => new { p.TenantId, p.Status });
            b.HasIndex(p => p.UserId);
            b.HasIndex(p => p.SiteId);
            b.HasIndex(p => p.LabourerId);

            b.HasOne(p => p.User)
             .WithMany(u => u.PaymentRequests)
             .HasForeignKey(p => p.UserId)
             .IsRequired(false)
             .OnDelete(DeleteBehavior.Restrict);

            b.HasOne(p => p.CategoryEntity)
             .WithMany(c => c.PaymentRequests)
             .HasForeignKey(p => p.CategoryId)
             .IsRequired(false)
             .OnDelete(DeleteBehavior.SetNull);

            b.HasOne(p => p.Labourer)
             .WithMany(l => l.WagePayments)
             .HasForeignKey(p => p.LabourerId)
             .IsRequired(false)
             .OnDelete(DeleteBehavior.SetNull);

            b.HasOne(p => p.Site)
             .WithMany(s => s.PaymentRequests)
             .HasForeignKey(p => p.SiteId)
             .IsRequired(false)
             .OnDelete(DeleteBehavior.SetNull);

            b.HasMany(p => p.AuditLogs)
             .WithOne(a => a.PaymentRequest)
             .HasForeignKey(a => a.PaymentRequestId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // PaymentAuditLog Configuration
        modelBuilder.Entity<PaymentAuditLog>(b =>
        {
            b.HasKey(a => a.Id);
            b.Property(a => a.Id).ValueGeneratedOnAdd();
            b.Property(a => a.Action).IsRequired().HasMaxLength(50);
            b.Property(a => a.PerformedByUserName).HasMaxLength(100);
            b.Property(a => a.Comments).HasMaxLength(500);

            b.HasIndex(a => new { a.TenantId, a.PaymentRequestId });
        });
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var tenantId = CurrentTenantId;

        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.State == EntityState.Added)
            {
                if (tenantId.HasValue && entry.Entity.GetType().GetProperty("TenantId") != null)
                {
                    var prop = entry.Entity.GetType().GetProperty("TenantId");
                    var currentVal = prop?.GetValue(entry.Entity);
                    if (currentVal == null || (Guid)currentVal == Guid.Empty)
                    {
                        prop?.SetValue(entry.Entity, tenantId.Value);
                    }
                }
            }
        }

        return base.SaveChangesAsync(cancellationToken);
    }
}
