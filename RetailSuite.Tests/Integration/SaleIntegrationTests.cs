using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using RetailSuite.Modules.Orders.Entities;

namespace RetailSuite.Tests.Integration;

public class SaleIntegrationTests
{
    [Fact]
    public async Task PosSale_EndToEnd_CreatesCompletedOrder()
    {
        await using var factory = new ApiTestFactory();
        var client = factory.CreateClient();

        var token = await SignupAndGetTokenAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var productId = await CreateProductAsync(client);
        var variantId = await AddVariantAsync(client, productId);

        var receive = await client.PostAsJsonAsync("/api/inventory/receive", new
        {
            productVariantId = variantId,
            quantity = 5,
            unitCost = 40m,
            reference = "IT-PO-001"
        });
        receive.EnsureSuccessStatusCode();

        var checkout = await client.PostAsJsonAsync("/api/sales/checkout", new
        {
            paidAmount = 100m,
            items = new[]
            {
                new { productVariantId = variantId, quantity = 2 }
            }
        });
        checkout.EnsureSuccessStatusCode();

        var orderId = GetGuidData(await checkout.Content.ReadAsStringAsync());
        var order = await client.GetAsync($"/api/orders/{orderId}");
        order.EnsureSuccessStatusCode();

        using var orderDoc = JsonDocument.Parse(await order.Content.ReadAsStringAsync());
        Assert.Equal((int)OrderStatus.Completed, orderDoc.RootElement.GetProperty("status").GetInt32());
        Assert.Equal(100m, orderDoc.RootElement.GetProperty("totalAmount").GetDecimal());

        var variants = await client.GetAsync("/api/products/variants");
        variants.EnsureSuccessStatusCode();

        using var variantsDoc = JsonDocument.Parse(await variants.Content.ReadAsStringAsync());
        var soldVariant = variantsDoc.RootElement
            .GetProperty("data")
            .EnumerateArray()
            .Single(v => v.GetProperty("id").GetGuid() == variantId);

        Assert.Equal(3, soldVariant.GetProperty("stockQuantity").GetInt32());
    }

    private static async Task<string> SignupAndGetTokenAsync(HttpClient client)
    {
        var subdomain = $"sale-{Guid.NewGuid():N}";
        var response = await client.PostAsJsonAsync("/api/auth/signup", new
        {
            tenantName = "Sale Test Store",
            subdomain,
            email = $"admin-{subdomain}@example.com",
            password = "Test@12345"
        });
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var token = doc.RootElement.GetProperty("data").GetProperty("token").GetString();
        return token ?? throw new InvalidOperationException("Signup did not return a token.");
    }

    private static async Task<Guid> CreateProductAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/products", new
        {
            name = "Integration Test Product",
            description = "Created by integration test"
        });
        response.EnsureSuccessStatusCode();

        return GetGuidData(await response.Content.ReadAsStringAsync());
    }

    private static async Task<Guid> AddVariantAsync(HttpClient client, Guid productId)
    {
        var response = await client.PostAsJsonAsync($"/api/products/{productId}/variants", new
        {
            sku = $"IT-SKU-{Guid.NewGuid():N}",
            price = 50m,
            costPrice = 40m,
            taxRate = 0m
        });
        response.EnsureSuccessStatusCode();

        return GetGuidData(await response.Content.ReadAsStringAsync());
    }

    private static Guid GetGuidData(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("data").GetGuid();
    }
}
