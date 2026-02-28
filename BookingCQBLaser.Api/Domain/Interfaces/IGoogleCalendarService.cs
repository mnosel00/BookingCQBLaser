using BookingCQBLaser.Domain.Entities;

namespace BookingCQBLaser.Domain.Interfaces;

public interface IGoogleCalendarService
{
    Task<IEnumerable<(DateTimeOffset Start, DateTimeOffset End)>> GetBusyPeriodsAsync(DateTimeOffset startDate, DateTimeOffset endDate, CancellationToken cancellationToken = default);
    Task<string> CreateEventAsync(Booking booking, CancellationToken cancellationToken = default);
}