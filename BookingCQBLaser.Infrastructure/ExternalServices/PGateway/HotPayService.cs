using BookingCQBLaser.Domain.Entities;
using BookingCQBLaser.Domain.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using System;
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

        public bool ValidateNotification(IFormCollection formData)
        {
            // Extract all required fields from webhook
            var hash = formData["HASH"].ToString();
            var status = formData["STATUS"].ToString();
            var kwota = formData["KWOTA"].ToString();
            var idZamowienia = formData["ID_ZAMOWIENIA"].ToString();
            var idPlatnosci = formData["ID_PLATNOSCI"].ToString();
            var sekret = formData["SEKRET"].ToString();
            var secure = formData["SECURE"].ToString();

            // Validate required fields are present
            if (string.IsNullOrEmpty(status) || string.IsNullOrEmpty(hash))
                return false;

            // CRITICAL SECURITY CHECK: Verify incoming SEKRET matches our configured secret BEFORE computing hash
            if (!string.Equals(sekret, _options.Secret, StringComparison.Ordinal))
            {
                return false;
            }

            // Compute hash according to HotPay documentation formula
            var rawString = $"{_options.Password};{kwota};{idPlatnosci};{idZamowienia};{status};{secure};{sekret}";
            var computedHash = ComputeSha256(rawString);

            // Compare hashes (case-insensitive as per HotPay specs)
            return string.Equals(hash, computedHash, StringComparison.OrdinalIgnoreCase);
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
