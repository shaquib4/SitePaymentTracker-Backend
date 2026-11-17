using System.Text;
using System.Threading.RateLimiting;
using EnterprisePaymentSystem.Api.Middleware;
using EnterprisePaymentSystem.Application.DTOs;
using EnterprisePaymentSystem.Application.Helpers;
using EnterprisePaymentSystem.Application.Interfaces;
using EnterprisePaymentSystem.Application.Interfaces.Repositories;
using EnterprisePaymentSystem.Application.Services;
using EnterprisePaymentSystem.Application.Validators;
using EnterprisePaymentSystem.Infrastructure.BackgroundJobs;
using EnterprisePaymentSystem.Infrastructure.Data;
using EnterprisePaymentSystem.Infrastructure.Repositories;
using EnterprisePaymentSystem.Infrastructure.Services;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Json;

// ==============================================================================
// 1. EARLY BOOTSTRAP LOGGING WITH SERILOG (FLUENT CONFIGURATION API)
// ==============================================================================
string logPath;
try
{
    var baseLogDir = Path.Combine(AppContext.BaseDirectory, "Logs");
    Directory.CreateDirectory(baseLogDir);
    logPath = Path.Combine(baseLogDir, "app-.json");
}
catch
{
    var tempLogDir = Path.Combine(Path.GetTempPath(), "Logs");
    Directory.CreateDirectory(tempLogDir);
    logPath = Path.Combine(tempLogDir, "app-.json");
}

Log.Logger = new LoggerConfiguration()
    // Noise Reduction: Global Information level, suppress ASP.NET Core & EF Core spam
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    // Local File Sink (Auto-Cleanup): Asynchronous, JSON format, hourly roll, 10MB limit, 12h retention
    .WriteTo.Async(a => a.File(
        formatter: new JsonFormatter(),
        path: logPath,
        rollingInterval: RollingInterval.Hour,
        fileSizeLimitBytes: 10 * 1024 * 1024, // 10 MB per file limit
        rollOnFileSizeLimit: true,
        retainedFileTimeLimit: TimeSpan.FromHours(12) // Strict 12-hour auto-delete policy
    ))
    .CreateLogger();

