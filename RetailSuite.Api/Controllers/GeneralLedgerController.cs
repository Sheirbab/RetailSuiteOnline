using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RetailSuite.Api.Authorization;
using RetailSuite.Infrastructure;
using RetailSuite.Infrastructure.Modules.Identity.Entities;
using RetailSuite.Modules.Accounting.Entities;
using RetailSuite.Modules.Accounting.Services;
using RetailSuite.Shared;

namespace RetailSuite.Api.Controllers;

/// <summary>
/// The General Ledger explorer. Read-only reports over the journal + accounts,
/// plus a manual-entry post endpoint for adjustments.
///
/// - GET /trial-balance?asOf=              — trial balance at a point in time
/// - GET /accounts/{id}/ledger?from=&to=   — one account's transactions with running balance
/// - GET /journal-entries?from=&to=&page=  — browse journal entries
/// - GET /journal-entries/{id}             — one journal entry with its lines
/// - POST /journal-entries                 — post a manual journal entry (admin only)
/// </summary>
[ApiController]
[Route("api/gl")]
[RequirePermission(Permissions.Accounting)]
public class GeneralLedgerController : ControllerBase
{
    private readonly RetailDbContext _db;
    private readonly AccountingService _accounting;

    public GeneralLedgerController(RetailDbContext db, AccountingService accounting)
    {
        _db         = db;
        _accounting = accounting;
    }

    // -----------------------------------------------------------------
    // GET /api/gl/trial-balance?asOf=2026-06-30
    // -----------------------------------------------------------------
    /// <summary>
    /// Trial balance at a point in time. Sums debit / credit for every account up to <paramref name="asOf"/>.
    /// Each account shows the natural-side balance based on its type
    /// (Asset/Expense = debit-normal, Liability/Equity/Revenue = credit-normal).
    /// </summary>
    [HttpGet("trial-balance")]
    public async Task<IActionResult> TrialBalance([FromQuery] DateTime? asOf)
    {
        var cutoff = (asOf ?? DateTime.UtcNow.Date).AddDays(1); // inclusive end of day

        var rawTotals = await _db.JournalEntryLines
            .Join(_db.JournalEntries,
                  l => l.JournalEntryId,
                  e => e.Id,
                  (l, e) => new { l, e })
            .Where(x => x.e.CreatedAt < cutoff)
            .GroupBy(x => x.l.AccountId)
            .Select(g => new
            {
                AccountId = g.Key,
                Debit     = g.Sum(x => x.l.DebitAmount),
                Credit    = g.Sum(x => x.l.CreditAmount)
            })
            .ToListAsync();

        var accounts = await _db.Accounts
            .OrderBy(a => a.Code)
            .ToListAsync();

        var totalsById = rawTotals.ToDictionary(t => t.AccountId);

        var rows = accounts.Select(a =>
        {
            var totals = totalsById.TryGetValue(a.Id, out var t) ? t : null;
            var debit  = totals?.Debit  ?? 0m;
            var credit = totals?.Credit ?? 0m;
            var net    = debit - credit;

            // Present balance on the natural side of the account.
            decimal debitBalance  = 0;
            decimal creditBalance = 0;
            switch (a.AccountType)
            {
                case AccountType.Asset:
                case AccountType.Expense:
                    if (net >= 0) debitBalance  = net;
                    else          creditBalance = -net; // "contra" balance
                    break;
                case AccountType.Liability:
                case AccountType.Equity:
                case AccountType.Revenue:
                    if (net <= 0) creditBalance = -net;
                    else          debitBalance  = net;
                    break;
            }

            return new
            {
                AccountId     = a.Id,
                a.Code,
                a.Name,
                AccountType   = a.AccountType.ToString(),
                DebitTotal    = debit,
                CreditTotal   = credit,
                DebitBalance  = debitBalance,
                CreditBalance = creditBalance
            };
        }).ToList();

        var totalDebit  = rows.Sum(r => r.DebitBalance);
        var totalCredit = rows.Sum(r => r.CreditBalance);

        return Ok(ApiResponse<object>.Ok(new
        {
            AsOf         = cutoff.AddDays(-1).ToString("yyyy-MM-dd"),
            Rows         = rows,
            TotalDebit   = totalDebit,
            TotalCredit  = totalCredit,
            InBalance    = totalDebit == totalCredit
        }));
    }

