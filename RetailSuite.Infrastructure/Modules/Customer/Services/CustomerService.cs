using RetailSuite.Infrastructure.Modules.Customer.Dtos;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RetailSuite.Infrastructure.Modules.Customer.Services;

public class CustomerService
{
    private readonly UserManager<IdentityUser> _userManager;
    private readonly RetailDbContext _db;

    public CustomerService(
        UserManager<IdentityUser> userManager,
        RetailDbContext db)
    {
        _userManager = userManager;
        _db = db;
    }

    public async Task<Guid> RegisterAsync(RegisterCustomerRequest request)
    {
        using var transaction = await _db.Database.BeginTransactionAsync();

        var user = new IdentityUser
        {
            UserName = request.Email,
            Email = request.Email
        };

        var result = await _userManager.CreateAsync(user, request.Password);

        if (!result.Succeeded)
            throw new Exception(string.Join(",", result.Errors.Select(e => e.Description)));

        var customer = new Customer(
            user.Id,
            request.FirstName,
            request.LastName,
            request.Phone);

        _db.Customers.Add(customer);

        await _db.SaveChangesAsync();
        await transaction.CommitAsync();

        return customer.Id;
    }
}