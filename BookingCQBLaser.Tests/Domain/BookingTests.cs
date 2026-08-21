using BookingCQBLaser.Domain.Entities;
using BookingCQBLaser.Domain.Enums;
using BookingCQBLaser.Domain.ValueObject;

namespace BookingCQBLaser.Tests.Domain;

public class BookingTests
{
    private static CustomerInfo ValidCustomer() => new("Jan", "Kowalski", "jan@example.com", "123456789");

    private static Booking CreateBooking(
        int participantsCount = 10,
        PackageType package = PackageType.S2,
        bool isAdultGroup = true,
        string? ageRange = null,
        int durationMinutes = 90)
    {
        return new Booking(
            ValidCustomer(),
            participantsCount,
            package,
            new DateTimeOffset(2026, 9, 1, 10, 0, 0, TimeSpan.Zero),
            durationMinutes,
            isAdultGroup,
            ageRange);
    }

    [Fact]
    public void Constructor_NotAdultGroupWithoutAgeRange_Throws()
    {
        Assert.Throws<ArgumentException>(() => CreateBooking(isAdultGroup: false, ageRange: null));
    }

    [Fact]
    public void Constructor_NotAdultGroupWithBlankAgeRange_Throws()
    {
        Assert.Throws<ArgumentException>(() => CreateBooking(isAdultGroup: false, ageRange: "   "));
    }

    [Fact]
    public void Constructor_AdultGroup_ForcesAgeRangeNull_EvenIfProvided()
    {
        var booking = CreateBooking(isAdultGroup: true, ageRange: "10-12 lat");

        Assert.True(booking.IsAdultGroup);
        Assert.Null(booking.AgeRange);
    }

    [Fact]
    public void Constructor_NotAdultGroupWithAgeRange_TrimsAndKeepsIt()
    {
        var booking = CreateBooking(isAdultGroup: false, ageRange: "  10-12 lat  ");

        Assert.False(booking.IsAdultGroup);
        Assert.Equal("10-12 lat", booking.AgeRange);
    }

    [Fact]
    public void Constructor_ComputesEndTimeFromBlockedDuration()
    {
        var booking = CreateBooking(durationMinutes: 120);

        Assert.Equal(booking.StartTime.AddMinutes(120), booking.EndTime);
    }

    [Theory]
    [InlineData(PackageType.S1, 9)] // below the S1-specific minimum of 10
    [InlineData(PackageType.U1, 9)] // below the U1-specific minimum of 10
    [InlineData(PackageType.S2, 7)] // below the default minimum of 8
    [InlineData(PackageType.Max, 27)] // above the max of 26
    public void Constructor_ParticipantsOutsideAllowedRange_Throws(PackageType package, int participants)
    {
        Assert.Throws<ArgumentException>(() => CreateBooking(participantsCount: participants, package: package));
    }

    [Theory]
    [InlineData(PackageType.S1, 10)] // exactly the S1/U1 minimum
    [InlineData(PackageType.S2, 8)] // exactly the default minimum
    [InlineData(PackageType.Max, 26)] // exactly the maximum
    public void Constructor_ParticipantsAtBoundary_Succeeds(PackageType package, int participants)
    {
        var booking = CreateBooking(participantsCount: participants, package: package);

        Assert.Equal(participants, booking.ParticipantsCount);
    }

    [Fact]
    public void MarkAsFailed_WhenPending_Succeeds()
    {
        var booking = CreateBooking();

        booking.MarkAsFailed();

        Assert.Equal(PaymentStatus.Failed, booking.PaymentStatus);
    }

    [Fact]
    public void MarkAsFailed_WhenAlreadyPaid_Throws()
    {
        var booking = CreateBooking();
        booking.MarkAsPaid();

        Assert.Throws<InvalidOperationException>(() => booking.MarkAsFailed());
    }

    [Fact]
    public void MarkAsFailed_WhenAlreadyFailed_Throws()
    {
        var booking = CreateBooking();
        booking.MarkAsFailed();

        Assert.Throws<InvalidOperationException>(() => booking.MarkAsFailed());
    }
}
