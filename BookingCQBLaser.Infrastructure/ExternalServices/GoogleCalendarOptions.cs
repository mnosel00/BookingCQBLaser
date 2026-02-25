using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BookingCQBLaser.Infrastructure.ExternalServices
{
    public class GoogleCalendarOptions
    {
        public string CalendarId { get; set; } = string.Empty;
        public string ServiceAccountJson { get; set; } = string.Empty;
    }
}
