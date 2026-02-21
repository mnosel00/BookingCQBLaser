using BookingCQBLaser.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BookingCQBLaser.Domain.Interfaces
{
    public interface IBookingRepository
    {
        Task<Booking?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
        Task<IEnumerable<Booking>> GetByDateRangeAsync(DateTimeOffset start, DateTimeOffset end, CancellationToken cancellationToken = default);
        Task AddAsync(Booking booking, CancellationToken cancellationToken = default);
        Task UpdateAsync(Booking booking, CancellationToken cancellationToken = default);
        Task DeleteAsync(Booking booking, CancellationToken cancellationToken = default);
    }
}
