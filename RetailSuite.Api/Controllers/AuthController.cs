using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using RetailSuite.Infrastructure;
using RetailSuite.Infrastructure.Email;
using RetailSuite.Infrastructure.Seeders;
using RetailSuite.Infrastructure.Modules.Customer.Model;
using RetailSuite.Infrastructure.Modules.Identity;
using RetailSuite.Infrastructure.Modules.Identity.Dtos;
using RetailSuite.Infrastructure.Modules.Identity.Entities;
using RetailSuite.Infrastructure.Modules.Identity.Services;
using RetailSuite.Infrastructure.Modules.Subscriptions.Entities;
using RetailSuite.Infrastructure.Modules.Subscriptions.Services;
using RetailSuite.Infrastructure.Modules.Tenant;
using RetailSuite.Infrastructure.Modules.Tenant.Entities;
using RetailSuite.Modules.Accounting.Entities;
using RetailSuite.Shared;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace RetailSuite.Api.Controllers;

[ApiController]
[Route("api/auth")]
[Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("auth-strict")]
public class AuthController : ControllerBase
{
    private readonly RetailDbContext _Db;
    private readonly IConfiguration _config;
    private readonly IVerificationTokenService _tokenService;
    private readonly INotificationService _notifications;
    private readonly ISubscriptionService _subs;
    private readonly VerificationOptions _verifyOptions;

    public AuthController(
        RetailDbContext Db,
        IConfiguration config,
        IVerificationTokenService tokenService,
        INotificationService notifications,
        ISubscriptionService subs,
        IOptions<VerificationOptions> verifyOptions)
    {
        _Db             = Db;
        _config         = config;
        _tokenService   = tokenService;
        _notifications  = notifications;
        _subs           = subs;
        _verifyOptions  = verifyOptions.Value;
    }

