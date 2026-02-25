using BookingCQBLaser.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BookingCQBLaser.Domain.Interfaces
{
    public interface IGoogleCalendarService
    {
        Task<IEnumerable<(DateTimeOffset Start, DateTimeOffset End)>> GetBusyPeriodsAsync(DateTimeOffset startDate, DateTimeOffset endDate, CancellationToken cancellationToken = default);
        Task<string> CreateEventAsync(Booking booking, CancellationToken cancellationToken = default);
    }
}
