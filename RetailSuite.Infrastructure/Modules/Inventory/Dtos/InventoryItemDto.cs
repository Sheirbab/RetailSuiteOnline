using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RetailSuite.Infrastructure.Modules.Inventory.Dtos
{
    public class InventoryItemDto
    {
        public Guid Id { get; set; } // VariantId
        public string ProductName { get; set; }
        public string SKU { get; set; }
        public int CurrentStock { get; set; }
        public decimal AverageCost { get; set; }
    }
}
