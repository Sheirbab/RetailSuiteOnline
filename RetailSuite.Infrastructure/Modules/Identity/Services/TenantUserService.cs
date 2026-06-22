using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RetailSuite.Infrastructure.Exceptions;
using RetailSuite.Infrastructure.Modules.Customer.Model;
using RetailSuite.Infrastructure.Modules.Identity.Entities;
using RetailSuite.Shared;

namespace RetailSuite.Infrastructure.Modules.Identity.Services;

public interface ITenantUserService
{
    Task<List<User>> ListAsync(Guid tenantId);
    Task<User?> GetAsync(Guid tenantId, Guid userId);

    /// <summary>
    /// Create a user with a generated 12-character temp password. The plaintext password
    /// is returned in the result so the admin can hand it to the new user — it's not
    /// stored anywhere else. The user must change it on first login.
    /// </summary>
    Task<CreateUserResult> CreateAsync(
        Guid tenantId, string email, string? fullName, UserRole role);

    Task UpdateAsync(Guid tenantId, Guid userId, string? fullName, UserRole? role, bool? isActive);

    /// <summary>Admin-initiated password reset — generates a new temp password and flags must-change.</summary>
    Task<string> ResetPasswordAsync(Guid tenantId, Guid userId);

    /// <summary>Self-service password change — used by the first-login flow.</summary>
    Task ChangePasswordAsync(Guid tenantId, Guid userId, string currentPassword, string newPassword);

    // ---- Permissions ----
    Task<List<string>> GetPermissionsAsync(Guid tenantId, Guid userId);
    Task SetPermissionsAsync(Guid tenantId, Guid userId, IEnumerable<string> codes);
}

public record CreateUserResult(Guid UserId, string Email, string TempPassword);

public class TenantUserService : ITenantUserService
{
    private readonly RetailDbContext _db;
    private readonly ILogger<TenantUserService> _logger;

    public TenantUserService(RetailDbContext db, ILogger<TenantUserService> logger)
    {
        _db      = db;
        _logger  = logger;
    }

    public async Task<List<User>> ListAsync(Guid tenantId) =>
        await _db.Users
            .Where(u => u.TenantId == tenantId)
            .OrderBy(u => u.Email)
            .ToListAsync();

    public Task<User?> GetAsync(Guid tenantId, Guid userId) =>
        _db.Users.FirstOrDefaultAsync(u => u.Id == userId && u.TenantId == tenantId);

    public async Task<CreateUserResult> CreateAsync(
        Guid tenantId, string email, string? fullName, UserRole role)
    {
        if (string.IsNullOrWhiteSpace(email))
            throw new BusinessRuleException("Email is required.");
        if (role == UserRole.SuperAdmin)
            throw new BusinessRuleException("Cannot create a SuperAdmin user from tenant scope.");

        var emailLower = email.Trim().ToLowerInvariant();
        var exists = await _db.Users
            .IgnoreQueryFilters()
            .AnyAsync(u => u.Email == emailLower);
        if (exists)
            throw new BusinessRuleException("A user with that email already exists.");

        // Generate a memorable but secure 12-char temp password (alphanumeric, no
        // confusing 0/O/1/l characters). Shown ONCE in the response to the admin.
        var tempPassword = GenerateTempPassword();
        var hash = BCrypt.Net.BCrypt.HashPassword(tempPassword);

        var user = new User(tenantId, emailLower, hash, role);
        user.SetFullName(fullName);
        user.RequirePasswordChange();   // must change on first login
        user.MarkEmailVerified();        // skip the email-verify flow for staff users
        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "Tenant {TenantId}: created user {Email} as {Role} (must change password on login).",
            tenantId, emailLower, role);

        return new CreateUserResult(user.Id, user.Email, tempPassword);
    }

    public async Task UpdateAsync(Guid tenantId, Guid userId, string? fullName, UserRole? role, bool? isActive)
    {
        var user = await GetAsync(tenantId, userId)
            ?? throw new NotFoundException("User", userId);

        if (fullName != null) user.SetFullName(fullName);
        if (role.HasValue)
        {
            if (role.Value == UserRole.SuperAdmin)
                throw new BusinessRuleException("Cannot set role to SuperAdmin from tenant scope.");
            user.SetRole(role.Value);
        }
        if (isActive.HasValue)
        {
            if (isActive.Value) user.Activate();
            else                user.Deactivate();
        }

        await _db.SaveChangesAsync();
    }

    public async Task<string> ResetPasswordAsync(Guid tenantId, Guid userId)
    {
        var user = await GetAsync(tenantId, userId)
            ?? throw new NotFoundException("User", userId);

        var tempPassword = GenerateTempPassword();
        user.SetPassword(BCrypt.Net.BCrypt.HashPassword(tempPassword));
        user.RequirePasswordChange();
        await _db.SaveChangesAsync();
        return tempPassword;
    }

    public async Task ChangePasswordAsync(Guid tenantId, Guid userId, string currentPassword, string newPassword)
    {
        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 8)
            throw new BusinessRuleException("New password must be at least 8 characters.");

        var user = await GetAsync(tenantId, userId)
            ?? throw new NotFoundException("User", userId);

        if (!BCrypt.Net.BCrypt.Verify(currentPassword ?? "", user.PasswordHash))
            throw new BusinessRuleException("Current password is incorrect.");

        user.SetPassword(BCrypt.Net.BCrypt.HashPassword(newPassword));
        await _db.SaveChangesAsync();
    }

    // ----- Permissions ---------------------------------------------------

    public async Task<List<string>> GetPermissionsAsync(Guid tenantId, Guid userId) =>
        await _db.UserPermissions
            .Where(p => p.UserId == userId && p.TenantId == tenantId)
            .Select(p => p.Code)
            .ToListAsync();

    public async Task SetPermissionsAsync(Guid tenantId, Guid userId, IEnumerable<string> codes)
    {
        var user = await GetAsync(tenantId, userId)
            ?? throw new NotFoundException("User", userId);

        // Normalise + validate against the known catalog.
        var normalised = codes
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => c.Trim().ToUpperInvariant())
            .Distinct()
            .Where(Permissions.IsKnown)
            .ToHashSet();

        // Clear then re-add — simplest atomic replace.
        var existing = await _db.UserPermissions
            .Where(p => p.UserId == userId && p.TenantId == tenantId)
            .ToListAsync();
        _db.UserPermissions.RemoveRange(existing);

        foreach (var code in normalised)
        {
            _db.UserPermissions.Add(new UserPermission(tenantId, userId, code));
        }

        await _db.SaveChangesAsync();
    }

    // ----- Internals -----------------------------------------------------

    private static string GenerateTempPassword()
    {
        // 12 chars from an unambiguous alphabet (no 0/O/1/l).
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnpqrstuvwxyz23456789";
        Span<byte> bytes = stackalloc byte[12];
        RandomNumberGenerator.Fill(bytes);
        var pw = new char[12];
        for (var i = 0; i < 12; i++) pw[i] = chars[bytes[i] % chars.Length];
        return new string(pw);
    }
}
