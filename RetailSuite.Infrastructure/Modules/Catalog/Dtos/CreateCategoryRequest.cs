using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RetailSuite.Infrastructure.Modules.Catalog.Dtos
{
    public class CreateCategoryRequest
    {
        public string Name { get; set; }
        public string Slug { get; set; }
        public Guid? ParentCategoryId { get; set; }
    }
}
