using System.Security.Cryptography;
using System.Text;
using BookingCQBLaser.Domain.Entities;
using BookingCQBLaser.Domain.Enums;
using BookingCQBLaser.Domain.ValueObject;
using BookingCQBLaser.Infrastructure.ExternalServices.PGateway;
using Microsoft.Extensions.Options;

namespace BookingCQBLaser.Tests.Infrastructure;

public class HotPayServiceTests
{
    private static readonly HotPayOptions Options = new()
    {
        Secret = "test-secret",
        Password = "test-password",
        Secure = "test-secure",
        SuccessUrl = "https://comboarena.netlify.app/sukces"
    };

    private static HotPayService CreateService() => new(Microsoft.Extensions.Options.Options.Create(Options));

    private static Booking CreateBooking()
    {
        var customer = new CustomerInfo("Jan", "Kowalski", "jan@example.com", "123456789");
        return new Booking(
            customer, 10, PackageType.S2,
            new DateTimeOffset(2026, 9, 1, 10, 0, 0, TimeSpan.Zero),
            90, true, null);
    }

    private static string Sha256Hex(string raw)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        var sb = new StringBuilder();
        foreach (var b in bytes) sb.Append(b.ToString("x2"));
        return sb.ToString();
    }

    [Fact]
    public void GeneratePaymentUrl_WithNullBooking_Throws()
    {
        var service = CreateService();
        Assert.Throws<ArgumentNullException>(() => service.GeneratePaymentUrl(null!, 304));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void GeneratePaymentUrl_WithNonPositiveAmount_Throws(int amount)
    {
        var service = CreateService();
        Assert.Throws<ArgumentException>(() => service.GeneratePaymentUrl(CreateBooking(), amount));
    }

    [Fact]
    public void GeneratePaymentUrl_IncludesExpectedParametersAndValidHash()
    {
        var service = CreateService();
        var booking = CreateBooking();

        var url = service.GeneratePaymentUrl(booking, 304);

        Assert.Contains("KWOTA=304", url);
        Assert.Contains($"ID_ZAMOWIENIA={booking.Id}", url);
        Assert.Contains($"SEKRET={Uri.EscapeDataString(Options.Secret)}", url);

        var expectedHash = Sha256Hex($"{Options.Password};304;Rezerwacja;{Options.SuccessUrl};{booking.Id};{Options.Secret}");
        Assert.Contains($"HASH={expectedHash}", url);
    }

    private static Dictionary<string, string> ValidNotification(string status = "SUCCESS", string kwota = "304")
    {
        const string idPlatnosci = "PMT-1";
        const string idZamowienia = "11111111-1111-1111-1111-111111111111";

        var hash = Sha256Hex($"{Options.Password};{kwota};{idPlatnosci};{idZamowienia};{status};{Options.Secure};{Options.Secret}");

        return new Dictionary<string, string>
        {
            ["STATUS"] = status,
            ["KWOTA"] = kwota,
            ["ID_PLATNOSCI"] = idPlatnosci,
            ["ID_ZAMOWIENIA"] = idZamowienia,
            ["SECURE"] = Options.Secure,
            ["SEKRET"] = Options.Secret,
            ["HASH"] = hash,
        };
    }

    [Fact]
    public void ValidateNotification_WithCorrectSignature_ReturnsTrue()
    {
        var service = CreateService();
        Assert.True(service.ValidateNotification(ValidNotification()));
    }

    [Fact]
    public void ValidateNotification_HashIsCaseInsensitive()
    {
        var service = CreateService();
        var notification = ValidNotification();
        notification["HASH"] = notification["HASH"].ToUpperInvariant();

        Assert.True(service.ValidateNotification(notification));
    }

    [Fact]
    public void ValidateNotification_WrongSekret_ReturnsFalse()
    {
        var service = CreateService();
        var notification = ValidNotification();
        notification["SEKRET"] = "wrong-secret";

        Assert.False(service.ValidateNotification(notification));
    }

    [Fact]
    public void ValidateNotification_TamperedAmount_ReturnsFalse()
    {
        var service = CreateService();
        var notification = ValidNotification();
        // HASH was computed for KWOTA=304; changing it after the fact must invalidate the hash.
        notification["KWOTA"] = "1";

        Assert.False(service.ValidateNotification(notification));
    }

    [Fact]
    public void ValidateNotification_MissingStatusOrHash_ReturnsFalse()
    {
        var service = CreateService();
        var notification = ValidNotification();
        notification.Remove("STATUS");

        Assert.False(service.ValidateNotification(notification));
    }
}
