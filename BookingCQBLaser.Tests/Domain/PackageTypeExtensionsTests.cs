using BookingCQBLaser.Domain.Enums;

namespace BookingCQBLaser.Tests.Domain;

public class PackageTypeExtensionsTests
{
    // Regression coverage for the intentional duration swap: Premium moved from 120 -> 90 min,
    // U1 moved from 90 -> 120 min. Getting this backwards would silently change what the arena
    // blocks out for two real packages.
    [Theory]
    [InlineData(PackageType.S1, 90)]
    [InlineData(PackageType.S2, 90)]
    [InlineData(PackageType.Premium, 90)]
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
