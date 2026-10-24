using BookingCQBLaser.Application.Services;
using BookingCQBLaser.Domain.Entities;
using BookingCQBLaser.Domain.Enums;
using BookingCQBLaser.Domain.Interfaces;
using BookingCQBLaser.Domain.ValueObject;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace BookingCQBLaser.Tests.Application;

public class AvailabilityCalculatorTests
{
    private sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private readonly Mock<IBookingRepository> _repository = new();
    private readonly Mock<IGoogleCalendarService> _googleCalendarService = new();
    private readonly AvailabilityOptions _options = new();

    // Fixed "now" comfortably before every target date used below, so the lead-time rule never
    // interferes with the scenario under test.
    private readonly FakeTimeProvider _timeProvider = new(new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero));

    private AvailabilityCalculator CreateCalculator(TimeProvider? timeProvider = null) =>
        new(_repository.Object, _googleCalendarService.Object, new SlotPacker(_options), _options,
            NullLogger<AvailabilityCalculator>.Instance, timeProvider ?? _timeProvider);

    private static DateTimeOffset PolandTime(int year, int month, int day, int hour, int minute)
    {
        var tz = GetPolandTimeZone();
        var local = new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, tz.GetUtcOffset(local));
    }

    private static TimeZoneInfo GetPolandTimeZone()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw"); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.FindSystemTimeZoneById("Central European Standard Time"); }
    }

    private static Booking LocalBooking(DateTimeOffset start, int durationMinutes, PackageType package = PackageType.S2)
    {
        var customer = new CustomerInfo("Jan", "Kowalski", "jan@example.com", "123456789");
        return new Booking(customer, 10, package, start, durationMinutes, true, null);
    }

    private void SetLocalBookings(params Booking[] bookings)
    {
        _repository
            .Setup(r => r.GetByDateRangeAsync(It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(bookings);
    }

    private void SetGoogleBusyPeriods(params (DateTimeOffset Start, DateTimeOffset End)[] periods)
    {
        _googleCalendarService
            .Setup(g => g.GetBusyPeriodsAsync(It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(periods);
    }

    public AvailabilityCalculatorTests()
    {
        SetLocalBookings();
        SetGoogleBusyPeriods();
    }

    [Fact]
    public async Task GetAvailableTimeSlotsAsync_Weekday_EmptyDay_EveryMorningStartPlusEveningAnchors()
    {
        // Tuesday. Morning offers every 30-min start up to 13:00 (09:00..13:00 = 9), whose blocks may
        // run into the dead afternoon; 13:00-17:30 is dead; the evening offers the 4 first-group anchors.
        var calculator = CreateCalculator();
        var date = PolandTime(2026, 9, 1, 8, 0);

        var slots = (await calculator.GetAvailableTimeSlotsAsync(date, PackageType.S2)).ToList();
        var starts = slots.Select(s => s.StartTime).ToHashSet();

        Assert.Equal(9, slots.Count); // 5 full-hour morning starts (09:00..13:00) + 4 evening anchors
        // Morning offers full hours up to 13:00; a half-hour like 09:30 only appears flush to a booking.
        Assert.Contains(PolandTime(2026, 9, 1, 9, 0), starts);
        Assert.Contains(PolandTime(2026, 9, 1, 10, 0), starts);
        Assert.DoesNotContain(PolandTime(2026, 9, 1, 9, 30), starts);
        Assert.Contains(PolandTime(2026, 9, 1, 13, 0), starts);   // last morning start; block runs past 13:00
        // Evening anchors.
        Assert.Contains(PolandTime(2026, 9, 1, 17, 30), starts);
        Assert.Contains(PolandTime(2026, 9, 1, 18, 0), starts);
        Assert.Contains(PolandTime(2026, 9, 1, 18, 30), starts);
        Assert.Contains(PolandTime(2026, 9, 1, 19, 30), starts);
        // Dead afternoon: no starts between 13:00 and 17:30.
        Assert.DoesNotContain(PolandTime(2026, 9, 1, 14, 0), starts);
        Assert.DoesNotContain(PolandTime(2026, 9, 1, 16, 0), starts);
        Assert.DoesNotContain(PolandTime(2026, 9, 1, 17, 0), starts);
    }

    [Fact]
    public async Task GetAvailableTimeSlotsAsync_Weekday_EveningGroupExists_LaterGroupsStickFlush()
    {
        // An evening group already holds 17:30-19:00. The anchors are gone; the next group must stick
        // flush (19:00), and nothing is offered before the 17:30 floor.
        SetLocalBookings(LocalBooking(PolandTime(2026, 9, 1, 17, 30), 90));
        var calculator = CreateCalculator();
        var date = PolandTime(2026, 9, 1, 8, 0);

        var slots = (await calculator.GetAvailableTimeSlotsAsync(date, PackageType.S2)).ToList();
        var starts = slots.Select(s => s.StartTime).ToHashSet();

        Assert.Contains(PolandTime(2026, 9, 1, 19, 0), starts);        // flush after the existing group
        Assert.DoesNotContain(PolandTime(2026, 9, 1, 18, 0), starts);  // anchors no longer offered
        Assert.DoesNotContain(PolandTime(2026, 9, 1, 19, 30), starts);
        Assert.Contains(PolandTime(2026, 9, 1, 9, 0), starts);         // morning unaffected
    }

    [Fact]
    public async Task GetAvailableTimeSlotsAsync_Weekday_MorningBookingDoesNotForceEveningToFlush()
    {
        // A morning group (09:00-10:30) must NOT push the evening into flush mode - the evening still
        // offers its first-group anchors.
        SetLocalBookings(LocalBooking(PolandTime(2026, 9, 1, 9, 0), 90));
        var calculator = CreateCalculator();
        var date = PolandTime(2026, 9, 1, 8, 0);

        var slots = (await calculator.GetAvailableTimeSlotsAsync(date, PackageType.S2)).ToList();
        var starts = slots.Select(s => s.StartTime).ToHashSet();

        Assert.Contains(PolandTime(2026, 9, 1, 17, 30), starts); // evening still anchored
        Assert.Contains(PolandTime(2026, 9, 1, 19, 30), starts);
    }

    [Fact]
    public async Task GetAvailableTimeSlotsAsync_WhenTheDayIsFullyOccupied_ReturnsEmpty()
    {
        // A single booking spanning 09:00-22:00 leaves no room for either session.
        SetLocalBookings(LocalBooking(PolandTime(2026, 9, 1, 9, 0), 780));
        var calculator = CreateCalculator();
        var date = PolandTime(2026, 9, 1, 8, 0);

        var slots = await calculator.GetAvailableTimeSlotsAsync(date, PackageType.Max);

        Assert.Empty(slots);
    }

    [Fact]
    public async Task GetAvailableTimeSlotsAsync_Sunday_EmptyDay_FirstGroupAnchorsBetween0900And1200()
    {
        // Sunday: the first group may start at any grid time 09:00-12:00 (free choice). Later times
        // only open up by sticking flush once a group exists. Close is 16:00.
        var calculator = CreateCalculator();
        var date = PolandTime(2026, 9, 6, 10, 0);

        var slots = (await calculator.GetAvailableTimeSlotsAsync(date, PackageType.S1)).ToList();
        var starts = slots.Select(s => s.StartTime).ToHashSet();

        Assert.Equal(7, slots.Count); // 09:00, 09:30 ... 12:00
        Assert.Equal(PolandTime(2026, 9, 6, 9, 0), slots.First().StartTime);
        Assert.Equal(PolandTime(2026, 9, 6, 12, 0), slots.Last().StartTime);
        Assert.Contains(PolandTime(2026, 9, 6, 10, 0), starts);        // free choice - not waste-filtered
        Assert.DoesNotContain(PolandTime(2026, 9, 6, 12, 30), starts); // past the 12:00 anchor cutoff
    }

    [Fact]
    public async Task GetAvailableTimeSlotsAsync_Sunday_WhenAGroupExists_LaterGroupsStickFlush()
    {
        // With a group already at 10:00-11:30, the anchors are gone; only flush starts remain (11:30),
        // and flush-before (08:30) is rejected as it is before opening.
        SetLocalBookings(LocalBooking(PolandTime(2026, 9, 6, 10, 0), 90));
        var calculator = CreateCalculator();
        var date = PolandTime(2026, 9, 6, 8, 0);

        var slots = (await calculator.GetAvailableTimeSlotsAsync(date, PackageType.S1)).ToList();
        var starts = slots.Select(s => s.StartTime).ToHashSet();

        Assert.Contains(PolandTime(2026, 9, 6, 11, 30), starts);     // flush after
        Assert.DoesNotContain(PolandTime(2026, 9, 6, 9, 0), starts); // anchors no longer offered
        Assert.DoesNotContain(PolandTime(2026, 9, 6, 10, 0), starts);
    }

    [Fact]
    public async Task GetAvailableTimeSlotsAsync_Saturday_UsesTileablePackingAllDay()
    {
        // Saturday keeps the all-day tileable packing - no anchors, no dead afternoon.
        var calculator = CreateCalculator();
        var date = PolandTime(2026, 8, 8, 10, 0);

        var slots = (await calculator.GetAvailableTimeSlotsAsync(date, PackageType.S2)).ToList();
        var starts = slots.Select(s => s.StartTime).ToHashSet();

        Assert.Equal(13, slots.Count);                              // full hours 09:00..21:00
        Assert.Contains(PolandTime(2026, 8, 8, 14, 0), starts);     // afternoon is bookable on Saturday
        Assert.DoesNotContain(PolandTime(2026, 8, 8, 9, 30), starts); // half-hour, not flush -> not offered
        Assert.DoesNotContain(PolandTime(2026, 8, 8, 21, 30), starts); // last start is 21:00
    }

    [Fact]
    public async Task GetAvailableTimeSlotsAsync_OutsideLeadTimeWindow_ReturnsEmpty()
    {
        // "Now" is fixed at 2026-08-01 (Saturday); 2026-08-03 is a Monday only 2 days out,
        // which fails the weekday 3-calendar-day rule.
        var calculator = CreateCalculator();
        var date = PolandTime(2026, 8, 3, 10, 0);

        var slots = await calculator.GetAvailableTimeSlotsAsync(date, PackageType.S1);

        Assert.Empty(slots);
    }

    [Theory]
    [InlineData(2026, 8, 4, true)]  // Tuesday, exactly 3 days out - allowed
    [InlineData(2026, 8, 3, false)] // Monday, only 2 days out - not allowed
    public void IsOnlineBookingAllowed_WeekdayLeadTimeRule(int year, int month, int day, bool expected)
    {
        var calculator = CreateCalculator();

        Assert.Equal(expected, calculator.IsOnlineBookingAllowed(PolandTime(year, month, day, 10, 0)));
    }

    [Fact]
    public void IsOnlineBookingAllowed_Saturday_ClosesAtFriday10PM()
    {
        // "now" fixed just before the Friday 22:00 Poland cutoff for the following Saturday.
        var friday10PM = PolandTime(2026, 8, 7, 21, 59);
        var calculator = CreateCalculator(new FakeTimeProvider(friday10PM));
        var saturday = PolandTime(2026, 8, 8, 10, 0);

        Assert.True(calculator.IsOnlineBookingAllowed(saturday));
    }

    [Fact]
    public void IsOnlineBookingAllowed_Saturday_AfterFriday10PMCutoff_IsDenied()
    {
        var friday1001PM = PolandTime(2026, 8, 7, 22, 1);
        var calculator = CreateCalculator(new FakeTimeProvider(friday1001PM));
        var saturday = PolandTime(2026, 8, 8, 10, 0);

        Assert.False(calculator.IsOnlineBookingAllowed(saturday));
    }

    [Fact]
    public async Task GetAvailableTimeSlotsAsync_DstSpringForwardSunday_UsesBoundaryOffsetNotMidnightOffset()
    {
        // 2027-03-28 is the EU spring-forward Sunday: Poland jumps CET(+1) -> CEST(+2) at 02:00.
        // Midnight that day is still +1; 09:00 is already +2. The old bug computed arena open
        // time using midnight's offset, which would be an hour wrong here.
        var earlyMarch = new FakeTimeProvider(new DateTimeOffset(2027, 3, 1, 0, 0, 0, TimeSpan.Zero));
        var calculator = CreateCalculator(earlyMarch);
        var date = PolandTime(2027, 3, 28, 10, 0);

        var slots = (await calculator.GetAvailableTimeSlotsAsync(date, PackageType.S1)).ToList();

        var firstSlot = slots.First();
        Assert.Equal(TimeSpan.FromHours(2), firstSlot.StartTime.Offset);
        Assert.Equal(PolandTime(2027, 3, 28, 9, 0), firstSlot.StartTime);
    }
}
