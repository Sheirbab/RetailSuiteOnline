using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RetailSuite.Api.Authorization;
using RetailSuite.Infrastructure;
using RetailSuite.Infrastructure.Modules.Identity.Entities;
using RetailSuite.Infrastructure.Seeders;
using RetailSuite.Modules.Accounting.Entities;
using RetailSuite.Shared;

namespace RetailSuite.Api.Controllers;

/// <summary>
/// Chart of Accounts CRUD. The tenant seeder creates the baseline set; this
/// controller lets admins add extras (e.g. new expense categories) or rename
/// / deactivate existing ones.
/// </summary>
[ApiController]
[Route("api/accounts")]
[RequirePermission(Permissions.Accounting)]
public class AccountsController : ControllerBase
{
    private readonly RetailDbContext _db;
    private readonly ITenantContext _tenantContext;

    public AccountsController(RetailDbContext db, ITenantContext tenantContext)
    {
        _db            = db;
        _tenantContext = tenantContext;
    }

    // POST /api/accounts/seed-defaults
    // Idempotent: safe to call multiple times. Fills in the standard Chart of
    // Accounts for the current tenant if any of the required accounts are missing.
    [HttpPost("seed-defaults")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> SeedDefaults()
    {
        var tenantId = _tenantContext.TenantId
            ?? throw new UnauthorizedAccessException("Tenant context missing.");

        var beforeCount = await _db.Accounts.CountAsync();
        await TenantDefaultsSeeder.SeedAsync(_db, tenantId);
        var afterCount  = await _db.Accounts.CountAsync();

        return Ok(ApiResponse<object>.Ok(new
        {
            BeforeCount = beforeCount,
            AfterCount  = afterCount,
            Added       = afterCount - beforeCount
        }));
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] bool? active)
    {
        var q = _db.Accounts.AsQueryable();
        if (active.HasValue) q = q.Where(a => a.IsActive == active.Value);

        var rows = await q
            .OrderBy(a => a.Code)
            .Select(a => new
            {
                a.Id, a.Code, a.Name,
                AccountType = a.AccountType.ToString(),
                a.IsActive
            })
            .ToListAsync();

        return Ok(ApiResponse<object>.Ok(rows));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id)
    {
        var a = await _db.Accounts.FirstOrDefaultAsync(x => x.Id == id);
        if (a == null) return NotFound(ApiResponse<object>.Fail("Account not found."));
        return Ok(ApiResponse<object>.Ok(new
        {
            a.Id, a.Code, a.Name,
            AccountType = a.AccountType.ToString(),
            a.IsActive
        }));
    }

    [HttpPost]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Create([FromBody] AccountRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(ApiResponse<object>.Fail("Code and Name are required."));
        if (!Enum.TryParse<AccountType>(request.AccountType, ignoreCase: true, out var accountType))
            return BadRequest(ApiResponse<object>.Fail("AccountType must be Asset, Liability, Equity, Revenue or Expense."));

        if (await _db.Accounts.AnyAsync(a => a.Code == request.Code.Trim()))
            return Conflict(ApiResponse<object>.Fail($"An account with code '{request.Code}' already exists."));

        var acct = new Account(request.Code, request.Name, accountType);
        _db.Accounts.Add(acct);
        await _db.SaveChangesAsync();

        return Ok(ApiResponse<object>.Ok(new { acct.Id, acct.Code, acct.Name, AccountType = acct.AccountType.ToString() }));
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Update(Guid id, [FromBody] AccountRequest request)
    {
        var acct = await _db.Accounts.FirstOrDefaultAsync(a => a.Id == id);
        if (acct == null) return NotFound(ApiResponse<object>.Fail("Account not found."));

        if (!string.IsNullOrWhiteSpace(request.Name)) acct.Rename(request.Name);
        if (!string.IsNullOrWhiteSpace(request.Code)
            && !string.Equals(request.Code, acct.Code, StringComparison.OrdinalIgnoreCase))
        {
            if (await _db.Accounts.AnyAsync(a => a.Code == request.Code.Trim() && a.Id != acct.Id))
                return Conflict(ApiResponse<object>.Fail($"An account with code '{request.Code}' already exists."));
            acct.SetCode(request.Code);
        }
        if (!string.IsNullOrWhiteSpace(request.AccountType)
            && Enum.TryParse<AccountType>(request.AccountType, ignoreCase: true, out var t))
            acct.SetType(t);

        if (request.IsActive.HasValue)
        {
            if (request.IsActive.Value) acct.Activate();
            else                        acct.Deactivate();
        }

        await _db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(new { acct.Id, acct.Code, acct.Name, AccountType = acct.AccountType.ToString(), acct.IsActive }));
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var acct = await _db.Accounts.FirstOrDefaultAsync(a => a.Id == id);
        if (acct == null) return NotFound(ApiResponse<object>.Fail("Account not found."));

        // Refuse hard-delete if journal history references this account.
        var used = await _db.JournalEntryLines.AnyAsync(l => l.AccountId == id);
        if (used)
        {
            acct.Deactivate();
            await _db.SaveChangesAsync();
            return Ok(ApiResponse<object>.Ok(new { Deactivated = id, Reason = "has-journal-entries" }));
        }

        _db.Accounts.Remove(acct);
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(new { Deleted = id }));
    }
}

public class AccountRequest
{
    public string  Code        { get; set; } = string.Empty;
    public string  Name        { get; set; } = string.Empty;
    /// <summary>Asset / Liability / Equity / Revenue / Expense</summary>
    public string  AccountType { get; set; } = string.Empty;
    public bool?   IsActive    { get; set; }
}
