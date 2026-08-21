using BookingCQBLaser.Domain.Entities;
using BookingCQBLaser.Domain.Interfaces;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace BookingCQBLaser.Infrastructure.ExternalServices.PGateway
{
    public class HotPayService : IHotPayService
    {
        private readonly HotPayOptions _options;

        public HotPayService(IOptions<HotPayOptions> options)
        {
            _options = options.Value;
        }

        public string GeneratePaymentUrl(Booking booking, int amount)
        {
            if (booking == null)
                throw new ArgumentNullException(nameof(booking));

            if (amount <= 0)
                throw new ArgumentException("Amount must be greater than zero.", nameof(amount));

            // Compute hash according to HotPay documentation
            var hashInput = $"{_options.Password};{amount};Rezerwacja;{_options.SuccessUrl};{booking.Id};{_options.Secret}";
            var hash = ComputeSha256(hashInput);

            // Build payment URL with all required parameters including mandatory HASH
            var paymentUrl = $"https://platnosc.hotpay.pl/?" +
                $"SEKRET={Uri.EscapeDataString(_options.Secret)}" +
                $"&KWOTA={amount}" +
                $"&NAZWA_USLUGI=Rezerwacja" +
                $"&ADRES_WWW={Uri.EscapeDataString(_options.SuccessUrl)}" +
                $"&ID_ZAMOWIENIA={booking.Id}" +
                $"&EMAIL={Uri.EscapeDataString(booking.Customer.Email)}" +
                $"&HASH={hash}";

            return paymentUrl;
        }

        public bool ValidateNotification(IReadOnlyDictionary<string, string> formData)
        {
            // Extract all required fields from webhook
            var hash = formData.GetValueOrDefault("HASH", string.Empty);
            var status = formData.GetValueOrDefault("STATUS", string.Empty);
            var kwota = formData.GetValueOrDefault("KWOTA", string.Empty);
            var idZamowienia = formData.GetValueOrDefault("ID_ZAMOWIENIA", string.Empty);
            var idPlatnosci = formData.GetValueOrDefault("ID_PLATNOSCI", string.Empty);
            var sekret = formData.GetValueOrDefault("SEKRET", string.Empty);
            var secure = formData.GetValueOrDefault("SECURE", string.Empty);

            // Validate required fields are present
            if (string.IsNullOrEmpty(status) || string.IsNullOrEmpty(hash))
                return false;

            // CRITICAL SECURITY CHECK: Verify incoming SEKRET matches our configured secret BEFORE computing hash.
            // Constant-time comparison so a network attacker can't use response-timing to guess the secret.
            if (!FixedTimeEquals(sekret, _options.Secret))
            {
                return false;
            }

            // Compute hash according to HotPay documentation formula
            var rawString = $"{_options.Password};{kwota};{idPlatnosci};{idZamowienia};{status};{secure};{sekret}";
            var computedHash = ComputeSha256(rawString);

            // Compare hashes (case-insensitive as per HotPay specs), still constant-time
            return FixedTimeEquals(hash.ToLowerInvariant(), computedHash);
        }

        private static bool FixedTimeEquals(string a, string b)
        {
            var aBytes = Encoding.UTF8.GetBytes(a);
            var bBytes = Encoding.UTF8.GetBytes(b);

            if (aBytes.Length != bBytes.Length)
            {
                return false;
            }

            return CryptographicOperations.FixedTimeEquals(aBytes, bBytes);
        }

        private static string ComputeSha256(string rawData)
        {
            using var sha256 = SHA256.Create();
            var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(rawData));
            var builder = new StringBuilder();
            foreach (var b in bytes)
            {
                builder.Append(b.ToString("x2"));
            }
            return builder.ToString();
        }
    }
}
