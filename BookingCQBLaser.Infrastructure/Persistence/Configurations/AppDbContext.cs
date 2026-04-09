using BookingCQBLaser.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using System;

namespace BookingCQBLaser.Infrastructure.Persistence.Configurations
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<Booking> Bookings { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // 1. APPLY ALL EXTERNAL CONFIGURATIONS
            modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

            // 2. Ensure all DateTimeOffset properties are saved and read as UTC for PostgreSQL
            foreach (var entityType in modelBuilder.Model.GetEntityTypes())
            {
                foreach (var property in entityType.GetProperties())
                {
                    if (property.ClrType == typeof(DateTimeOffset) || property.ClrType == typeof(DateTimeOffset?))
                    {
                        property.SetValueConverter(new ValueConverter<DateTimeOffset, DateTimeOffset>(
                            v => v.ToUniversalTime(),
                            v => v.ToUniversalTime()));
                    }
                }
            }
        }
    }
}
