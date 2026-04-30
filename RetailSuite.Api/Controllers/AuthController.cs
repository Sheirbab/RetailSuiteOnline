using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using RetailSuite.Infrastructure;
using RetailSuite.Infrastructure.Modules.Customer.Model;
using RetailSuite.Infrastructure.Modules.Identity;
using RetailSuite.Infrastructure.Modules.Identity.Dtos;
using RetailSuite.Infrastructure.Modules.Identity.Entities;
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
public class AuthController : ControllerBase
{
    private readonly RetailDbContext _Db;
    private readonly IConfiguration _config;

    public AuthController(
        RetailDbContext Db,
        IConfiguration config)
    {
        _Db = Db;
        _config = config;
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

        if (await _Db.Tenants.AnyAsync(t => t.Subdomain == request.Subdomain))
            return BadRequest(new ApiResponse<string>(false, "Subdomain already taken.", null));

        using var transaction = await _Db.Database.BeginTransactionAsync();

        try
        {
            // 1. Create Tenant
            var tenant = new Tenant(request.TenantName, request.Subdomain);
            _Db.Tenants.Add(tenant);
            await _Db.SaveChangesAsync();

            // 2. Create Admin User
            var passwordHash = BCrypt.Net.BCrypt.HashPassword(request.Password);
            var user = new User(tenant.Id, request.Email, passwordHash, UserRole.Admin);
            _Db.Users.Add(user);
            await _Db.SaveChangesAsync();

            // 3. Seed Chart of Accounts
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

            await transaction.CommitAsync();

            var token = GenerateJwt(user);
            return Ok(new ApiResponse<string>(true, "Account created successfully.", token));
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
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

    private string GenerateJwt(User user)
    {
        var jwtSettings = _config.GetSection("Jwt");

        var claims = new[]
        {
            new Claim("userId",   user.Id.ToString()),
            new Claim("tenantId", user.TenantId.ToString()),
            new Claim(ClaimTypes.Role, user.Role.ToString())
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
