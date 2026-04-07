using BookingCQBLaser.Domain.Entities;
using BookingCQBLaser.Domain.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

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
            return $"https://platnosc.hotpay.pl/?SEKRET={_options.Secret}&KWOTA={amount}&NAZWA_USLUGI=Rezerwacja&ADRES_PRZEKIEROWANIA={_options.SuccessUrl}&ID_ZAMOWIENIA={booking.Id}&EMAIL={booking.Customer.Email}";
        }

        public bool ValidateNotification(IFormCollection formData)
        {
            var kwota = formData["KWOTA"].ToString();
            var idZamowienia = formData["ID_ZAMOWIENIA"].ToString();
            var status = formData["STATUS"].ToString();
            var secure = formData["SECURE"].ToString();
            var idPlatnosci = formData["ID_PLATNOSCI"].ToString();

            if (status != "SUCCESS")
                return false;

            // HotPay Hash Format: SHA256(Password + ";" + STATUS + ";" + KWOTA + ";" + ID_ZAMOWIENIA + ";" + ID_PLATNOSCI)
            var rawString = $"{_options.Password};{status};{kwota};{idZamowienia};{idPlatnosci}";
            var computedHash = ComputeSha256(rawString);

            return string.Equals(secure, computedHash, StringComparison.OrdinalIgnoreCase);
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
