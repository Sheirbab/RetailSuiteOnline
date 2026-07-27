using Microsoft.EntityFrameworkCore;
using RetailSuite.Infrastructure.Modules.Customer.Dtos;
using RetailSuite.Infrastructure.Modules.Identity.Entities;
using RetailSuite.Shared;

namespace RetailSuite.Infrastructure.Modules.Customer.Services;

public class CustomerService
{
    private readonly RetailDbContext _db;

    public CustomerService(RetailDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Registers a new customer (creates both a User and a Customer record).
    /// Expects a pre-hashed password so that hashing can be done by the caller (API layer).
    /// </summary>
    public async Task<Guid> RegisterAsync(RegisterCustomerRequest request, string passwordHash, Guid tenantId)
    {
        Guid newCustomerId = Guid.Empty;

        var strategy = _db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            using var transaction = await _db.Database.BeginTransactionAsync();

            var user = new User(tenantId, request.Email, passwordHash, Model.UserRole.Customer);
            _db.Users.Add(user);
            await _db.SaveChangesAsync();

            var customer = new Entities.Customer(
                user.Id,
                request.FirstName,
                request.LastName,
                request.Email,
                request.Phone);

            // TenantId is set automatically by SaveChangesAsync override when TenantContext is available.
            // Explicit assignment as safety net.
            customer.TenantId = tenantId;

            _db.Customers.Add(customer);
            await _db.SaveChangesAsync();

            await transaction.CommitAsync();

            newCustomerId = customer.Id;
        });

        return newCustomerId;
    }

    /// <summary>Returns all customers for the current tenant.</summary>
    public async Task<List<Entities.Customer>> GetAllAsync()
    {
        return await _db.Customers
            .OrderBy(c => c.LastName)
            .ThenBy(c => c.FirstName)
            .ToListAsync();
    }

    /// <summary>Returns a single customer by ID.</summary>
    public async Task<Entities.Customer?> GetByIdAsync(Guid id)
    {
        return await _db.Customers
            .FirstOrDefaultAsync(c => c.Id == id);
    }

    /// <summary>Persist tracked changes. Used by endpoints that mutate the entity in place.</summary>
    public Task SaveAsync() => _db.SaveChangesAsync();
}
