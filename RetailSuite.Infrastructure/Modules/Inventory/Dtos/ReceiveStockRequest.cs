using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RetailSuite.Infrastructure.Modules.Inventory.Dtos
{
    public class ReceiveStockRequest
    {
        public Guid ProductVariantId { get; set; }
        public int Quantity { get; set; }
        public decimal UnitCost { get; set; }
        public string? Reference { get; set; }
    }
}
