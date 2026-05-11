using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RetailSuite.Infrastructure;
using RetailSuite.Infrastructure.Email;
using RetailSuite.Modules.Accounting.Entities;
using RetailSuite.Modules.Accounting.Services;
using RetailSuite.Modules.Orders.Entities;

public class PaymentService
{
    private readonly RetailDbContext _db;
    private readonly AccountingService _accountingService;
    private readonly INotificationService _notifications;
    private readonly ILogger<PaymentService> _logger;

    public PaymentService(
        RetailDbContext db,
        AccountingService accountingService,
        INotificationService notifications,
        ILogger<PaymentService> logger)
    {
        _db = db;
        _accountingService = accountingService;
        _notifications = notifications;
        _logger = logger;
    }

    public async Task ReceivePaymentAsync(
     Guid orderId,
     decimal amount,
     string method)
    {
        _logger.LogInformation("Processing payment for Order {OrderId}: {Amount:C} via {PaymentMethod}", orderId, amount, method);

        using var transaction = await _db.Database.BeginTransactionAsync();

        var order = await _db.Orders
            .FirstOrDefaultAsync(o => o.Id == orderId);

        if (order == null)
        {
            _logger.LogWarning("Payment processing failed: Order {OrderId} not found", orderId);
            throw new Exception("Order not found.");
        }

        if (order.Status == OrderStatus.Cancelled)
        {
            _logger.LogWarning("Payment processing failed: Order {OrderId} is cancelled", orderId);
            throw new Exception("Cannot pay a cancelled order.");
        }

        order.RegisterPayment(amount);

        var payment = new Payment(orderId, amount, method);
        _db.Payments.Add(payment);

        var cashAccount = await _db.Accounts.FirstAsync(a => a.Code == "1000");
        var arAccount = await _db.Accounts.FirstAsync(a => a.Code == "1200");

        await _accountingService.CreateJournalEntryAsync(
            orderId.ToString(),
            $"Payment for Order {order.OrderNumber}",
            new List<(Guid, decimal, decimal)>
            {
            (cashAccount.Id, amount, 0),
            (arAccount.Id, 0, amount)
            });

        await _db.SaveChangesAsync();
        await transaction.CommitAsync();

        _logger.LogInformation("Payment {PaymentId} successfully recorded for Order {OrderId}", payment.Id, orderId);

        // Best-effort notification — never throws.
        await _notifications.SendPaymentReceivedAsync(payment.Id);
    }
    public async Task DeletePaymentAsync(Guid paymentId)
    {
        using var transaction = await _db.Database.BeginTransactionAsync();

        var payment = await _db.Payments
            .FirstOrDefaultAsync(p => p.Id == paymentId);

        if (payment == null)
            throw new Exception("Payment not found.");

        var order = await _db.Orders
            .FirstAsync(o => o.Id == payment.OrderId);

        order.RegisterPayment(-payment.Amount); // reduce paid amount

        var cashAccount = await _db.Accounts.FirstAsync(a => a.Code == "1000");
        var arAccount = await _db.Accounts.FirstAsync(a => a.Code == "1200");

        await _accountingService.CreateJournalEntryAsync(
            payment.OrderId.ToString(),
            $"Payment reversal",
            new List<(Guid, decimal, decimal)>
            {
            (arAccount.Id, payment.Amount, 0),
            (cashAccount.Id, 0, payment.Amount)
            });

        _db.Payments.Remove(payment);

        await _db.SaveChangesAsync();
        await transaction.CommitAsync();
    }
}