try
{
    Log.Information("Initializing Enterprise Payment Tracking API (Construction Multi-Tenant SaaS)...");

    var builder = WebApplication.CreateBuilder(args);

    // Hook Serilog into the Host Builder
    builder.Host.UseSerilog();

    // ==============================================================================
    // 2. EXPLICIT DATABASE CONTEXT CONFIGURATION (Supports Local & Render Cloud DB)
    // ==============================================================================
    static string NormalizeSupabaseConnectionString(string connectionStringOrUrl, string? rootCertificatePath)
    {
        string cs = connectionStringOrUrl.Trim();
        if (!string.IsNullOrWhiteSpace(rootCertificatePath) && !File.Exists(rootCertificatePath))
        {
            throw new InvalidOperationException("The PostgreSQL root CA certificate path configured by PGSSLROOTCERT does not exist.");
        }

        if (cs.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) ||
            cs.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            var uri = new Uri(cs);
            var userInfo = uri.UserInfo.Split(':');
            var username = Uri.UnescapeDataString(userInfo[0]);
            var password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : "";
            var database = uri.AbsolutePath.TrimStart('/');
            var port = uri.Port > 0 ? uri.Port : 5432;

            cs = $"Host={uri.Host};Port={port};Database={database};Username={username};Password={password};SSL Mode=VerifyFull;";
        }

        try
        {
            var csBuilder = new Npgsql.NpgsqlConnectionStringBuilder(cs);

            // Render IPv6 Fix: Supabase direct hosts (db.<project>.supabase.co) only route via IPv6,
            // which fails on Render free containers with "Network is unreachable (101)".
            // Route through Supabase IPv4 Pooler (aws-0-ap-southeast-2.pooler.supabase.com).
            if (!string.IsNullOrEmpty(csBuilder.Host) &&
                csBuilder.Host.StartsWith("db.", StringComparison.OrdinalIgnoreCase) &&
                csBuilder.Host.EndsWith(".supabase.co", StringComparison.OrdinalIgnoreCase))
            {
                var projectRef = csBuilder.Host.Substring(3, csBuilder.Host.IndexOf(".supabase.co", StringComparison.OrdinalIgnoreCase) - 3);
                csBuilder.Host = "aws-0-ap-southeast-2.pooler.supabase.com";
                csBuilder.Port = 5432;

                if (!string.IsNullOrEmpty(csBuilder.Username) && !csBuilder.Username.Contains('.'))
                {
                    csBuilder.Username = $"{csBuilder.Username}.{projectRef}";
                }

                csBuilder.SslMode = Npgsql.SslMode.VerifyFull;
            }

            // Require encryption and validate the server certificate for every PostgreSQL connection.
            csBuilder.SslMode = Npgsql.SslMode.VerifyFull;
            if (!string.IsNullOrWhiteSpace(rootCertificatePath))
            {
                csBuilder.RootCertificate = rootCertificatePath;
            }
            return csBuilder.ConnectionString;
        }
        catch
        {
            return cs;
        }
    }

    var pgConnectionRaw = builder.Configuration.GetConnectionString("PostgreSqlConnection")
                          ?? builder.Configuration.GetConnectionString("DefaultConnection")
                          ?? Environment.GetEnvironmentVariable("DATABASE_URL");
    var pgRootCertificatePath = builder.Configuration["PGSSLROOTCERT"];

    var pgConnection = !string.IsNullOrWhiteSpace(pgConnectionRaw)
        ? NormalizeSupabaseConnectionString(pgConnectionRaw, pgRootCertificatePath)
        : null;

    var usePostgres = builder.Configuration.GetValue<bool>("Database:UsePostgres")
                      || (!string.IsNullOrWhiteSpace(pgConnection) && pgConnection.Contains("Host="));

    if (usePostgres && string.IsNullOrWhiteSpace(pgConnection))
    {
        throw new InvalidOperationException("PostgreSQL is enabled but no connection string is configured. Set ConnectionStrings__PostgreSqlConnection or DATABASE_URL.");
    }

    if (usePostgres && !string.IsNullOrWhiteSpace(pgConnection))
    {
        builder.Services.AddDbContext<ApplicationDbContext>(options =>
            options.UseNpgsql(pgConnection, npgsql =>
            {
                npgsql.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName);
            }));
    }
    else
    {
        var sqliteConnection = builder.Configuration.GetConnectionString("SqliteConnection")
                               ?? "Data Source=kepayments.db";

        builder.Services.AddDbContext<ApplicationDbContext>(options =>
            options.UseSqlite(sqliteConnection));
    }

    builder.Services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<ApplicationDbContext>());

    // ==============================================================================
    // 3. EXPLICIT REPOSITORY LAYER REGISTRATIONS
    // ==============================================================================
    builder.Services.AddScoped<IPaymentRepository, PaymentRepository>();
    builder.Services.AddScoped<IUserRepository, UserRepository>();
    builder.Services.AddScoped<ITenantRepository, TenantRepository>();

    // ==============================================================================
    // 4. EXPLICIT COMMON LOGIC HELPERS
    // ==============================================================================
    builder.Services.AddSingleton<PaymentCalculationHelper>();
    builder.Services.AddSingleton<DateRangeHelper>();
    builder.Services.AddSingleton<AuditTrailHelper>();
    builder.Services.AddSingleton<CurrencyHelper>();

    // ==============================================================================
    // 5. EXPLICIT APPLICATION SERVICES
    // ==============================================================================
    builder.Services.AddScoped<IPaymentService, PaymentService>();
    builder.Services.AddScoped<IAuthService, AuthService>();

    // ==============================================================================
    // 6. EXPLICIT INFRASTRUCTURE & BACKGROUND WORKER REGISTRATIONS
    // ==============================================================================
    builder.Services.AddHttpContextAccessor();
    builder.Services.AddHttpClient("EmailClient");
    builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
    builder.Services.AddTransient<IExcelExportService, ExcelExportService>();
    builder.Services.AddTransient<IPdfBreakdownExportService, PdfBreakdownExportService>();
    builder.Services.AddTransient<IEmailService, EmailService>();
    builder.Services.AddScoped<IAuditReportJobProcessor, AuditReportJobProcessor>();
    builder.Services.AddSingleton<IBackgroundExportQueue, BackgroundExportQueue>();
    builder.Services.AddHostedService<ExportBackgroundWorker>();

    // ==============================================================================
    // 7. EXPLICIT SECURITY, JWT & ROLE POLICIES
    // ==============================================================================
    builder.Services.AddSingleton<IPasswordHasher, PasswordHasher>();
    builder.Services.AddSingleton<IJwtTokenGenerator, JwtTokenGenerator>();

    var jwtSecret = builder.Configuration["Jwt:Secret"];
    if (string.IsNullOrWhiteSpace(jwtSecret) || Encoding.UTF8.GetByteCount(jwtSecret) < 32)
    {
        throw new InvalidOperationException("Jwt:Secret must be configured with at least 32 bytes through a secret store or environment variable.");
    }
    var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "EnterprisePaymentSystem";
    var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "EnterprisePaymentSystemClient";

    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = jwtIssuer,
                ValidAudience = jwtAudience,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
                ClockSkew = TimeSpan.Zero
            };
            options.Events = new JwtBearerEvents
            {
                OnTokenValidated = async context =>
                {
                    var principal = context.Principal;
                    var userId = principal?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                                 ?? principal?.FindFirst("sub")?.Value;
                    var tenantId = principal?.FindFirst("tenant_id")?.Value;
                    var role = principal?.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value
                               ?? principal?.FindFirst("role")?.Value;

                    if (!Guid.TryParse(userId, out var parsedUserId) ||
                        !Guid.TryParse(tenantId, out var parsedTenantId) ||
                        !Enum.TryParse<EnterprisePaymentSystem.Domain.Enums.UserRole>(role, true, out var parsedRole))
                    {
                        context.Fail("The access token is missing valid user, tenant, or role claims.");
                        return;
                    }

                    // JWTs are self-contained, so check current account status and role on each
                    // request. This immediately cuts off deactivated users and stale elevated roles.
                    var db = context.HttpContext.RequestServices.GetRequiredService<ApplicationDbContext>();
                    var accountIsCurrent = await db.Users
                        .IgnoreQueryFilters()
                        .AnyAsync(user => user.Id == parsedUserId &&
                                          user.TenantId == parsedTenantId &&
                                          user.IsActive &&
                                          user.Role == parsedRole,
                            context.HttpContext.RequestAborted);
                    if (!accountIsCurrent)
                    {
                        context.Fail("The account is inactive or its access role has changed.");
                    }

                    return;
                }
            };
        });

    builder.Services.AddAuthorization(options =>
    {
        options.AddPolicy("RequireAdmin", policy => policy.RequireRole("Admin"));
        options.AddPolicy("RequireAccountant", policy => policy.RequireRole("Accountant", "Admin"));
        options.AddPolicy("RequireSupervisor", policy => policy.RequireRole("Supervisor", "Admin"));
    });

    // ==============================================================================
    // 8. EXPLICIT FLUENTVALIDATORS
    // ==============================================================================
    builder.Services.AddScoped<IValidator<CreatePaymentRequest>, CreatePaymentRequestValidator>();
    builder.Services.AddScoped<IValidator<UpdatePaymentRequest>, UpdatePaymentRequestValidator>();
    builder.Services.AddScoped<IValidator<ExportReportRequest>, ExportReportRequestValidator>();
    builder.Services.AddScoped<IValidator<AdminAuditPackRequest>, AdminAuditPackRequestValidator>();
    builder.Services.AddScoped<IValidator<PaymentFilterQuery>, PaymentFilterQueryValidator>();

    // ==============================================================================
    // 9. API GATEWAY: RATE LIMITING & CORS
    // ==============================================================================
    builder.Services.AddRateLimiter(options =>
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.AddPolicy("fixed-ip-policy", httpContext =>
        {
            var clientIp = httpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";
            return RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: clientIp,
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 120,
                    Window = TimeSpan.FromMinutes(1),
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    QueueLimit = 10
                });
        });
    });

    builder.Services.AddCors(options =>
    {
        var configuredOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
        if (builder.Environment.IsDevelopment())
        {
            configuredOrigins = configuredOrigins
                .Append("http://localhost:5173")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        else if (configuredOrigins.Length == 0)
        {
            throw new InvalidOperationException("Cors:AllowedOrigins must explicitly list trusted frontend origins outside Development.");
        }

        options.AddPolicy("AllowFrontend", policy =>
        {
            policy.WithOrigins(configuredOrigins)
                  .AllowAnyHeader()
                  .AllowAnyMethod();
        });
    });

    // ==============================================================================
    // 10. CONTROLLERS & SWAGGER OPENAPI WITH JWT
    // ==============================================================================
    builder.Services.AddControllers()
        .AddJsonOptions(options =>
        {
            options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
            options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
        });

    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(c =>
    {
        c.SwaggerDoc("v1", new OpenApiInfo
        {
            Title = "KE Payment Tracker API (Construction SaaS)",
            Version = "v1",
            Description = "Multi-Tenant Construction Payment Tracking API (Admin, Accountant, Supervisor Roles, Labour Wages, MB Profitability)"
        });

        c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
        {
            Name = "Authorization",
            Type = SecuritySchemeType.ApiKey,
            Scheme = "Bearer",
            BearerFormat = "JWT",
            In = ParameterLocation.Header,
            Description = "Enter 'Bearer' [space] and your JWT token.\r\nExample: \"Bearer eyJhbGciOi...\""
        });

        c.AddSecurityRequirement(new OpenApiSecurityRequirement
        {
            {
                new OpenApiSecurityScheme
                {
                    Reference = new OpenApiReference
                    {
                        Type = ReferenceType.SecurityScheme,
                        Id = "Bearer"
                    }
                },
                Array.Empty<string>()
            }
        });

        // Include XML documentation comments in Swagger UI
        var xmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
        var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
        if (File.Exists(xmlPath))
        {
            c.IncludeXmlComments(xmlPath);
        }
    });

    // ==============================================================================
    // 11. PIPELINE EXECUTION & MIDDLEWARE
    // ==============================================================================
    var app = builder.Build();

    // Serilog HTTP Request Logging (Captures method, path, status code, and latency)
    app.UseSerilogRequestLogging();

    app.UseMiddleware<ExceptionHandlingMiddleware>();

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI(c =>
        {
            c.SwaggerEndpoint("/swagger/v1/swagger.json", "KE Payment Tracker API v1");
            c.RoutePrefix = "swagger";
        });
    }

    app.UseCors("AllowFrontend");
    app.UseRateLimiter();
    app.UseAuthentication();
    app.UseAuthorization();

    app.MapControllers().RequireRateLimiting("fixed-ip-policy");

    // Cloud Health Probe & Root Redirect
    app.MapGet("/", () => Results.Redirect("/health"));
    app.MapGet("/health", () => Results.Ok(new
    {
        status = "Healthy",
        service = "KE Payment Tracker Backend API",
        version = "1.0.0",
        timestamp = DateTime.UtcNow
    }));

    // Multi-Tenant Seeding on Bootstrap
    using (var scope = app.Services.CreateScope())
    {
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
        await DbInitializer.SeedAsync(
            context,
            hasher,
            logger,
            seedDemoData: app.Environment.IsDevelopment(),
            demoPassword: builder.Configuration["Seed:DemoPassword"]);
        await DbInitializer.BootstrapAdminAsync(
            context,
            hasher,
            logger,
            enabled: builder.Configuration.GetValue<bool>("BootstrapAdmin:Enabled"),
            email: builder.Configuration["BootstrapAdmin:Email"],
            password: builder.Configuration["BootstrapAdmin:Password"],
            tenantIdValue: builder.Configuration["BootstrapAdmin:TenantId"],
            fullName: builder.Configuration["BootstrapAdmin:FullName"],
            phoneNumber: builder.Configuration["BootstrapAdmin:PhoneNumber"]);
    }

    Log.Information("Host configured successfully. Running application on configured ports...");
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Host terminated unexpectedly during application startup!");
}
finally
{
    Log.Information("Flushing Serilog buffers and shutting down cleanly...");
    Log.CloseAndFlush();
}

public partial class Program { }
