using RetailSuite.Modules.Orders.Dtos;

namespace RetailSuite.Infrastructure.Modules.Orders.Dtos
{
    public class CreatePosSaleRequest
    {
        /// <summary>Optional — null/empty Guid for walk-in.</summary>
        public Guid? CustomerId { get; set; }

        public List<CreatePosSaleLine> Items { get; set; } = new();

        /// <summary>Cash received from the customer. Should be >= AmountDueAfterRedemptions.</summary>
        public decimal PaidAmount { get; set; }

        /// <summary>Order-level discount in rupees. Cashier-applied.</summary>
        public decimal OrderDiscountAmount { get; set; }

        /// <summary>Store-credit rupees to redeem against this sale (if customer attached and has credit).</summary>
        public decimal StoreCreditRedeem { get; set; }

        /// <summary>Loyalty points to redeem against this sale.</summary>
        public int LoyaltyPointsRedeem { get; set; }

        /// <summary>Optional held-sale id being resumed — service deletes it after a successful checkout.</summary>
        public Guid? ResumedFromHeldSaleId { get; set; }

        /// <summary>
        /// Branch / location selling this sale. POS terminal sends this from localStorage.
        /// If omitted, SaleService falls back to the tenant's default location.
        /// </summary>
        public Guid? LocationId { get; set; }

        /// <summary>
        /// When the cashier collected more cash than the amount due (positive change):
        ///   false → cashier returned the change to the customer (default).
        ///   true  → cashier kept the change; it's posted to the customer's store-credit
        ///           ledger so the balance is owed-back-to-customer. Requires an attached
        ///           real customer; ignored on walk-in sales.
        /// </summary>
        public bool CreditChangeAsStoreCredit { get; set; }
    }

    /// <summary>Single line in a POS cart, with optional per-line discount.</summary>
    public class CreatePosSaleLine
    {
        public Guid ProductVariantId { get; set; }
        public int Quantity { get; set; }

        /// <summary>Per-line discount in rupees (applied before tax).</summary>
        public decimal LineDiscountAmount { get; set; }
    }
}
