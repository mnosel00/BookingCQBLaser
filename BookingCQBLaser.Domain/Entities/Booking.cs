using BookingCQBLaser.Domain.Enums;
using BookingCQBLaser.Domain.ValueObject;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BookingCQBLaser.Domain.Entities
{
    public class Booking
    {
        public Guid Id { get; private set; }
        public CustomerInfo Customer { get; private set; }
        public int ParticipantsCount { get; private set; }
        public PackageType Package { get; private set; }
        public DateTimeOffset StartTime { get; private set; }
        public DateTimeOffset EndTime { get; private set; }
        public string? GoogleCalendarEventId { get; private set; }
        public DateTimeOffset CreatedAt { get; private set; }
        public PaymentStatus PaymentStatus { get; private set; } = PaymentStatus.Pending;


        // Constructor for ORM usage
        private Booking() { }

        public Booking(
            CustomerInfo customer,
            int participantsCount,
            PackageType package,
            DateTimeOffset startTime,
            int totalBlockedDurationMinutes)
        {
            Id = Guid.NewGuid();
            CreatedAt = DateTimeOffset.UtcNow;

            Customer = customer ?? throw new ArgumentNullException(nameof(customer));
            SetBookingDetails(participantsCount, package, startTime, totalBlockedDurationMinutes);
        }

        public void UpdateCustomer(CustomerInfo customer)
        {
            Customer = customer ?? throw new ArgumentNullException(nameof(customer));
        }

        public void SetBookingDetails(int participantsCount, PackageType package, DateTimeOffset startTime, int totalBlockedDurationMinutes)
        {
            if (participantsCount <= 0) throw new ArgumentException("Participants count must be greater than zero.", nameof(participantsCount));

            ParticipantsCount = participantsCount;
            Package = package;
            StartTime = startTime;
            EndTime = StartTime.AddMinutes(totalBlockedDurationMinutes);
        }

        public void UpdateGoogleCalendarEventId(string? eventId)
        {
            GoogleCalendarEventId = eventId;
        }

        public void MarkAsPaid()
        {
            PaymentStatus = PaymentStatus.Paid;
        }
        public void MarkAsFailed()
        {
            if (PaymentStatus != PaymentStatus.Pending)
            {
                throw new InvalidOperationException($"Cannot mark booking as failed when PaymentStatus is {PaymentStatus}. Only Pending bookings can be marked as failed.");
            }
            PaymentStatus = PaymentStatus.Failed;
        }
    }
}
