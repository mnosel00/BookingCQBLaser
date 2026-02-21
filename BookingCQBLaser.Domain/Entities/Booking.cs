using BookingCQBLaser.Domain.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BookingCQBLaser.Domain.Entities
{
    public class Booking
    {
        private const int TurnaroundBufferMinutes = 30;

        public Guid Id { get; private set; }
        public string CustomerFirstName { get; private set; }
        public string CustomerLastName { get; private set; }
        public string CustomerEmail { get; private set; }
        public string CustomerPhone { get; private set; }
        public int ParticipantsCount { get; private set; }
        public PackageType Package { get; private set; }
        public DateTimeOffset StartTime { get; private set; }
        public DateTimeOffset EndTime { get; private set; }
        public string? GoogleCalendarEventId { get; private set; }
        public DateTimeOffset CreatedAt { get; private set; }

        // Constructor for ORM usage
        private Booking() { }

        public Booking(
            string firstName,
            string lastName,
            string email,
            string phone,
            int participantsCount,
            PackageType package,
            DateTimeOffset startTime)
        {
            Id = Guid.NewGuid();
            CreatedAt = DateTimeOffset.UtcNow;

            SetCustomerDetails(firstName, lastName, email, phone);
            SetBookingDetails(participantsCount, package, startTime);
        }

        public void SetCustomerDetails(string firstName, string lastName, string email, string phone)
        {
            if (string.IsNullOrWhiteSpace(firstName)) throw new ArgumentException("First name is required.", nameof(firstName));
            if (string.IsNullOrWhiteSpace(lastName)) throw new ArgumentException("Last name is required.", nameof(lastName));
            if (string.IsNullOrWhiteSpace(email)) throw new ArgumentException("Email is required.", nameof(email));
            if (string.IsNullOrWhiteSpace(phone)) throw new ArgumentException("Phone is required.", nameof(phone));

            CustomerFirstName = firstName;
            CustomerLastName = lastName;
            CustomerEmail = email;
            CustomerPhone = phone;
        }

        public void SetBookingDetails(int participantsCount, PackageType package, DateTimeOffset startTime)
        {
            if (participantsCount <= 0) throw new ArgumentException("Participants count must be greater than zero.", nameof(participantsCount));

            ParticipantsCount = participantsCount;
            Package = package;
            StartTime = startTime;

            RecalculateEndTime();
        }

        public void UpdateGoogleCalendarEventId(string? eventId)
        {
            GoogleCalendarEventId = eventId;
        }

        private void RecalculateEndTime()
        {
            var duration = Package.GetBaseDurationMinutes();
            EndTime = StartTime.AddMinutes(duration + TurnaroundBufferMinutes);
        }
    }
}
