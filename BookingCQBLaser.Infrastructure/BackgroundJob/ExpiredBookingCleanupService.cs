using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BookingCQBLaser.Domain.Enums;
using BookingCQBLaser.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BookingCQBLaser.Infrastructure.BackgroundJobs
{
    public class ExpiredBookingCleanupService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<ExpiredBookingCleanupService> _logger;

        public ExpiredBookingCleanupService(IServiceProvider serviceProvider, ILogger<ExpiredBookingCleanupService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("ExpiredBookingCleanupService is starting.");

            // Loop runs every 5 minutes
            using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));

            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await CleanupAbandonedBookingsAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred while running expired bookings cleanup.");
                }
            }
        }

        private async Task CleanupAbandonedBookingsAsync(CancellationToken cancellationToken)
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // Threshold: anything created more than 15 minutes ago
            var threshold = DateTimeOffset.UtcNow.AddMinutes(-15);

            var abandonedBookings = await dbContext.Bookings
                .Where(b => b.PaymentStatus == PaymentStatus.Pending && b.CreatedAt <= threshold)
                .ToListAsync(cancellationToken);

            if (abandonedBookings.Count > 0)
            {
                foreach (var booking in abandonedBookings)
                {
                    // Update PaymentStatus bypassing private setter via EF tracking
                    dbContext.Entry(booking).Property(b => b.PaymentStatus).CurrentValue = PaymentStatus.Failed;
                }

                await dbContext.SaveChangesAsync(cancellationToken);
                _logger.LogInformation("Cleaned up {Count} abandoned pending bookings.", abandonedBookings.Count);
            }
        }
    }
}
