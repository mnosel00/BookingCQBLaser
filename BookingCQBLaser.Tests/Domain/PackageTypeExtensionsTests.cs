using BookingCQBLaser.Domain.Enums;

namespace BookingCQBLaser.Tests.Domain;

public class PackageTypeExtensionsTests
{
    // Regression coverage for the per-package blocked (calendar) duration. Premium blocks the arena
    // for 2h; U1 is 120 while S1/S2 are 90. Getting any of these wrong silently changes how much
    // arena time a real package reserves.
    [Theory]
    [InlineData(PackageType.S1, 90)]
    [InlineData(PackageType.S2, 90)]
    [InlineData(PackageType.Premium, 120)]
    [InlineData(PackageType.Max, 120)]
    [InlineData(PackageType.U1, 120)]
    [InlineData(PackageType.U2, 120)]
    [InlineData(PackageType.U3, 120)]
    [InlineData(PackageType.Combat, 120)]
    public void GetBaseDurationMinutes_ReturnsExpectedBlockedDuration(PackageType package, int expectedMinutes)
    {
        Assert.Equal(expectedMinutes, package.GetBaseDurationMinutes());
    }
}
