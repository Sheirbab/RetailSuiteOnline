using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RetailSuite.Infrastructure.Modules.Customer.Dtos;
using RetailSuite.Infrastructure.Modules.Customer.Services;
using RetailSuite.Shared;

namespace RetailSuite.Api.Controllers;

[ApiController]
[Route("api/customers")]
public class CustomersController : ControllerBase
{
    private readonly CustomerService _customerService;
    private readonly ICurrentUserContext _currentUser;

    public CustomersController(CustomerService customerService, ICurrentUserContext currentUser)
    {
        _customerService = customerService;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Register a new customer account (public endpoint — used from the storefront or by staff).
    /// </summary>
    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterCustomerRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Email)     ||
            string.IsNullOrWhiteSpace(request.Password)  ||
            string.IsNullOrWhiteSpace(request.FirstName) ||
            string.IsNullOrWhiteSpace(request.LastName))
        {
            return BadRequest(ApiResponse<object>.Fail("All required fields must be provided."));
        }

        if (request.Password.Length < 8)
            return BadRequest(ApiResponse<object>.Fail("Password must be at least 8 characters."));

        var passwordHash = BCrypt.Net.BCrypt.HashPassword(request.Password);
        var tenantId     = _currentUser.TenantId;

        var customerId = await _customerService.RegisterAsync(request, passwordHash, tenantId);

        return Ok(new ApiResponse<Guid>(true, "Customer registered successfully.", customerId));
    }

    /// <summary>
    /// List all customers for the current tenant (staff/admin only).
    /// </summary>
    [Authorize(Policy = "StaffOrAdmin")]
    [HttpGet]
    public async Task<IActionResult> List(int page = 1, int pageSize = 20)
    {
        var customers = await _customerService.GetAllAsync();

        var total = customers.Count;
        var paged = customers
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(c => new
            {
                c.Id,
                c.FirstName,
                c.LastName,
                c.FullName,
                c.Email,
                c.Phone,
                c.CreatedAt
            });

        return Ok(new ApiResponse<object>(true, null, new
        {
            Total    = total,
            Page     = page,
            PageSize = pageSize,
            Data     = paged
        }));
    }

    /// <summary>
    /// Get a single customer by ID — includes extended profile fields.
    /// </summary>
    [Authorize(Policy = "StaffOrAdmin")]
    [HttpGet("{id}")]
    public async Task<IActionResult> Get(Guid id)
    {
        var customer = await _customerService.GetByIdAsync(id);

        if (customer == null)
            return NotFound(ApiResponse<object>.Fail("Customer not found."));

        return Ok(new ApiResponse<object>(true, null, new
        {
            customer.Id,
            customer.FirstName,
            customer.LastName,
            customer.FullName,
            customer.Email,
            customer.Phone,
            customer.Cnic,
            Group            = customer.Group.ToString(),
            customer.MarketingConsent,
            customer.DateOfBirth,
            customer.Notes,
            customer.CreatedAt
        }));
    }

    /// <summary>
    /// Patch a customer's profile (Staff/Admin). Allows updating name, contact, CNIC,
    /// segment group, marketing consent, DOB and notes.
    /// </summary>
    [Authorize(Policy = "StaffOrAdmin")]
    [HttpPatch("{id}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateCustomerProfileRequest request)
    {
        var customer = await _customerService.GetByIdAsync(id);
        if (customer == null)
            return NotFound(ApiResponse<object>.Fail("Customer not found."));

        if (!string.IsNullOrWhiteSpace(request.FirstName) || !string.IsNullOrWhiteSpace(request.LastName))
            customer.Rename(request.FirstName ?? customer.FirstName, request.LastName ?? customer.LastName);

        if (request.Email != null || request.Phone != null)
            customer.UpdateContact(request.Email ?? customer.Email, request.Phone ?? customer.Phone);

        if (request.Cnic != null) customer.SetCnic(request.Cnic);
        if (request.Notes != null) customer.SetNotes(request.Notes);

        if (request.MarketingConsent.HasValue)
            customer.SetMarketingConsent(request.MarketingConsent.Value);

        if (request.DateOfBirth.HasValue)
            customer.SetDateOfBirth(request.DateOfBirth);

        if (!string.IsNullOrWhiteSpace(request.Group)
            && Enum.TryParse<Infrastructure.Modules.Customer.Entities.CustomerGroup>(request.Group, ignoreCase: true, out var group))
        {
            customer.SetGroup(group);
        }

        await _customerService.SaveAsync();

        return Ok(ApiResponse<object>.Ok(new
        {
            customer.Id,
            customer.FullName,
            Group = customer.Group.ToString(),
            customer.MarketingConsent,
            customer.Cnic
        }));
    }
}

public class UpdateCustomerProfileRequest
{
    public string?  FirstName { get; set; }
    public string?  LastName  { get; set; }
    public string?  Email { get; set; }
    public string?  Phone { get; set; }
    public string?  Cnic { get; set; }
    public string?  Group { get; set; }
    public bool?    MarketingConsent { get; set; }
    public DateTime? DateOfBirth { get; set; }
    public string?  Notes { get; set; }
}
