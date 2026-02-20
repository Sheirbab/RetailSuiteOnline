using RetailSuite.Infrastructure.Modules.Customer.Dtos;
using RetailSuite.Infrastructure.Modules.Identity;
using RetailSuite.Infrastructure.Modules.Identity.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RetailSuite.Infrastructure.Modules.Customer.Services;

public class CustomerService
{
    private readonly RetailDbContext _db;
    private readonly IdentityDbContext _identityDbContext;

    public CustomerService(RetailDbContext db, IdentityDbContext identityDbContext)
    {
        _db = db;
        this._identityDbContext = identityDbContext;
    }

    public async Task<Guid> RegisterAsync(RegisterCustomerRequest request)
    {
        using var transaction = await _db.Database.BeginTransactionAsync();

        var user = new User(Guid.NewGuid(), request.Email, request.Password, Model.UserRole.Customer);

        _identityDbContext.Users.Add(user);
        var result = await _identityDbContext.SaveChangesAsync();

        var customer = new Entities.Customer(
            user.Id,
            request.FirstName,
            request.LastName,
            request.Email,
            request.Phone);

        _db.Customers.Add(customer);

        await _db.SaveChangesAsync();
        await transaction.CommitAsync();

        return customer.Id;
    }
}