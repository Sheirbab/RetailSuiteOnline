using RetailSuite.Shared;

namespace RetailSuite.Infrastructure.Modules.Subscriptions.Entities;

/// <summary>
/// A tenant's current subscription. There is exactly one active row per tenant.
/// History is captured by Status transitions; we don't keep one row per period
/// (invoices serve that purpose in Sub-phase 3c).
/// </summary>
public class TenantSubscription : TenantEntity
{
    /// <summary>FK to the selected <see cref="SubscriptionPlan"/>.</summary>
    public Guid PlanId { get; private set; }

    /// <summary>Plan code snapshot — denormalised for fast reads.</summary>
    public string PlanCode { get; private set; } = string.Empty;

    public BillingCycle BillingCycle { get; private set; }

    public SubscriptionStatus Status { get; private set; }

    /// <summary>Start of the current billing period.</summary>
    public DateTime StartDate { get; private set; }

    /// <summary>End of the current billing period.</summary>
    public DateTime EndDate { get; private set; }

    /// <summary>When the trial expires (null if not in / never had a trial).</summary>
    public DateTime? TrialEndsAt { get; private set; }

    /// <summary>When the next invoice should be generated.</summary>
    public DateTime NextBillingAt { get; private set; }

    /// <summary>If true, subscription does NOT auto-renew at next billing — used for soft-cancel.</summary>
    public bool CancelAtPeriodEnd { get; private set; }

    /// <summary>When the cancellation was requested (null if not cancelled).</summary>
    public DateTime? CancelledAt { get; private set; }

    /// <summary>Auto-renewal flag. Distinct from CancelAtPeriodEnd — this is the persistent setting.</summary>
    public bool AutoRenew { get; private set; } = true;

    /// <summary>Most recent price charged — useful for proration calculations.</summary>
    public decimal LastPrice { get; private set; }

    public string Currency { get; private set; } = "PKR";

    private TenantSubscription() { }

    public TenantSubscription(
        Guid tenantId,
        SubscriptionPlan plan,
        BillingCycle billingCycle,
        DateTime? trialEndsAt = null)
    {
        Id           = Guid.NewGuid();
        CreatedAt    = DateTime.UtcNow;
        TenantId     = tenantId;
        PlanId       = plan.Id;
        PlanCode     = plan.Code;
        BillingCycle = billingCycle;
        Currency     = plan.Currency;
        LastPrice    = plan.PriceFor(billingCycle);

        var now = DateTime.UtcNow;
        StartDate = now;

        if (trialEndsAt.HasValue && trialEndsAt.Value > now)
        {
            Status        = SubscriptionStatus.Trialing;
            TrialEndsAt   = trialEndsAt;
            EndDate       = trialEndsAt.Value;
            NextBillingAt = trialEndsAt.Value;
        }
        else
        {
            Status        = SubscriptionStatus.Active;
            EndDate       = NextPeriodEnd(now, billingCycle);
            NextBillingAt = EndDate;
        }
    }

    public bool IsActive => Status == SubscriptionStatus.Trialing
                         || Status == SubscriptionStatus.Active
                         || Status == SubscriptionStatus.PastDue
                         || Status == SubscriptionStatus.GracePeriod;

    public bool IsInTrial => Status == SubscriptionStatus.Trialing
                          && TrialEndsAt.HasValue
                          && TrialEndsAt.Value > DateTime.UtcNow;

    public int DaysRemainingInPeriod =>
        (int)Math.Max(0, Math.Ceiling((EndDate - DateTime.UtcNow).TotalDays));

    // ---- Lifecycle transitions ----------------------------------------

    /// <summary>Move from Trialing to Active. Sets the first paid period dates.</summary>
    public void Activate()
    {
        Status        = SubscriptionStatus.Active;
        var now       = DateTime.UtcNow;
        StartDate     = now;
        EndDate       = NextPeriodEnd(now, BillingCycle);
        NextBillingAt = EndDate;
        TrialEndsAt   = null;
    }

    /// <summary>
    /// Switch plans mid-period. Caller is responsible for any proration / invoice generation —
    /// this entity just records the new plan and resets dates as instructed.
    /// </summary>
    public void ChangePlan(SubscriptionPlan newPlan, BillingCycle newCycle, bool effectiveImmediately)
    {
        PlanId       = newPlan.Id;
        PlanCode     = newPlan.Code;
        BillingCycle = newCycle;
        Currency     = newPlan.Currency;
        LastPrice    = newPlan.PriceFor(newCycle);

        if (effectiveImmediately)
        {
            var now       = DateTime.UtcNow;
            StartDate     = now;
            EndDate       = NextPeriodEnd(now, newCycle);
            NextBillingAt = EndDate;
            Status        = SubscriptionStatus.Active;
        }
        // else: change takes effect at the existing NextBillingAt — no date update here.
    }

    /// <summary>Mark for cancellation at period end. Access continues until EndDate.</summary>
    public void ScheduleCancellation()
    {
        CancelAtPeriodEnd = true;
        AutoRenew         = false;
        CancelledAt       = DateTime.UtcNow;
    }

    /// <summary>Undo a pending cancellation if the tenant changes their mind before period end.</summary>
    public void Resume()
    {
        CancelAtPeriodEnd = false;
        AutoRenew         = true;
        CancelledAt       = null;
    }

    /// <summary>Hard-stop the subscription right now (e.g. admin override).</summary>
    public void ForceCancel()
    {
        Status      = SubscriptionStatus.Cancelled;
        CancelledAt = DateTime.UtcNow;
        EndDate     = DateTime.UtcNow;
    }

    public void MarkPastDue()     => Status = SubscriptionStatus.PastDue;
    public void MarkGracePeriod() => Status = SubscriptionStatus.GracePeriod;
    public void MarkExpired()     => Status = SubscriptionStatus.Expired;

    /// <summary>Advance the period after a successful renewal payment.</summary>
    public void RenewToNextPeriod()
    {
        StartDate     = EndDate;
        EndDate       = NextPeriodEnd(EndDate, BillingCycle);
        NextBillingAt = EndDate;
        Status        = SubscriptionStatus.Active;
    }

    public void UpdateLastPrice(decimal price) => LastPrice = price;

    // ---- Helpers ------------------------------------------------------

    private static DateTime NextPeriodEnd(DateTime from, BillingCycle cycle) =>
        cycle == BillingCycle.Yearly ? from.AddYears(1) : from.AddMonths(1);
}
