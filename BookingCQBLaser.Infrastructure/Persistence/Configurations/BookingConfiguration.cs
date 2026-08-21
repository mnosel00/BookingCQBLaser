using BookingCQBLaser.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BookingCQBLaser.Infrastructure.Persistence.Configurations
{
    public class BookingConfiguration : IEntityTypeConfiguration<Booking>
    {
        public void Configure(EntityTypeBuilder<Booking> builder)
        {
            builder.ToTable("Bookings");

            builder.HasKey(b => b.Id);

            builder.OwnsOne(b => b.Customer, customer =>
            {
                customer.Property(c => c.FirstName)
                    .HasColumnName("Customer_FirstName")
                    .HasMaxLength(100)
                    .IsRequired();

                customer.Property(c => c.LastName)
                    .HasColumnName("Customer_LastName")
                    .HasMaxLength(100)
                    .IsRequired();

                customer.Property(c => c.Email)
                    .HasColumnName("Customer_Email")
                    .HasMaxLength(255)
                    .IsRequired();

                customer.Property(c => c.Phone)
                    .HasColumnName("Customer_Phone")
                    .HasMaxLength(50)
                    .IsRequired();
            });

            builder.Property(b => b.GoogleCalendarEventId)
                .IsRequired(false);

            builder.Property(b => b.Package)
                .HasConversion<string>();

            builder.Property(b => b.IsAdultGroup)
                .IsRequired()
                .HasDefaultValue(false)
                .HasColumnName("IsAdultGroup");

            builder.Property(b => b.AgeRange)
                .IsRequired(false)
                .HasMaxLength(50)
                .HasColumnName("AgeRange");

            // GetByDateRangeAsync filters on StartTime + PaymentStatus for every availability
            // check; the cleanup job filters on PaymentStatus + CreatedAt every 5 minutes.
            builder.HasIndex(b => new { b.StartTime, b.PaymentStatus })
                .HasDatabaseName("IX_Bookings_StartTime_PaymentStatus");

            builder.HasIndex(b => new { b.PaymentStatus, b.CreatedAt })
                .HasDatabaseName("IX_Bookings_PaymentStatus_CreatedAt");
        }
    }
}
