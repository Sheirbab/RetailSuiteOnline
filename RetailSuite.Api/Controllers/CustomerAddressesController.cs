using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RetailSuite.Infrastructure;
using RetailSuite.Infrastructure.Modules.Customer.Entities;
using RetailSuite.Shared;

namespace RetailSuite.Api.Controllers;

/// <summary>
/// Address book — many addresses per customer. Used by storefront checkout for shipping
/// and by admin for delivery-tracked sales.
/// </summary>
[ApiController]
[Route("api/customers/{customerId:guid}/addresses")]
[Authorize]
public class CustomerAddressesController : ControllerBase
{
    private readonly RetailDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserContext _currentUser;

    public CustomerAddressesController(
        RetailDbContext db,
        ITenantContext tenantContext,
        ICurrentUserContext currentUser)
    {
        _db = db;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
    }

    [HttpGet]
    public async Task<IActionResult> List(Guid customerId)
    {
        EnsureAccessAllowed(customerId);

        var rows = await _db.CustomerAddresses
            .Where(a => a.CustomerId == customerId)
            .OrderByDescending(a => a.IsDefaultShipping)
            .ThenByDescending(a => a.CreatedAt)
            .Select(a => Project(a))
            .ToListAsync();

        return Ok(ApiResponse<object>.Ok(rows));
    }

    [HttpPost]
    public async Task<IActionResult> Create(Guid customerId, [FromBody] AddressRequest request)
    {
        EnsureAccessAllowed(customerId);

        if (string.IsNullOrWhiteSpace(request.Line1) ||
            string.IsNullOrWhiteSpace(request.City)  ||
            string.IsNullOrWhiteSpace(request.RecipientName))
        {
            return BadRequest(ApiResponse<object>.Fail("Recipient, line 1 and city are required."));
        }

        var tenantId = RequireTenantId();
        var address = new CustomerAddress(
            tenantId, customerId,
            request.Label ?? "Address",
            request.RecipientName!,
            request.Line1!,
            request.City!,
            request.Country ?? "PK");

        address.Update(
            request.Label ?? "Address",
            request.RecipientName!,
            request.Line1!, request.Line2, request.City!,
            request.Province, request.PostalCode, request.Country ?? "PK",
            request.Phone, request.DeliveryInstructions);

        // First address auto-becomes default for both purposes.
        var existingCount = await _db.CustomerAddresses.CountAsync(a => a.CustomerId == customerId);
        if (existingCount == 0)
        {
            address.MarkDefaultShipping();
            address.MarkDefaultBilling();
        }

        _db.CustomerAddresses.Add(address);
        await _db.SaveChangesAsync();

        return Ok(ApiResponse<object>.Ok(Project(address)));
    }

    [HttpPatch("{addressId:guid}")]
    public async Task<IActionResult> Update(Guid customerId, Guid addressId, [FromBody] AddressRequest request)
    {
        EnsureAccessAllowed(customerId);

        var address = await _db.CustomerAddresses
            .FirstOrDefaultAsync(a => a.Id == addressId && a.CustomerId == customerId);
        if (address == null)
            return NotFound(ApiResponse<object>.Fail("Address not found."));

        address.Update(
            request.Label ?? address.Label,
            request.RecipientName ?? address.RecipientName,
            request.Line1 ?? address.Line1, request.Line2,
            request.City ?? address.City,
            request.Province, request.PostalCode,
            request.Country ?? address.Country,
            request.Phone, request.DeliveryInstructions);

        await _db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(Project(address)));
    }

    [HttpDelete("{addressId:guid}")]
    public async Task<IActionResult> Delete(Guid customerId, Guid addressId)
    {
        EnsureAccessAllowed(customerId);

        var address = await _db.CustomerAddresses
            .FirstOrDefaultAsync(a => a.Id == addressId && a.CustomerId == customerId);
        if (address == null)
            return NotFound(ApiResponse<object>.Fail("Address not found."));

        _db.CustomerAddresses.Remove(address);
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(new { Deleted = addressId }));
    }

    [HttpPatch("{addressId:guid}/default-shipping")]
    public async Task<IActionResult> SetDefaultShipping(Guid customerId, Guid addressId)
    {
        EnsureAccessAllowed(customerId);

        var all = await _db.CustomerAddresses
            .Where(a => a.CustomerId == customerId)
            .ToListAsync();
        var target = all.FirstOrDefault(a => a.Id == addressId);
        if (target == null)
            return NotFound(ApiResponse<object>.Fail("Address not found."));

        foreach (var a in all) a.UnmarkDefaultShipping();
        target.MarkDefaultShipping();
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(Project(target)));
    }

    [HttpPatch("{addressId:guid}/default-billing")]
    public async Task<IActionResult> SetDefaultBilling(Guid customerId, Guid addressId)
    {
        EnsureAccessAllowed(customerId);

        var all = await _db.CustomerAddresses
            .Where(a => a.CustomerId == customerId)
            .ToListAsync();
        var target = all.FirstOrDefault(a => a.Id == addressId);
        if (target == null)
            return NotFound(ApiResponse<object>.Fail("Address not found."));

        foreach (var a in all) a.UnmarkDefaultBilling();
        target.MarkDefaultBilling();
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(Project(target)));
    }

    // ---------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------

    private void EnsureAccessAllowed(Guid customerId)
    {
        // Staff/Admin can read any customer; a Customer can only read their own.
        if (User.IsInRole("Admin") || User.IsInRole("Staff") || User.IsInRole("SuperAdmin")) return;

        // Customer role — only their own record.
        var customer = _db.Customers.AsNoTracking().FirstOrDefault(c => c.Id == customerId);
        if (customer == null || customer.UserId != _currentUser.UserId)
            throw new UnauthorizedAccessException("You can only access your own addresses.");
    }

    private Guid RequireTenantId() =>
        _tenantContext.TenantId
        ?? throw new UnauthorizedAccessException("Tenant context missing.");

    private static object Project(CustomerAddress a) => new
    {
        a.Id, a.CustomerId,
        a.Label, a.RecipientName,
        a.Line1, a.Line2, a.City, a.Province, a.PostalCode, a.Country,
        a.Phone, a.DeliveryInstructions,
        a.IsDefaultShipping, a.IsDefaultBilling,
        a.CreatedAt
    };
}

public class AddressRequest
{
    public string?  Label { get; set; }
    public string?  RecipientName { get; set; }
    public string?  Line1 { get; set; }
    public string?  Line2 { get; set; }
    public string?  City { get; set; }
    public string?  Province { get; set; }
    public string?  PostalCode { get; set; }
    public string?  Country { get; set; }
    public string?  Phone { get; set; }
    public string?  DeliveryInstructions { get; set; }
}
