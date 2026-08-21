using System;
using System.Text.RegularExpressions;

namespace BookingCQBLaser.Domain.ValueObject
{
    public record CustomerInfo
    {
        private static readonly Regex EmailPattern = new(
            @"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled);

        private static readonly Regex PhonePattern = new(
            @"^\d{9}$", RegexOptions.Compiled);

        public string FirstName { get; }
        public string LastName { get; }
        public string Email { get; }
        public string Phone { get; }

        private CustomerInfo() { }

        public CustomerInfo(string firstName, string lastName, string email, string phone)
        {
            if (string.IsNullOrWhiteSpace(firstName)) throw new ArgumentException("First name is required.", nameof(firstName));
            if (string.IsNullOrWhiteSpace(lastName)) throw new ArgumentException("Last name is required.", nameof(lastName));
            if (string.IsNullOrWhiteSpace(email)) throw new ArgumentException("Email is required.", nameof(email));
            if (string.IsNullOrWhiteSpace(phone)) throw new ArgumentException("Phone is required.", nameof(phone));

            if (!EmailPattern.IsMatch(email)) throw new ArgumentException("Email is not a valid address.", nameof(email));
            if (!PhonePattern.IsMatch(phone)) throw new ArgumentException("Phone must be exactly 9 digits.", nameof(phone));

            FirstName = firstName;
            LastName = lastName;
            Email = email;
            Phone = phone;
        }
    }
}
