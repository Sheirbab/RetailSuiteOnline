using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RetailSuite.Shared
{
    public class ApiResponse<T>
    {
        public bool Success { get; set; } = true;

        public string? Message { get; set; }

        public T? Data { get; set; }
    }
}
