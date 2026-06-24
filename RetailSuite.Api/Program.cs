using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.Threading.RateLimiting;
using RetailSuite.Api.Middleware;
using RetailSuite.Api.Seeding;
using RetailSuite.Api.MultiTenancy;
using RetailSuite.Infrastructure;
using RetailSuite.Infrastructure.Email;
using RetailSuite.Infrastructure.Modules.Customer.Services;
using RetailSuite.Infrastructure.Modules.Customer.Entities;
using RetailSuite.Infrastructure.Modules.Identity;
using RetailSuite.Infrastructure.Modules.Barcodes.Services;
using RetailSuite.Infrastructure.Modules.Catalog.Services;
using RetailSuite.Infrastructure.Modules.Identity.Services;
using RetailSuite.Infrastructure.Modules.Images.Services;
using RetailSuite.Infrastructure.Modules.Inventory.Services;
using RetailSuite.Infrastructure.Modules.Orders.Services;
using RetailSuite.Infrastructure.Modules.Receiving.Services;
using RetailSuite.Infrastructure.Modules.SupplierReturns.Services;
using RetailSuite.Infrastructure.Modules.Tax.Services;
using RetailSuite.Infrastructure.Modules.Locations.Services;
using RetailSuite.Infrastructure.Modules.Payments.Services;
using RetailSuite.Infrastructure.Modules.Transfers.Services;
using RetailSuite.Infrastructure.Modules.Wallet.Services;
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
    builder.Services.AddScoped<IStoreCreditService, StoreCreditService>();
    builder.Services.AddScoped<ILoyaltyService, LoyaltyService>();

    builder.Services.Configure<ImageStorageOptions>(builder.Configuration.GetSection(ImageStorageOptions.Section));
    builder.Services.AddScoped<IImageValidationService, ImageValidationService>();
    builder.Services.AddScoped<IImageStorageService, LocalImageStorageService>();

    // Barcode generation (ZXing + SkiaSharp under the hood).
    builder.Services.AddSingleton<IBarcodeService, BarcodeService>();

    // HTML sanitizer for product descriptions — stateless, register as singleton.
    builder.Services.AddSingleton<IHtmlSanitizerService, HtmlSanitizerService>();

    // Suppliers + Receiving (purchase orders).
    builder.Services.AddScoped<IReceivingOrderNumberGenerator, ReceivingOrderNumberGenerator>();
    builder.Services.AddScoped<IReceivingOrderService, ReceivingOrderService>();

    // Supplier returns + credit notes.
    builder.Services.AddScoped<ISupplierReturnNumberGenerator, SupplierReturnNumberGenerator>();
    builder.Services.AddScoped<ISupplierReturnService, SupplierReturnService>();

    // FBR-compliant invoice stamping (per-tenant invoice numbers + seller snapshot).
    builder.Services.AddScoped<ISalesInvoiceNumberGenerator, SalesInvoiceNumberGenerator>();
    builder.Services.AddScoped<IInvoiceStampingService, InvoiceStampingService>();

    // Customer wallet OTP login. LogOnlyOtpDelivery for dev — swap for real SMS in prod.
    builder.Services.AddScoped<IOtpDeliveryService, LogOnlyOtpDelivery>();
    builder.Services.AddScoped<IOtpService, OtpService>();

    // Locations (branches / shops).
    builder.Services.AddScoped<ILocationService, LocationService>();

    // Inter-location stock transfers.
    builder.Services.AddScoped<ITransferNumberGenerator, TransferNumberGenerator>();
    builder.Services.AddScoped<IInventoryTransferService, InventoryTransferService>();

    // Storefront payment intents (QR-based EasyPaisa / JazzCash).
    builder.Services.AddScoped<IOrderPaymentService, OrderPaymentService>();

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

    // Per-permission server-side enforcement (paired with the Permissions catalog
    // and the UserPermission junction table). Use [RequirePermission("CODE")] on
    // controllers or actions; admins bypass automatically.
    builder.Services.AddSingleton<IAuthorizationPolicyProvider, RetailSuite.Api.Authorization.PermissionPolicyProvider>();
    builder.Services.AddScoped<IAuthorizationHandler, RetailSuite.Api.Authorization.RequirePermissionHandler>();

    // Tenant user / staff management.
    builder.Services.AddScoped<ITenantUserService, TenantUserService>();

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
        options.AddPolicy("WalletCustomer",    policy => policy.RequireRole("WalletCustomer"));

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
        c.SwaggerDoc("v1", new OpenApiInfo
        {
            Title       = "RetailSuite API",
            Version     = "v1",
            Description = "Multi-tenant retail platform — products, inventory, orders, " +
                          "subscriptions, receiving workflow, and webhook integrations.",
            Contact     = new OpenApiContact { Name = "RetailSuite Support" }
        });

        // Pull XML comments from the API project + any referenced library that has them.
        // Files are emitted next to the assembly when <GenerateDocumentationFile>true</GenerateDocumentationFile>.
        var binDir = AppContext.BaseDirectory;
        foreach (var xmlFile in Directory.GetFiles(binDir, "RetailSuite.*.xml"))
        {
            c.IncludeXmlComments(xmlFile, includeControllerXmlComments: true);
        }

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

    // ---------------------------------------------------------------
    // CORS — explicit allow-list, no wildcards in production.
    // ---------------------------------------------------------------
    var allowedOrigins = builder.Configuration
        .GetSection("Cors:AllowedOrigins")
        .Get<string[]>()
        ?? new[] { "https://app.retailsuite.local" };

    builder.Services.AddCors(options =>
    {
        options.AddPolicy("AppOrigins", policy =>
        {
            policy.WithOrigins(allowedOrigins)
                  .AllowAnyHeader()
                  .AllowAnyMethod()
                  .AllowCredentials();
        });
    });

    // ---------------------------------------------------------------
    // Rate limiting — auth + webhook endpoints especially.
    // 10 req/min per IP for /api/auth/* and /api/webhooks/* keeps brute-force
    // and webhook-spam noise down; the rest of the API is uncapped to avoid
    // disrupting legitimate POS / catalog usage.
    // ---------------------------------------------------------------
    builder.Services.AddRateLimiter(opts =>
    {
        opts.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

        opts.AddPolicy("auth-strict", httpContext =>
            RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "anon",
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit          = 10,
                    Window               = TimeSpan.FromMinutes(1),
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    QueueLimit           = 0,
                    AutoReplenishment    = true
                }));

        opts.AddPolicy("webhook-strict", httpContext =>
            RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "anon",
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit          = 60,
                    Window               = TimeSpan.FromMinutes(1),
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    QueueLimit           = 0,
                    AutoReplenishment    = true
                }));
    });

    var app = builder.Build();

    // ---------------------------------------------------------------
    // Production secrets validation — fail fast on placeholder values.
    // Prevents accidentally deploying with dev keys or empty secrets.
    // ---------------------------------------------------------------
    if (app.Environment.IsProduction())
    {
        var problems = new List<string>();

        var jwtKey = builder.Configuration["Jwt:Key"] ?? "";
        if (jwtKey.Length < 32)
            problems.Add("Jwt:Key is shorter than 32 chars — set a strong production key via user-secrets or env.");
        if (jwtKey.Contains("DEV", StringComparison.OrdinalIgnoreCase)
            || jwtKey.Contains("THIS_IS_A_SUPER_LONG_DEV", StringComparison.OrdinalIgnoreCase))
            problems.Add("Jwt:Key still contains the development placeholder. Replace before deploying.");

        var superPwd = builder.Configuration["SuperAdmin:Password"] ?? "";
        if (superPwd.Equals("Admin@12345", StringComparison.Ordinal))
            problems.Add("SuperAdmin:Password is the default dev password. Replace before deploying.");

        if (problems.Count > 0)
        {
            Log.Fatal("Refusing to start in Production with insecure configuration:\n  - {Problems}",
                string.Join("\n  - ", problems));
            throw new InvalidOperationException(
                "Production secrets validation failed. See log for details.");
        }
    }

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

    app.UseCors("AppOrigins");
    app.UseRateLimiter();

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
