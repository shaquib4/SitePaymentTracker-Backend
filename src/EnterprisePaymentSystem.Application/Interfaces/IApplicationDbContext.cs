using Microsoft.EntityFrameworkCore;
using EnterprisePaymentSystem.Domain.Entities;

namespace EnterprisePaymentSystem.Application.Interfaces;

public interface IApplicationDbContext
{
    DbSet<Tenant> Tenants { get; }
    DbSet<User> Users { get; }
    DbSet<PaymentRequest> PaymentRequests { get; }
    DbSet<PaymentAuditLog> PaymentAuditLogs { get; }
    DbSet<ExpenseCategory> Categories { get; }
    DbSet<Site> Sites { get; }
    DbSet<Labourer> Labourers { get; }
    DbSet<MeasurementBook> MeasurementBooks { get; }
    DbSet<PeriodAccessRequest> PeriodAccessRequests { get; }
    DbSet<AuditReportJob> AuditReportJobs { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
