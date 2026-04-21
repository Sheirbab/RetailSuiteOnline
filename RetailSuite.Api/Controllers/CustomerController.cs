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
    public async Task<IActionResult> List()
    {
        var customers = await _customerService.GetAllAsync();

        var result = customers.Select(c => new
        {
            c.Id,
            c.FirstName,
            c.LastName,
            c.FullName,
            c.Email,
            c.Phone,
            c.CreatedAt
        });

        return Ok(new ApiResponse<object>(true, null, result));
    }

    /// <summary>
    /// Get a single customer by ID.
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
            customer.CreatedAt
        }));
    }
}
