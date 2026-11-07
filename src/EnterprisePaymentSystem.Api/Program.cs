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
        path: Path.Combine(AppContext.BaseDirectory, "Logs", "app-.json"),
        rollingInterval: RollingInterval.Hour,
        fileSizeLimitBytes: 10 * 1024 * 1024, // 10 MB per file limit
        rollOnFileSizeLimit: true,
        retainedFileTimeLimit: TimeSpan.FromHours(12) // Strict 12-hour auto-delete policy
    ))
    // Cloud SaaS Sink: Asynchronous HTTP sink streaming Warnings and Errors to Better Stack / Axiom
    .WriteTo.Async(a => a.Http(
        requestUri: "https://in-automate.betterstack.com/api/v1/logs", // Better Stack / Axiom HTTP Sink Placeholder
        queueLimitBytes: 10 * 1024 * 1024,
        restrictedToMinimumLevel: LogEventLevel.Warning
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
    static string ParsePostgresUrl(string connectionStringOrUrl)
    {
        if (connectionStringOrUrl.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) ||
            connectionStringOrUrl.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            var uri = new Uri(connectionStringOrUrl);
            var userInfo = uri.UserInfo.Split(':');
            var username = userInfo[0];
            var password = userInfo.Length > 1 ? userInfo[1] : "";
            var database = uri.AbsolutePath.TrimStart('/');
            var port = uri.Port > 0 ? uri.Port : 5432;

            return $"Host={uri.Host};Port={port};Database={database};Username={username};Password={password};SSL Mode=Prefer;Trust Server Certificate=true;";
        }
        return connectionStringOrUrl;
    }

    var pgConnectionRaw = builder.Configuration.GetConnectionString("PostgreSqlConnection")
                          ?? builder.Configuration.GetConnectionString("DefaultConnection")
                          ?? Environment.GetEnvironmentVariable("DATABASE_URL");

    var pgConnection = !string.IsNullOrWhiteSpace(pgConnectionRaw)
        ? ParsePostgresUrl(pgConnectionRaw)
        : null;

    var usePostgres = builder.Configuration.GetValue<bool>("Database:UsePostgres")
                      || (!string.IsNullOrWhiteSpace(pgConnection) && pgConnection.Contains("Host="));

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
    builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
    builder.Services.AddTransient<IExcelExportService, ExcelExportService>();
    builder.Services.AddSingleton<IBackgroundExportQueue, BackgroundExportQueue>();
    builder.Services.AddHostedService<ExportBackgroundWorker>();

    // ==============================================================================
    // 7. EXPLICIT SECURITY, JWT & ROLE POLICIES
    // ==============================================================================
    builder.Services.AddSingleton<IPasswordHasher, PasswordHasher>();
    builder.Services.AddSingleton<IJwtTokenGenerator, JwtTokenGenerator>();

    var jwtSecret = builder.Configuration["Jwt:Secret"] ?? "SuperSecretKeyForEnterprisePaymentSystem2026!MustBeAtLeast32Bytes";
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
        options.AddPolicy("AllowFrontend", policy =>
        {
            var configuredOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>();
            if (configuredOrigins != null && configuredOrigins.Length > 0)
            {
                policy.WithOrigins(configuredOrigins)
                      .AllowAnyHeader()
                      .AllowAnyMethod()
                      .AllowCredentials();
            }
            else
            {
                policy.SetIsOriginAllowed(_ => true)
                      .AllowAnyHeader()
                      .AllowAnyMethod()
                      .AllowCredentials();
            }
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

    if (app.Environment.IsDevelopment() || true)
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
    app.MapGet("/", () => Results.Redirect("/swagger"));
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
        await DbInitializer.SeedAsync(context, hasher, logger);
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
