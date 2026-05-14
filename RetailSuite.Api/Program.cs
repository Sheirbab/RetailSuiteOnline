using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
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
using RetailSuite.Infrastructure.Modules.Barcodes.Services;
using RetailSuite.Infrastructure.Modules.Identity.Services;
using RetailSuite.Infrastructure.Modules.Images.Services;
using RetailSuite.Infrastructure.Modules.Inventory.Services;
using RetailSuite.Infrastructure.Modules.Orders.Services;
using RetailSuite.Infrastructure.Modules.Receiving.Services;
using RetailSuite.Infrastructure.Modules.Subscriptions.Services;
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

    builder.Services.Configure<ImageStorageOptions>(builder.Configuration.GetSection(ImageStorageOptions.Section));
    builder.Services.AddScoped<IImageValidationService, ImageValidationService>();
    builder.Services.AddScoped<IImageStorageService, LocalImageStorageService>();

    // Barcode generation (ZXing + SkiaSharp under the hood).
    builder.Services.AddSingleton<IBarcodeService, BarcodeService>();

    // Suppliers + Receiving (purchase orders).
    builder.Services.AddScoped<IReceivingOrderNumberGenerator, ReceivingOrderNumberGenerator>();
    builder.Services.AddScoped<IReceivingOrderService, ReceivingOrderService>();

    // ---------------------------------------------------------------
    // Payment gateway configuration (config-driven)
    // ---------------------------------------------------------------
    // Bind per-gateway options from appsettings
    builder.Services.Configure<PaymentOptions>(builder.Configuration.GetSection(PaymentOptions.Section));
    builder.Services.Configure<StripeOptions>(builder.Configuration.GetSection(StripeOptions.Section));
    builder.Services.Configure<EasyPaisaOptions>(builder.Configuration.GetSection(EasyPaisaOptions.Section));
    builder.Services.Configure<JazzCashOptions>(builder.Configuration.GetSection(JazzCashOptions.Section));

    // Stripe webhook handler
    builder.Services.AddScoped<StripeWebhookHandler>();
    builder.Services.AddScoped<IStripeWebhookHandler>(sp => sp.GetRequiredService<StripeWebhookHandler>());

    // Sub-phase 3d — EasyPaisa + JazzCash webhook handlers + reconciler.
    builder.Services.AddScoped<IEasyPaisaWebhookHandler, EasyPaisaWebhookHandler>();
    builder.Services.AddScoped<IJazzCashWebhookHandler, JazzCashWebhookHandler>();
    builder.Services.AddScoped<ISubscriptionPaymentReconciler, SubscriptionPaymentReconciler>();

    // Register each gateway implementation. EasyPaisa and JazzCash need HttpClient.
    builder.Services.AddScoped<StripePaymentGateway>();
    builder.Services.AddScoped<FakePaymentGateway>();
    builder.Services.AddScoped<CashPaymentGateway>();
    builder.Services.AddHttpClient<EasyPaisaPaymentGateway>();
    builder.Services.AddHttpClient<JazzCashPaymentGateway>();

    // Factory selects the active gateway based on Payments:Provider in appsettings.
    builder.Services.AddScoped<IPaymentGatewayFactory, PaymentGatewayFactory>();
    builder.Services.AddScoped<IPaymentGateway>(sp =>
        sp.GetRequiredService<IPaymentGatewayFactory>().GetActive());

    // Email service (configure smtp settings in appsettings.json)
    builder.Services.AddScoped<IEmailService, SmtpEmailService>();
    builder.Services.AddScoped<INotificationService, NotificationService>();

    // ---------------------------------------------------------------
    // Identity — email verification
    // ---------------------------------------------------------------
    builder.Services.Configure<VerificationOptions>(builder.Configuration.GetSection(VerificationOptions.Section));
    builder.Services.AddScoped<IVerificationTokenService, VerificationTokenService>();
    builder.Services.AddSingleton<IAuthorizationHandler, VerifiedEmailHandler>();

    // ---------------------------------------------------------------
    // Subscriptions
    // ---------------------------------------------------------------
    builder.Services.AddScoped<ISubscriptionService, SubscriptionService>();
    builder.Services.AddScoped<IEntitlementService, EntitlementService>();

    // Subscription billing (Sub-phase 3c)
    builder.Services.Configure<BillingOptions>(builder.Configuration.GetSection(BillingOptions.Section));
    builder.Services.AddScoped<IInvoiceNumberGenerator, InvoiceNumberGenerator>();
    builder.Services.AddScoped<ISubscriptionBillingService, SubscriptionBillingService>();
    builder.Services.AddHostedService<SubscriptionRenewalHostedService>();

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

        // Sub-phase 3a — gate tenant-side APIs behind verified email.
        // Apply via [Authorize(Policy = "RequireVerifiedEmail")] on controllers/actions
        // that should be locked until the user clicks the verification link.
        options.AddPolicy("RequireVerifiedEmail", policy =>
        {
            policy.RequireAuthenticatedUser();
            policy.Requirements.Add(new VerifiedEmailRequirement());
        });
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
    // Seed subscription plans (idempotent — only adds missing codes)
    // ---------------------------------------------------------------
    await using (var scope = app.Services.CreateAsyncScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<RetailDbContext>();
        var seederLogger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
        await SubscriptionPlanSeeder.SeedAsync(db, seederLogger);
    }

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

    // Block requests from Suspended / Cancelled tenants (Sub-phase 3b).
    // Runs after auth so we have tenantId in claims; allowlists auth/webhooks/subscriptions/swagger.
    app.UseMiddleware<SubscriptionEnforcementMiddleware>();

    app.MapControllers();

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application failed to start.");
    if (Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Testing")
        throw;
}
finally
{
    Log.CloseAndFlush();
}

public partial class Program { }