    [HttpPost("signup")]
    public async Task<IActionResult> Signup(SignupRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.TenantName) ||
            string.IsNullOrWhiteSpace(request.Subdomain) ||
            string.IsNullOrWhiteSpace(request.Email) ||
            string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest(new ApiResponse<string>(false, "All fields are required.", null));
        }

        if (request.Password.Length < 8)
            return BadRequest(new ApiResponse<string>(false, "Password must be at least 8 characters.", null));

        var email     = request.Email.Trim().ToLowerInvariant();
        var subdomain = request.Subdomain.Trim().ToLowerInvariant();

        if (await _Db.Tenants.IgnoreQueryFilters().AnyAsync(t => t.Subdomain == subdomain))
            return BadRequest(new ApiResponse<string>(false, "Subdomain already taken.", null));

        using var transaction = await _Db.Database.BeginTransactionAsync();

        try
        {
            // 1. Create Tenant — starts as PendingVerification.
            var tenant = new Tenant(
                request.TenantName.Trim(),
                subdomain,
                billingEmail: string.IsNullOrWhiteSpace(request.BillingEmail) ? email : request.BillingEmail.Trim().ToLowerInvariant(),
                countryCode: string.IsNullOrWhiteSpace(request.CountryCode) ? "PK" : request.CountryCode);
            tenant.SetStatus(TenantStatus.PendingVerification);
            _Db.Tenants.Add(tenant);
            await _Db.SaveChangesAsync();

            // 2. Create Admin User — unverified.
            var passwordHash = BCrypt.Net.BCrypt.HashPassword(request.Password);
            var user = new User(tenant.Id, email, passwordHash, UserRole.Admin);
            _Db.Users.Add(user);
            await _Db.SaveChangesAsync();

            // 3. Seed Chart of Accounts (unchanged).
            var accounts = new List<Account>
            {
                new Account("1000", "Cash",                  AccountType.Asset)     { TenantId = tenant.Id },
                new Account("1100", "Inventory",             AccountType.Asset)     { TenantId = tenant.Id },
                new Account("1200", "Accounts Receivable",   AccountType.Asset)     { TenantId = tenant.Id },
                new Account("2000", "Tax Payable",           AccountType.Liability) { TenantId = tenant.Id },
                new Account("4000", "Revenue",               AccountType.Revenue)   { TenantId = tenant.Id },
                new Account("5000", "Cost of Goods Sold",    AccountType.Expense)   { TenantId = tenant.Id },
            };
            _Db.Accounts.AddRange(accounts);
            await _Db.SaveChangesAsync();

            // 3b. Seed per-tenant defaults (shipping methods so the storefront works out of the box).
            await TenantDefaultsSeeder.SeedAsync(_Db, tenant.Id);

            // 4. Issue verification token (must be done inside the txn so we can roll back on failure).
            var plaintextToken = await _tokenService.IssueAsync(tenant.Id, user.Id, TokenPurpose.VerifyEmail);

            // 5. Create initial subscription — defaults to FREE plan, Monthly cycle.
            //    The subscription starts in Trialing if the plan has trial days, else Active.
            //    Tenant.Status remains PendingVerification until email is verified.
            var planCode = string.IsNullOrWhiteSpace(request.PlanCode)
                ? "FREE"
                : request.PlanCode.Trim().ToUpperInvariant();

            var billingCycle = Enum.TryParse<BillingCycle>(request.BillingCycle, ignoreCase: true, out var cycle)
                ? cycle
                : BillingCycle.Monthly;

            await _subs.CreateInitialSubscriptionAsync(tenant.Id, planCode, billingCycle);

            // 5b. Record the customer's chosen payment method on the new subscription.
            //     "Card" = auto-pay (store last4/brand snapshot for charges next cycle).
            //     "JazzCash" / "EasyPaisa" / "BankTransfer" / "Cash" = manual pay
            //     (customer pays each invoice themselves; we just record the preferred channel).
            if (request.PaymentMethod != null)
            {
                var sub = await _subs.GetActiveAsync(tenant.Id);
                if (sub != null)
                {
                    var t = (request.PaymentMethod.Type ?? "").Trim();
                    if (t.Equals("Card", StringComparison.OrdinalIgnoreCase))
                    {
                        var (brand, last4) = ExtractCardDisplay(request.PaymentMethod);
                        if (last4 != null && request.PaymentMethod.ExpMonth > 0 && request.PaymentMethod.ExpYear > 0)
                        {
                            sub.SetCardPaymentMethod(
                                cardBrand:         brand ?? "Card",
                                cardLast4:         last4,
                                expMonth:          request.PaymentMethod.ExpMonth,
                                expYear:           request.PaymentMethod.ExpYear,
                                holderName:        request.PaymentMethod.HolderName,
                                gatewayCustomerId: request.PaymentMethod.GatewayToken);
                        }
                    }
                    else if (!string.IsNullOrWhiteSpace(t))
                    {
                        // Manual pay path — JazzCash / EasyPaisa / BankTransfer / Cash.
                        sub.SetManualPaymentMethod(t);
                    }

                    await _Db.SaveChangesAsync();
                }
            }

            await transaction.CommitAsync();

            // 6. Fire verification email — best-effort, outside the transaction.
            var verifyUrl = BuildVerificationUrl(plaintextToken);
            await _notifications.SendVerifyEmailAsync(
                toAddress: user.Email,
                recipientName: user.Email,
                tenantName: tenant.Name,
                verificationUrl: verifyUrl,
                expiryHours: _verifyOptions.TokenTtlHours,
                tenantId: tenant.Id,
                userId: user.Id);

            // 7. Return a JWT scoped as "unverified" — user can call /verify-email and /resend-verification
            //    but anything behind [Authorize(Policy="RequireVerifiedEmail")] will return 403.
            var token = GenerateJwt(user);
            return Ok(new ApiResponse<object>(true, "Account created. Please check your email to verify.", new
            {
                Token              = token,
                RequiresVerification = true,
                TenantId           = tenant.Id,
                Subdomain          = tenant.Subdomain
            }));
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    // -------------------------------------------------------------
    // POST /api/auth/verify-email  — public
    // -------------------------------------------------------------
    [HttpPost("verify-email")]
    public async Task<IActionResult> VerifyEmail([FromBody] VerifyEmailRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Token))
            return BadRequest(new ApiResponse<string>(false, "Token is required.", null));

        var token = await _tokenService.ValidateAsync(request.Token, TokenPurpose.VerifyEmail);
        if (token is null)
            return BadRequest(new ApiResponse<string>(false, "Invalid or expired token.", null));

        var user = await _Db.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == token.UserId);

        if (user is null)
            return NotFound(new ApiResponse<string>(false, "User not found.", null));

        var tenant = await _Db.Tenants
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == user.TenantId);

        using var tx = await _Db.Database.BeginTransactionAsync();
        try
        {
            user.MarkEmailVerified();
            await _tokenService.MarkUsedAsync(token);

            // Move tenant out of PendingVerification on first successful verify.
            // If the subscription is in Trialing, mirror that on the Tenant; otherwise Active.
            if (tenant != null && tenant.Status == TenantStatus.PendingVerification)
            {
                var activeSub = await _subs.GetActiveAsync(tenant.Id);
                var newStatus = activeSub?.Status == SubscriptionStatus.Trialing
                    ? TenantStatus.Trialing
                    : TenantStatus.Active;
                tenant.SetStatus(newStatus);
            }

            await _Db.SaveChangesAsync();
            await tx.CommitAsync();
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }

        // Welcome email — best-effort.
        if (tenant != null)
        {
            await _notifications.SendWelcomeTenantAsync(
                toAddress: user.Email,
                recipientName: user.Email,
                tenantName: tenant.Name,
                loginUrl: $"{_verifyOptions.PublicBaseUrl.TrimEnd('/')}/login",
                tenantId: tenant.Id);
        }

        // Re-issue JWT so the new IsEmailVerified claim is reflected immediately.
        var newToken = GenerateJwt(user);
        return Ok(new ApiResponse<object>(true, "Email verified.", new { Token = newToken }));
    }

    // -------------------------------------------------------------
    // POST /api/auth/resend-verification  — public
    // -------------------------------------------------------------
    [HttpPost("resend-verification")]
    public async Task<IActionResult> ResendVerification([FromBody] ResendVerificationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Email))
            return BadRequest(new ApiResponse<string>(false, "Email is required.", null));

        var email = request.Email.Trim().ToLowerInvariant();

        // Find the user (tenant-scoped if subdomain provided, else first match).
        User? user;
        if (!string.IsNullOrWhiteSpace(request.Subdomain))
        {
            var tenant = await _Db.Tenants
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(t => t.Subdomain == request.Subdomain);
            if (tenant is null)
                return Ok(new ApiResponse<string>(true, "If an account exists, a verification email has been sent.", null));

            user = await _Db.Users
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(u => u.Email == email && u.TenantId == tenant.Id);
        }
        else
        {
            user = await _Db.Users
                .IgnoreQueryFilters()
                .Where(u => u.Email == email)
                .OrderBy(u => u.CreatedAt)
                .FirstOrDefaultAsync();
        }

        // Always return generic 200 to avoid leaking which emails exist.
        if (user is null || user.IsEmailVerified)
            return Ok(new ApiResponse<string>(true, "If an account exists, a verification email has been sent.", null));

        if (await _tokenService.IsResendThrottledAsync(user.Id, TokenPurpose.VerifyEmail))
            return StatusCode(429, new ApiResponse<string>(false, "Please wait before requesting another email.", null));

        var tenantForUser = await _Db.Tenants
            .IgnoreQueryFilters()
            .FirstAsync(t => t.Id == user.TenantId);

        var plaintextToken = await _tokenService.IssueAsync(user.TenantId, user.Id, TokenPurpose.VerifyEmail);
        var verifyUrl = BuildVerificationUrl(plaintextToken);

        await _notifications.SendVerifyEmailAsync(
            toAddress: user.Email,
            recipientName: user.Email,
            tenantName: tenantForUser.Name,
            verificationUrl: verifyUrl,
            expiryHours: _verifyOptions.TokenTtlHours,
            tenantId: tenantForUser.Id,
            userId: user.Id);

        return Ok(new ApiResponse<string>(true, "If an account exists, a verification email has been sent.", null));
    }

    private string BuildVerificationUrl(string plaintextToken)
    {
        var baseUrl = _verifyOptions.PublicBaseUrl.TrimEnd('/');
        return $"{baseUrl}/verify-email?token={Uri.EscapeDataString(plaintextToken)}";
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
            return BadRequest(new ApiResponse<string>(false, "Email and password are required.", null));

        User? user;
        if (!string.IsNullOrWhiteSpace(request.Subdomain))
        {
            var tenant = await _Db.Tenants
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(t => t.Subdomain == request.Subdomain);

            if (tenant == null)
                return Unauthorized(new ApiResponse<string>(false, "Invalid email or password.", null));

            user = await _Db.Users
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(u => u.Email == request.Email && u.TenantId == tenant.Id);
        }
        else
        {
            var users = await _Db.Users
                .IgnoreQueryFilters()
                .Where(u => u.Email == request.Email)
                .ToListAsync();

            if (users.Count > 1)
            {
                return BadRequest(new ApiResponse<string>(
                    false,
                    "Multiple accounts found for this email. Please provide subdomain to login.",
                    null));
            }

            user = users.SingleOrDefault();
        }

        if (user == null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            return Unauthorized(new ApiResponse<string>(false, "Invalid email or password.", null));

        var token = GenerateJwt(user);
        return Ok(new ApiResponse<string>(true, "Login successful.", token));
    }

    /// <summary>
    /// Extract the display-safe card details from a SignupPaymentMethod.
    /// In dev we derive from the raw PAN. In production replace this with
    /// "ask the gateway for its tokenised card metadata" — never inspect raw PANs server-side.
    /// </summary>
    private static (string? Brand, string? Last4) ExtractCardDisplay(
        RetailSuite.Infrastructure.Modules.Identity.Dtos.SignupPaymentMethod pm)
    {
        var pan = (pm.CardNumber ?? "").Where(char.IsDigit).Aggregate("", (a, c) => a + c);
        if (pan.Length < 4) return (null, null);

        var last4 = pan[^4..];
        var brand = pan switch
        {
            { Length: >= 1 } when pan.StartsWith("4")      => "Visa",
            { Length: >= 2 } when pan.StartsWith("5")
                              || pan.StartsWith("2")        => "Mastercard",
            { Length: >= 2 } when pan.StartsWith("34")
                              || pan.StartsWith("37")       => "Amex",
            _ => "Card"
        };
        return (brand, last4);
    }

    private string GenerateJwt(User user)
    {
        var jwtSettings = _config.GetSection("Jwt");

        var claims = new[]
        {
            new Claim("userId",   user.Id.ToString()),
            new Claim("tenantId", user.TenantId.ToString()),
            new Claim(ClaimTypes.Role, user.Role.ToString()),
            new Claim("email_verified", user.IsEmailVerified ? "true" : "false")
        };

        var key  = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings["Key"] ?? ""));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer:            jwtSettings["Issuer"],
            audience:          jwtSettings["Audience"],
            claims:            claims,
            expires:           DateTime.UtcNow.AddHours(8),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
