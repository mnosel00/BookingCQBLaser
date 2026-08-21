using BookingCQBLaser.Domain.ValueObject;

namespace BookingCQBLaser.Tests.Domain;

public class CustomerInfoTests
{
    [Fact]
    public void Constructor_WithValidData_Succeeds()
    {
        var customer = new CustomerInfo("Jan", "Kowalski", "jan@example.com", "123456789");

        Assert.Equal("Jan", customer.FirstName);
        Assert.Equal("jan@example.com", customer.Email);
        Assert.Equal("123456789", customer.Phone);
    }

    [Theory]
    [InlineData("", "Kowalski", "jan@example.com", "123456789")]
    [InlineData("Jan", "", "jan@example.com", "123456789")]
    [InlineData("Jan", "Kowalski", "", "123456789")]
    [InlineData("Jan", "Kowalski", "jan@example.com", "")]
    public void Constructor_WithMissingRequiredField_Throws(string firstName, string lastName, string email, string phone)
    {
        Assert.Throws<ArgumentException>(() => new CustomerInfo(firstName, lastName, email, phone));
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("missing-at-sign.com")]
    [InlineData("no-domain@")]
    public void Constructor_WithInvalidEmailFormat_Throws(string email)
    {
        Assert.Throws<ArgumentException>(() => new CustomerInfo("Jan", "Kowalski", email, "123456789"));
    }

    [Theory]
    [InlineData("12345678")] // 8 digits
    [InlineData("1234567890")] // 10 digits
    [InlineData("123-456-789")] // formatted, not raw digits
    [InlineData("abcdefghi")]
    public void Constructor_WithInvalidPhoneFormat_Throws(string phone)
    {
        Assert.Throws<ArgumentException>(() => new CustomerInfo("Jan", "Kowalski", "jan@example.com", phone));
    }
}
