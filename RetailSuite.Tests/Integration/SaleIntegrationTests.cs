// Integration tests — see AuthIntegrationTests.cs for notes on how to enable.

namespace RetailSuite.Tests.Integration;

public class SaleIntegrationTests
{
    [Fact(Skip = "Integration test — requires running infrastructure. Remove Skip to enable.")]
    public async Task PosSale_EndToEnd_CreatesCompletedOrder()
    {
        // Full flow:
        // 1. POST /api/auth/signup → get JWT
        // 2. POST /api/products   → create product
        // 3. POST /api/products/{id}/variants → create variant
        // 4. POST /api/inventory/receive → add stock
        // 5. POST /api/sales/checkout → complete sale
        // 6. GET  /api/orders/{id}    → assert Status == Completed
        await Task.CompletedTask;
    }
}