    // -----------------------------------------------------------------
    // GET /api/gl/accounts/{id}/ledger?from=2026-01-01&to=2026-06-30
    // -----------------------------------------------------------------
    /// <summary>
    /// Per-account transaction list with a running balance. Signed as
    /// debit-normal for Asset/Expense, credit-normal for the rest.
    /// </summary>
    [HttpGet("accounts/{id:guid}/ledger")]
    public async Task<IActionResult> AccountLedger(Guid id, [FromQuery] DateTime? from, [FromQuery] DateTime? to)
    {
        var acct = await _db.Accounts.FirstOrDefaultAsync(a => a.Id == id);
        if (acct == null) return NotFound(ApiResponse<object>.Fail("Account not found."));

        var fromDate = from ?? DateTime.UtcNow.Date.AddMonths(-3);
        var toDate   = (to ?? DateTime.UtcNow.Date).AddDays(1);

        // Opening balance = sum of activity strictly before fromDate.
        var openingRaw = await _db.JournalEntryLines
            .Join(_db.JournalEntries,
                  l => l.JournalEntryId, e => e.Id,
                  (l, e) => new { l, e })
            .Where(x => x.l.AccountId == id && x.e.CreatedAt < fromDate)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Debit  = g.Sum(x => x.l.DebitAmount),
                Credit = g.Sum(x => x.l.CreditAmount)
            })
            .FirstOrDefaultAsync();

        var openingNet    = (openingRaw?.Debit ?? 0m) - (openingRaw?.Credit ?? 0m);
        var isDebitNormal = acct.AccountType is AccountType.Asset or AccountType.Expense;
        // Represent balance on the natural side (positive = normal-side).
        var openingBalance = isDebitNormal ? openingNet : -openingNet;

        // Movements within window.
        var lines = await _db.JournalEntryLines
            .Join(_db.JournalEntries,
                  l => l.JournalEntryId, e => e.Id,
                  (l, e) => new { l, e })
            .Where(x => x.l.AccountId == id
                     && x.e.CreatedAt >= fromDate
                     && x.e.CreatedAt <  toDate)
            .OrderBy(x => x.e.CreatedAt)
            .Select(x => new
            {
                x.e.CreatedAt,
                JournalEntryId = x.e.Id,
                x.e.ReferenceId,
                x.e.Description,
                x.l.DebitAmount,
                x.l.CreditAmount
            })
            .ToListAsync();

        var rows = new List<object>();
        var running = openingBalance;
        foreach (var l in lines)
        {
            var delta = l.DebitAmount - l.CreditAmount;
            if (!isDebitNormal) delta = -delta;
            running += delta;

            rows.Add(new
            {
                Date           = l.CreatedAt,
                l.JournalEntryId,
                l.ReferenceId,
                l.Description,
                l.DebitAmount,
                l.CreditAmount,
                RunningBalance = running
            });
        }

        return Ok(ApiResponse<object>.Ok(new
        {
            Account = new
            {
                acct.Id, acct.Code, acct.Name,
                AccountType = acct.AccountType.ToString(),
                Normal      = isDebitNormal ? "Debit" : "Credit"
            },
            From           = fromDate.ToString("yyyy-MM-dd"),
            To             = toDate.AddDays(-1).ToString("yyyy-MM-dd"),
            OpeningBalance = openingBalance,
            ClosingBalance = running,
            Rows           = rows
        }));
    }

    // -----------------------------------------------------------------
    // GET /api/gl/journal-entries?from=&to=&page=1&pageSize=50
    // -----------------------------------------------------------------
    [HttpGet("journal-entries")]
    public async Task<IActionResult> ListJournalEntries(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] bool?     manualOnly,
        [FromQuery] int       page = 1,
        [FromQuery] int       pageSize = 50)
    {
        page     = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var fromDate = from ?? DateTime.UtcNow.Date.AddMonths(-1);
        var toDate   = (to ?? DateTime.UtcNow.Date).AddDays(1);

        var q = _db.JournalEntries
            .Where(e => e.CreatedAt >= fromDate && e.CreatedAt < toDate);
        if (manualOnly == true) q = q.Where(e => e.IsManual);

        var total = await q.CountAsync();

        var items = await q
            .OrderByDescending(e => e.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(e => new
            {
                e.Id,
                e.CreatedAt,
                e.ReferenceId,
                e.Description,
                e.IsManual,
                Debit  = _db.JournalEntryLines.Where(l => l.JournalEntryId == e.Id).Sum(l => l.DebitAmount),
                Credit = _db.JournalEntryLines.Where(l => l.JournalEntryId == e.Id).Sum(l => l.CreditAmount),
                LineCount = _db.JournalEntryLines.Count(l => l.JournalEntryId == e.Id)
            })
            .ToListAsync();

        return Ok(ApiResponse<object>.Ok(new
        {
            Total    = total,
            Page     = page,
            PageSize = pageSize,
            From     = fromDate.ToString("yyyy-MM-dd"),
            To       = toDate.AddDays(-1).ToString("yyyy-MM-dd"),
            Items    = items
        }));
    }

    // -----------------------------------------------------------------
    // GET /api/gl/journal-entries/{id}
    // -----------------------------------------------------------------
    [HttpGet("journal-entries/{id:guid}")]
    public async Task<IActionResult> GetJournalEntry(Guid id)
    {
        var e = await _db.JournalEntries.FirstOrDefaultAsync(x => x.Id == id);
        if (e == null) return NotFound(ApiResponse<object>.Fail("Journal entry not found."));

        var lines = await _db.JournalEntryLines
            .Where(l => l.JournalEntryId == id)
            .Join(_db.Accounts,
                  l => l.AccountId,
                  a => a.Id,
                  (l, a) => new
                  {
                      l.Id,
                      AccountId   = a.Id,
                      AccountCode = a.Code,
                      AccountName = a.Name,
                      l.DebitAmount,
                      l.CreditAmount
                  })
            .ToListAsync();

        return Ok(ApiResponse<object>.Ok(new
        {
            e.Id,
            e.CreatedAt,
            e.ReferenceId,
            e.Description,
            e.IsManual,
            Lines       = lines,
            TotalDebit  = lines.Sum(l => l.DebitAmount),
            TotalCredit = lines.Sum(l => l.CreditAmount)
        }));
    }

    // -----------------------------------------------------------------
    // POST /api/gl/journal-entries — post a manual adjustment
    // -----------------------------------------------------------------
    [HttpPost("journal-entries")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> PostManualJournal([FromBody] ManualJournalRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Description))
            return BadRequest(ApiResponse<object>.Fail("Description is required."));
        if (request.Lines == null || request.Lines.Count < 2)
            return BadRequest(ApiResponse<object>.Fail("A journal entry needs at least two lines."));

        // Validate line arithmetic + resolve account ids
        decimal totalDebit  = 0;
        decimal totalCredit = 0;
        var seenPairs = new HashSet<(Guid, decimal, decimal)>();
        foreach (var l in request.Lines)
        {
            if (l.DebitAmount < 0 || l.CreditAmount < 0)
                return BadRequest(ApiResponse<object>.Fail("Debit and credit amounts cannot be negative."));
            if (l.DebitAmount > 0 && l.CreditAmount > 0)
                return BadRequest(ApiResponse<object>.Fail("A single line cannot have both a debit and credit."));
            if (l.DebitAmount == 0 && l.CreditAmount == 0)
                return BadRequest(ApiResponse<object>.Fail("Every line must have a non-zero debit or credit."));

            totalDebit  += l.DebitAmount;
            totalCredit += l.CreditAmount;
        }

        if (totalDebit != totalCredit)
            return BadRequest(ApiResponse<object>.Fail(
                $"Journal is not balanced: DR {totalDebit:N2} vs CR {totalCredit:N2}."));

        // Verify every accountId exists.
        var accountIds = request.Lines.Select(l => l.AccountId).Distinct().ToList();
        var found = await _db.Accounts
            .Where(a => accountIds.Contains(a.Id) && a.IsActive)
            .Select(a => a.Id)
            .ToListAsync();
        var missing = accountIds.Except(found).ToList();
        if (missing.Any())
            return BadRequest(ApiResponse<object>.Fail(
                $"Unknown or inactive account(s): {string.Join(", ", missing)}"));

        var journalLines = request.Lines
            .Select(l => (l.AccountId, l.DebitAmount, l.CreditAmount))
            .ToList();

        // Reuse the existing AccountingService so a manual entry is indistinguishable
        // from an auto-posted one (except for the IsManual flag we stamp below).
        await _accounting.CreateJournalEntryAsync(
            referenceId: request.ReferenceId,
            description: request.Description,
            lines:       journalLines);

        // Flag as manual so the UI can badge it.
        var newest = await _db.JournalEntries
            .OrderByDescending(e => e.CreatedAt)
            .FirstAsync(e => e.Description == request.Description);
        newest.MarkManual();
        await _db.SaveChangesAsync();

        return Ok(ApiResponse<object>.Ok(new
        {
            Posted      = newest.Id,
            TotalDebit  = totalDebit,
            TotalCredit = totalCredit
        }));
    }
}

public class ManualJournalRequest
{
    public string?  ReferenceId { get; set; }
    public string   Description { get; set; } = string.Empty;
    public List<ManualJournalLine> Lines { get; set; } = new();
}

public class ManualJournalLine
{
    public Guid    AccountId    { get; set; }
    public decimal DebitAmount  { get; set; }
    public decimal CreditAmount { get; set; }
}
