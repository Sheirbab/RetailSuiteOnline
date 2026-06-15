using RetailSuite.Infrastructure.Modules.Inventory.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RetailSuite.Infrastructure.Modules.Inventory.Dtos
{
    public class AdjustStockRequest
    {
        public Guid ProductVariantId { get; set; }
        public int Quantity { get; set; }

        public InventoryTransactionType Type { get; set; }

        public string? Reference { get; set; }
        public string? Reason { get; set; }

        /// <summary>Optional — defaults to the tenant's default location.</summary>
        public Guid? LocationId { get; set; }
    }
}
