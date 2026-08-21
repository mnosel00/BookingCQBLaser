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
        public bool IsAdultGroup { get; private set; }
        public string? AgeRange { get; private set; }



        // Constructor for ORM usage
        private Booking() { }

        public Booking(
            CustomerInfo customer,
            int participantsCount,
            PackageType package,
            DateTimeOffset startTime,
            int totalBlockedDurationMinutes,
            bool isAdultGroup,
            string? ageRange)
        {
            Id = Guid.NewGuid();
            CreatedAt = DateTimeOffset.UtcNow;

            Customer = customer ?? throw new ArgumentNullException(nameof(customer));

            if (!isAdultGroup && string.IsNullOrWhiteSpace(ageRange))
            {
                throw new ArgumentException(
                    "AgeRange is required when IsAdultGroup is false. Please provide an age range for non-adult groups.",
                    nameof(ageRange));
            }

            IsAdultGroup = isAdultGroup;
            AgeRange = isAdultGroup ? null : ageRange?.Trim();

            SetBookingDetails(participantsCount, package, startTime, totalBlockedDurationMinutes);
        }

        public void UpdateCustomer(CustomerInfo customer)
        {
            Customer = customer ?? throw new ArgumentNullException(nameof(customer));
        }

        private const int MaxParticipants = 26;
        private const int DefaultMinParticipants = 8;
        private const int HigherMinParticipants = 10;

        public void SetBookingDetails(int participantsCount, PackageType package, DateTimeOffset startTime, int totalBlockedDurationMinutes)
        {
            var minParticipants = (package == PackageType.S1 || package == PackageType.U1)
                ? HigherMinParticipants
                : DefaultMinParticipants;

            if (participantsCount < minParticipants || participantsCount > MaxParticipants)
            {
                throw new ArgumentException(
                    $"Participants count for package {package} must be between {minParticipants} and {MaxParticipants}.",
                    nameof(participantsCount));
            }

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
