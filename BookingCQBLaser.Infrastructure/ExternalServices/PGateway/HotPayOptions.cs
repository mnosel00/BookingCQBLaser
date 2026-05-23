using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BookingCQBLaser.Infrastructure.ExternalServices.PGateway
{
    public class HotPayOptions
    {
        public string Secret { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string Secure { get; set; } = string.Empty;
        public string NotificationUrl { get; set; } = string.Empty;
        public string SuccessUrl { get; set; } = string.Empty;
        public string TrustedWebhookIpAddresses { get; set; } = string.Empty;
    }
}
