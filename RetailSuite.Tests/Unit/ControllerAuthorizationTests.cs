using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using RetailSuite.Api.Controllers;
using RetailSuite.Infrastructure;
using RetailSuite.Infrastructure.Modules.Customer.Entities;
using RetailSuite.Infrastructure.Modules.Orders.Dtos;
using RetailSuite.Modules.Accounting.Entities;
using RetailSuite.Modules.Orders.Entities;
using RetailSuite.Shared;

namespace RetailSuite.Tests.Unit;

public class ControllerAuthorizationTests
{
    private static RetailDbContext CreateInMemoryDb()
    {
        var tenantContext = new Mock<ITenantContext>();
        tenantContext.Setup(t => t.TenantId).Returns((Guid?)null);

        var options = new DbContextOptionsBuilder<RetailDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new RetailDbContext(options, tenantContext.Object);
    }

    private static Mock<ICurrentUserContext> CreateCustomerContext(Guid userId, Guid tenantId)
    {
        var currentUser = new Mock<ICurrentUserContext>();
        currentUser.Setup(x => x.UserId).Returns(userId);
        currentUser.Setup(x => x.TenantId).Returns(tenantId);
        currentUser.Setup(x => x.Role).Returns("Customer");
        currentUser.Setup(x => x.IsAuthenticated).Returns(true);
        return currentUser;
    }

    [Fact]
    public async Task OrdersGet_ReturnsForbid_WhenCustomerTriesToAccessAnotherCustomersOrder()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateInMemoryDb();

        var ownerUserId = Guid.NewGuid();
        var callerUserId = Guid.NewGuid();

        var ownerCustomer = new Customer(ownerUserId, "Owner", "User", "owner@test.com", null) { TenantId = tenantId };
        var callerCustomer = new Customer(callerUserId, "Caller", "User", "caller@test.com", null) { TenantId = tenantId };
        db.Customers.AddRange(ownerCustomer, callerCustomer);

        var order = new Order("ORD-SEC-001", ownerCustomer.Id) { TenantId = tenantId };
        db.Orders.Add(order);
        await db.SaveChangesAsync();

        var currentUser = CreateCustomerContext(callerUserId, tenantId);
        var mockLogger = new Mock<ILogger<OrdersController>>();
        var controller = new OrdersController(orderService: null!, db, currentUser.Object, mockLogger.Object);

        var result = await controller.Get(order.Id);

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task OrdersUpdate_ReturnsForbid_WhenCustomerTriesToUpdateAnotherCustomersOrder()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateInMemoryDb();

        var ownerUserId = Guid.NewGuid();
        var callerUserId = Guid.NewGuid();

        var ownerCustomer = new Customer(ownerUserId, "Owner", "User", "owner@test.com", null) { TenantId = tenantId };
        var callerCustomer = new Customer(callerUserId, "Caller", "User", "caller@test.com", null) { TenantId = tenantId };
        db.Customers.AddRange(ownerCustomer, callerCustomer);

        var order = new Order("ORD-SEC-002", ownerCustomer.Id) { TenantId = tenantId };
        db.Orders.Add(order);
        await db.SaveChangesAsync();

        var currentUser = CreateCustomerContext(callerUserId, tenantId);
        var mockLogger = new Mock<ILogger<OrdersController>>();
        var controller = new OrdersController(orderService: null!, db, currentUser.Object, mockLogger.Object);

        var result = await controller.Update(order.Id, new CreateOrderRequest());

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task PaymentsGetByOrder_ReturnsForbid_WhenCustomerTriesToAccessAnotherCustomersOrder()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateInMemoryDb();

        var ownerUserId = Guid.NewGuid();
        var callerUserId = Guid.NewGuid();

        var ownerCustomer = new Customer(ownerUserId, "Owner", "User", "owner@test.com", null) { TenantId = tenantId };
        var callerCustomer = new Customer(callerUserId, "Caller", "User", "caller@test.com", null) { TenantId = tenantId };
        db.Customers.AddRange(ownerCustomer, callerCustomer);

        var order = new Order("ORD-SEC-003", ownerCustomer.Id) { TenantId = tenantId };
        db.Orders.Add(order);
        db.Payments.Add(new Payment(order.Id, 20m, "Cash") { TenantId = tenantId });
        await db.SaveChangesAsync();

        var currentUser = CreateCustomerContext(callerUserId, tenantId);
        var controller = new PaymentsController(paymentService: null!, db, currentUser.Object);

        var result = await controller.GetByOrder(order.Id);

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task PaymentsGetOutstanding_ReturnsForbid_WhenCustomerTriesToAccessAnotherCustomersOrder()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateInMemoryDb();

        var ownerUserId = Guid.NewGuid();
        var callerUserId = Guid.NewGuid();

        var ownerCustomer = new Customer(ownerUserId, "Owner", "User", "owner@test.com", null) { TenantId = tenantId };
        var callerCustomer = new Customer(callerUserId, "Caller", "User", "caller@test.com", null) { TenantId = tenantId };
        db.Customers.AddRange(ownerCustomer, callerCustomer);

        var order = new Order("ORD-SEC-004", ownerCustomer.Id) { TenantId = tenantId };
        db.Orders.Add(order);
        await db.SaveChangesAsync();

        var currentUser = CreateCustomerContext(callerUserId, tenantId);
        var controller = new PaymentsController(paymentService: null!, db, currentUser.Object);

        var result = await controller.GetOutstanding(order.Id);

        Assert.IsType<ForbidResult>(result);
    }
}
