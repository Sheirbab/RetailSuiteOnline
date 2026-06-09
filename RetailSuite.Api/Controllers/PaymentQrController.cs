using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RetailSuite.Infrastructure;
using RetailSuite.Infrastructure.Modules.Barcodes.Services;
using RetailSuite.Infrastructure.Modules.Payments.Entities;
using RetailSuite.Infrastructure.Modules.Payments.Services;
using RetailSuite.Shared;

namespace RetailSuite.Api.Controllers;

/// <summary>
/// Renders the QR code for a payment intent + lets the customer poll the intent's
/// status. Anonymous because the intent id is the secret — knowing it is sufficient
/// to look up the QR. Intents are short-lived (~30 min) so this is safe enough.
/// </summary>
[ApiController]
[Route("api/payments")]
[AllowAnonymous]
public class PaymentQrController : ControllerBase
{
    private readonly RetailDbContext _db;
    private readonly IBarcodeService _barcodes;
    private readonly IOrderPaymentService _payments;

    public PaymentQrController(
        RetailDbContext db, IBarcodeService barcodes, IOrderPaymentService payments)
    {
        _db        = db;
        _barcodes  = barcodes;
        _payments  = payments;
    }

    // GET /api/payments/qr/{intentId}.png
    [HttpGet("qr/{intentId:guid}.png")]
    public async Task<IActionResult> Qr(Guid intentId)
    {
        var intent = await _db.OrderPaymentIntents
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(i => i.Id == intentId);
        if (intent == null || string.IsNullOrEmpty(intent.QrPayload))
            return NotFound();

        var png = _barcodes.GenerateQrPng(intent.QrPayload, sizePx: 320);
        return File(png, "image/png");
    }

    // GET /api/payments/intent/{intentId}/status
    [HttpGet("intent/{intentId:guid}/status")]
    public async Task<IActionResult> Status(Guid intentId)
    {
        var intent = await _db.OrderPaymentIntents
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(i => i.Id == intentId);
        if (intent == null)
            return NotFound(ApiResponse<object>.Fail("Intent not found."));

        // Lazy expiry: caller's read flips a stale-but-not-yet-marked intent.
        if (intent.Status == PaymentIntentStatus.Pending && intent.IsExpired)
        {
            intent.MarkExpired();
            await _db.SaveChangesAsync();
        }

        return Ok(ApiResponse<object>.Ok(new
        {
            intent.Id,
            intent.OrderId,
            intent.Provider,
            intent.AmountDue,
            Status    = intent.Status.ToString(),
            intent.ExpiresAt,
            intent.PaidAt,
            intent.FailedAt,
            intent.FailureReason
        }));
    }

    /// <summary>
    /// Dev-only convenience: simulate a successful gateway callback for a given intent.
    /// Used to test the end-to-end flow without real EP/JC sandbox credentials.
    /// Disable / remove in production.
    /// </summary>
    [HttpPost("intent/{intentId:guid}/simulate-paid")]
    public async Task<IActionResult> SimulatePaid(Guid intentId)
    {
        var intent = await _db.OrderPaymentIntents
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(i => i.Id == intentId);
        if (intent == null) return NotFound(ApiResponse<object>.Fail("Intent not found."));
        if (string.IsNullOrEmpty(intent.ProviderTxnId))
            return BadRequest(ApiResponse<object>.Fail("Intent has no provider txn id."));

        var updated = await _payments.MarkIntentPaidByGatewayTxnAsync(
            intent.Provider, intent.ProviderTxnId, rawPayload: "{\"simulated\":true}");

        return Ok(ApiResponse<object>.Ok(new { Status = updated?.Status.ToString() ?? "Unknown" }));
    }
}
