using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using RetailSuite.Modules.Identity;
using RetailSuite.Modules.Identity.Dtos;
using RetailSuite.Modules.Identity.Entities;
using RetailSuite.Modules.Tenant;
using RetailSuite.Modules.Tenant.Entities;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace RetailSuite.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly TenantDbContext _tenantDb;
    private readonly IdentityDbContext _identityDb;
    private readonly IConfiguration _config;

    public AuthController(
        TenantDbContext tenantDb,
        IdentityDbContext identityDb,
        IConfiguration config)
    {
        _tenantDb = tenantDb;
        _identityDb = identityDb;
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
            return BadRequest("All fields are required.");
        }

        if (request.Password.Length < 8)
            return BadRequest("Password must be at least 8 characters.");

        // Check subdomain uniqueness
        if (await _tenantDb.Tenants
            .AnyAsync(t => t.Subdomain == request.Subdomain))
        {
            return BadRequest("Subdomain already taken.");
        }

        using var transaction = await _tenantDb.Database.BeginTransactionAsync();

        try
        {
            var tenant = new Tenant(request.TenantName, request.Subdomain);
            _tenantDb.Tenants.Add(tenant);
            await _tenantDb.SaveChangesAsync();

            var passwordHash = BCrypt.Net.BCrypt.HashPassword(request.Password);

            var user = new User(
                tenant.Id,
                request.Email,
                passwordHash,
                "Admin");

            _identityDb.Users.Add(user);
            await _identityDb.SaveChangesAsync();

            await transaction.CommitAsync();

            var token = GenerateJwt(user);

            return Ok(new { token });
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }
    [HttpPost("login")]
    public async Task<IActionResult> Login(string email, string password)
    {
        var user = await _identityDb.Users
            .FirstOrDefaultAsync(u => u.Email == email);

        if (user == null)
            return Unauthorized("Invalid credentials.");

        if (!BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
            return Unauthorized("Invalid credentials.");

        var token = GenerateJwt(user);

        return Ok(new { token });
    }

    private string GenerateJwt(User user)
    {
        var jwtSettings = _config.GetSection("Jwt");

        var claims = new[]
        {
            new Claim("userId", user.Id.ToString()),
            new Claim("tenantId", user.TenantId.ToString()),
            new Claim(ClaimTypes.Role, user.Role)
        };

        var jwtKey = jwtSettings["Key"] ?? "";
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));

        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: jwtSettings["Issuer"],
            audience: jwtSettings["Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddHours(2),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
