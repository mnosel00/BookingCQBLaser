using BookingCQBLaser.Domain.Entities;
using BookingCQBLaser.Domain.Enums;
using BookingCQBLaser.Domain.Interfaces;
using BookingCQBLaser.Infrastructure.Persistence.Configurations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BookingCQBLaser.Infrastructure.Persistence.Repositories
{
    public class BookingRepository : IBookingRepository
    {
        private readonly AppDbContext _dbContext;

        public BookingRepository(AppDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task<Booking?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            return await _dbContext.Bookings
                .FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
        }

        public async Task<IEnumerable<Booking>> GetByDateRangeAsync(DateTimeOffset start, DateTimeOffset end, CancellationToken cancellationToken = default)
        {
            start = start.ToUniversalTime();
            end = end.ToUniversalTime();

            return await _dbContext.Bookings
                .Where(b => b.StartTime >= start && b.StartTime <= end && b.PaymentStatus != PaymentStatus.Failed)
                .ToListAsync(cancellationToken);
        }

        public async Task AddAsync(Booking booking, CancellationToken cancellationToken = default)
        {
            await _dbContext.Bookings.AddAsync(booking, cancellationToken);

            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex) when (IsOverlapViolation(ex))
            {
                // The database's exclusion constraint is the final line of defense against two
                // concurrent requests both passing the application-level availability check for
                // the same time range.
                throw new InvalidOperationException(
                    "The requested time slot overlaps with an existing booking.", ex);
            }
        }

        private static bool IsOverlapViolation(DbUpdateException ex)
        {
            return ex.InnerException is PostgresException pg
                && pg.SqlState == PostgresErrorCodes.ExclusionViolation;
        }

        public async Task UpdateAsync(Booking booking, CancellationToken cancellationToken = default)
        {
            _dbContext.Bookings.Update(booking);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        public async Task DeleteAsync(Booking booking, CancellationToken cancellationToken = default)
        {
            _dbContext.Bookings.Remove(booking);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}
