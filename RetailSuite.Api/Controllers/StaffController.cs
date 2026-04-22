using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RetailSuite.Infrastructure;
using RetailSuite.Infrastructure.Modules.Customer.Entities;
using RetailSuite.Infrastructure.Modules.Customer.Model;
using RetailSuite.Infrastructure.Modules.Identity.Entities;
using RetailSuite.Shared;

namespace RetailSuite.Api.Controllers;

[ApiController]
[Route("api/staff")]
[Authorize(Policy = "AdminOnly")]
public class StaffController : ControllerBase
{
    private readonly RetailDbContext _db;
    private readonly ICurrentUserContext _currentUser;

    public StaffController(RetailDbContext db, ICurrentUserContext currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Create a new staff user account. Returns a one-time temporary password.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateStaffRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.FirstName) ||
            string.IsNullOrWhiteSpace(request.LastName)  ||
            string.IsNullOrWhiteSpace(request.Email))
        {
            return BadRequest(ApiResponse<object>.Fail("First name, last name and email are required."));
        }

        var tenantId = _currentUser.TenantId;

        // Check for duplicate email within tenant
        var exists = await _db.Users
            .AnyAsync(u => u.Email == request.Email && u.TenantId == tenantId);

        if (exists)
            return Conflict(ApiResponse<object>.Fail("A user with this email already exists."));

        // Generate a random 10-char temp password
        var tempPassword = GenerateTempPassword();
        var passwordHash = BCrypt.Net.BCrypt.HashPassword(tempPassword);

        using var tx = await _db.Database.BeginTransactionAsync();

        var user = new User(tenantId, request.Email, passwordHash, UserRole.Staff);
        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        // Create a Customer profile for the staff member so they appear in the tenant
        var profile = new Customer(user.Id, request.FirstName, request.LastName, request.Email, request.Phone);
        profile.TenantId = tenantId;
        _db.Customers.Add(profile);
        await _db.SaveChangesAsync();

        await tx.CommitAsync();

        return Ok(new ApiResponse<object>(true, "Staff member created.", new
        {
            UserId       = user.Id,
            Email        = request.Email,
            FullName     = $"{request.FirstName} {request.LastName}",
            TempPassword = tempPassword
        }));
    }

    /// <summary>
    /// List all staff users for the current tenant.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> List()
    {
        var tenantId = _currentUser.TenantId;

        var staff = await _db.Users
            .Where(u => u.Role == UserRole.Staff && u.TenantId == tenantId)
            .Select(u => new
            {
                u.Id,
                u.Email,
                u.CreatedAt
            })
            .ToListAsync();

        return Ok(new ApiResponse<object>(true, null, staff));
    }

    // -------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------
    private static string GenerateTempPassword()
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghjkmnpqrstuvwxyz23456789!@#";
        var random = new Random();
        return new string(Enumerable.Range(0, 10).Select(_ => chars[random.Next(chars.Length)]).ToArray());
    }
}

public class CreateStaffRequest
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName  { get; set; } = string.Empty;
    public string Email     { get; set; } = string.Empty;
    public string? Phone    { get; set; }
}
