using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using RetailSuite.Api.Middleware;
using RetailSuite.Api.Seeding;
using RetailSuite.Api.MultiTenancy;
using RetailSuite.Infrastructure;
using RetailSuite.Infrastructure.Email;
using RetailSuite.Infrastructure.Modules.Customer.Services;
using RetailSuite.Infrastructure.Modules.Identity;
using RetailSuite.Infrastructure.Modules.Inventory.Services;
using RetailSuite.Infrastructure.Modules.Orders.Services;
using RetailSuite.Infrastructure.Modules.Tenant;
using RetailSuite.Infrastructure.Payments;
using RetailSuite.Infrastructure.Seeders;
using RetailSuite.Modules.Accounting.Services;
using RetailSuite.Shared;
using Serilog;
using System.Text;

// ---------------------------------------------------------------
// Configure Serilog before the host is built
// ---------------------------------------------------------------
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // ---------------------------------------------------------------
    // Serilog
    // ---------------------------------------------------------------
    builder.Host.UseSerilog((ctx, cfg) => cfg
        .MinimumLevel.Information()
        .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
        .MinimumLevel.Override("Microsoft.EntityFrameworkCore", Serilog.Events.LogEventLevel.Warning)
        .Enrich.FromLogContext()
        .Enrich.WithProperty("Application", "RetailSuite.Api")
        .Enrich.WithProperty("Environment", ctx.HostingEnvironment.EnvironmentName)
        .WriteTo.Console()
        .WriteTo.File(
            "logs/retailsuite-.log",
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 30,
            outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}"));

    // ---------------------------------------------------------------
    // Database
    // ---------------------------------------------------------------
    builder.Services.AddDbContext<RetailDbContext>(options =>
        options.UseSqlServer(builder.Configuration.GetConnectionString("Default")));

    // ---------------------------------------------------------------
    // Health Checks
    // ---------------------------------------------------------------
    builder.Services.AddHealthChecks()
        .AddDbContextCheck<RetailDbContext>();

    // ---------------------------------------------------------------
    // DI — infrastructure
    // ---------------------------------------------------------------
    builder.Services.AddHttpContextAccessor();
    builder.Services.AddScoped<ICurrentUserContext, CurrentUserContext>();
    builder.Services.AddScoped<ITenantContext, TenantContext>();
    builder.Services.AddScoped<InventoryService>();
    builder.Services.AddScoped<OrderService>();
    builder.Services.AddScoped<AccountingService>();
    builder.Services.AddScoped<PaymentService>();
    builder.Services.AddScoped<SaleService>();
    builder.Services.AddScoped<CustomerService>();

    // ---------------------------------------------------------------
    // Payment gateway configuration
    // ---------------------------------------------------------------
    // Configure Stripe options from appsettings
    builder.Services.Configure<StripeOptions>(builder.Configuration.GetSection(StripeOptions.Section));

    // Register Stripe-related services
    builder.Services.AddScoped<StripeWebhookHandler>();
    builder.Services.AddScoped<IStripeWebhookHandler>(sp => sp.GetRequiredService<StripeWebhookHandler>());

    // Use Stripe in production, FakePaymentGateway in development (will be resolved later)
    builder.Services.AddScoped<IPaymentGateway>(serviceProvider =>
    {
        var environment = serviceProvider.GetRequiredService<IWebHostEnvironment>();
        if (environment.IsProduction())
        {
            return serviceProvider.GetRequiredService<StripePaymentGateway>();
        }
        else
        {
            return serviceProvider.GetRequiredService<FakePaymentGateway>();
        }
    });

    // Register both implementations
    builder.Services.AddScoped<StripePaymentGateway>();
    builder.Services.AddScoped<FakePaymentGateway>();

    // Email service (configure smtp settings in appsettings.json)
    builder.Services.AddScoped<IEmailService, SmtpEmailService>();

    // ---------------------------------------------------------------
    // Authorization policies
    // ---------------------------------------------------------------
    builder.Services.AddAuthorization(options =>
    {
        options.AddPolicy("SuperAdminOnly",    policy => policy.RequireRole("SuperAdmin"));
        options.AddPolicy("SuperOrAdmin",      policy => policy.RequireRole("SuperAdmin", "Admin"));
        options.AddPolicy("AdminOnly",         policy => policy.RequireRole("Admin"));
        options.AddPolicy("StaffOrAdmin",      policy => policy.RequireRole("Admin", "Staff"));
        options.AddPolicy("CustomerOnly",      policy => policy.RequireRole("Customer"));
    });

    // ---------------------------------------------------------------
    // JWT Authentication
    // ---------------------------------------------------------------
    var jwtSettings = builder.Configuration.GetSection("Jwt");

    builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme    = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer           = true,
            ValidateAudience         = true,
            ValidateLifetime         = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer              = jwtSettings["Issuer"],
            ValidAudience            = jwtSettings["Audience"],
            IssuerSigningKey         = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwtSettings["Key"] ?? ""))
        };
    });

    // ---------------------------------------------------------------
    // MVC + Swagger
    // ---------------------------------------------------------------
    builder.Services.AddControllers()
        .AddJsonOptions(o =>
        {
            // Prevent circular-reference exceptions when EF nav-props point back to parent
            o.JsonSerializerOptions.ReferenceHandler =
                System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
        });
    builder.Services.AddEndpointsApiExplorer();

    builder.Services.AddSwaggerGen(c =>
    {
        c.SwaggerDoc("v1", new OpenApiInfo { Title = "RetailSuite API", Version = "v1" });

        c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
        {
            Name        = "Authorization",
            Type        = SecuritySchemeType.Http,
            Scheme      = "Bearer",
            BearerFormat = "JWT",
            In          = ParameterLocation.Header,
            Description = "Paste your JWT token here (without 'Bearer ' prefix)."
        });

        c.AddSecurityRequirement(new OpenApiSecurityRequirement
        {
            {
                new OpenApiSecurityScheme
                {
                    Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
                },
                Array.Empty<string>()
            }
        });
    });

    var app = builder.Build();

    // ---------------------------------------------------------------
    // Seed super-admin (idempotent — no-op if already exists)
    // ---------------------------------------------------------------
    await SuperAdminSeeder.SeedAsync(app.Services);

    // ---------------------------------------------------------------
    // Seed demo data (idempotent — no-op if demo tenant exists)
    // ---------------------------------------------------------------
    await using (var scope = app.Services.CreateAsyncScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<RetailDbContext>();
        await DemoDataSeeder.SeedDemoDataAsync(db);
    }

    // ---------------------------------------------------------------
    // Middleware pipeline
    // ---------------------------------------------------------------
    app.UseSwagger();
    app.UseSwaggerUI();

    app.UseStaticFiles();   // serves /uploads/* and other static assets

    app.MapGet("/", () => "RetailSuite API Running 🚀");

    app.MapHealthChecks("/health");

    app.UseMiddleware<ExceptionMiddleware>();

    app.UseSerilogRequestLogging();

    app.UseAuthentication();
    app.UseAuthorization();

    app.MapControllers();

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application failed to start.");
}
finally
{
    Log.CloseAndFlush();
}
