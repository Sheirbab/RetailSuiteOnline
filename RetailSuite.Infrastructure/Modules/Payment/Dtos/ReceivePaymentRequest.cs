using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RetailSuite.Infrastructure.Modules.Payment.Dtos;

public class ReceivePaymentRequest
{
    public Guid OrderId { get; set; }
    public decimal Amount { get; set; }
    public string Method { get; set; }
}