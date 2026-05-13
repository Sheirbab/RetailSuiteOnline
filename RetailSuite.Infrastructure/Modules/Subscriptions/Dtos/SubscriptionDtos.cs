using RetailSuite.Infrastructure.Modules.Subscriptions.Entities;

namespace RetailSuite.Infrastructure.Modules.Subscriptions.Dtos;

// ----- Read DTOs --------------------------------------------------------

public record PlanResponse(
    Guid    Id,
    string  Code,
    string  Name,
    string  Description,
    decimal MonthlyPrice,
    decimal YearlyPrice,
    string  Currency,
    int     TrialDays,
    bool    IsActive,
    int     SortOrder,
    int?    MaxUsers,
    int?    MaxProducts,
    int?    MaxOrdersPerMonth,
    int?    MaxStorageMb,
    bool    ApiAccess,
    bool    MultiStore,
    bool    AdvancedAnalytics,
    bool    WebhooksEnabled,
    bool    PrioritySupport);

public record CurrentSubscriptionResponse(
    Guid    SubscriptionId,
    string  PlanCode,
    string  PlanName,
    string  BillingCycle,
    string  Status,
    DateTime StartDate,
    DateTime EndDate,
    DateTime? TrialEndsAt,
    DateTime NextBillingAt,
    bool    CancelAtPeriodEnd,
    decimal LastPrice,
    string  Currency);

// ----- Write DTOs -------------------------------------------------------

public class ChangePlanRequest
{
    public string PlanCode { get; set; } = string.Empty;
    public string BillingCycle { get; set; } = "Monthly";   // "Monthly" | "Yearly"
}

public class CreatePlanRequest
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal MonthlyPrice { get; set; }
    public decimal YearlyPrice { get; set; }
    public string Currency { get; set; } = "PKR";
    public int TrialDays { get; set; } = 14;
    public int SortOrder { get; set; } = 100;

    public int? MaxUsers { get; set; }
    public int? MaxProducts { get; set; }
    public int? MaxOrdersPerMonth { get; set; }
    public int? MaxStorageMb { get; set; }

    public bool ApiAccess { get; set; }
    public bool MultiStore { get; set; }
    public bool AdvancedAnalytics { get; set; }
    public bool WebhooksEnabled { get; set; }
    public bool PrioritySupport { get; set; }
}

public class UpdatePlanRequest
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public decimal? MonthlyPrice { get; set; }
    public decimal? YearlyPrice { get; set; }
    public string?  Currency { get; set; }
    public int?     TrialDays { get; set; }
    public int?     SortOrder { get; set; }
    public bool?    IsActive { get; set; }

    public int? MaxUsers { get; set; }
    public int? MaxProducts { get; set; }
    public int? MaxOrdersPerMonth { get; set; }
    public int? MaxStorageMb { get; set; }

    public bool? ApiAccess { get; set; }
    public bool? MultiStore { get; set; }
    public bool? AdvancedAnalytics { get; set; }
    public bool? WebhooksEnabled { get; set; }
    public bool? PrioritySupport { get; set; }
}

// ----- Mappers ----------------------------------------------------------

public static class SubscriptionMappers
{
    public static PlanResponse ToResponse(this SubscriptionPlan p) => new(
        p.Id, p.Code, p.Name, p.Description,
        p.MonthlyPrice, p.YearlyPrice, p.Currency, p.TrialDays, p.IsActive, p.SortOrder,
        p.MaxUsers, p.MaxProducts, p.MaxOrdersPerMonth, p.MaxStorageMb,
        p.ApiAccess, p.MultiStore, p.AdvancedAnalytics, p.WebhooksEnabled, p.PrioritySupport);

    public static CurrentSubscriptionResponse ToResponse(this TenantSubscription s, string planName) => new(
        s.Id, s.PlanCode, planName,
        s.BillingCycle.ToString(), s.Status.ToString(),
        s.StartDate, s.EndDate, s.TrialEndsAt, s.NextBillingAt,
        s.CancelAtPeriodEnd, s.LastPrice, s.Currency);
}
