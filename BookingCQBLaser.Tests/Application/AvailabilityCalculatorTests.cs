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
    public async Task GetAvailableTimeSlotsAsync_EmptyDay_OffersEveryGapFreeStartAndSkipsOrphaningOnes()
    {
        // Tuesday, arena 09:00-23:00 (840 min), S2 blocks 90 min. On the 30-min grid that is 26
        // candidate starts, but the packer drops the 6 whose leftover would strand arena time it
        // can never fill (a 30/60/150-min gap): 09:30, 10:00, 11:30, 19:00, 20:30, 21:00.
        var calculator = CreateCalculator();
        var date = PolandTime(2026, 9, 1, 12, 0);

        var slots = (await calculator.GetAvailableTimeSlotsAsync(date, PackageType.S2)).ToList();
        var starts = slots.Select(s => s.StartTime).ToHashSet();

        Assert.Equal(20, slots.Count);
        Assert.Equal(PolandTime(2026, 9, 1, 9, 0), slots.First().StartTime);
        Assert.Equal(PolandTime(2026, 9, 1, 21, 30), slots.Last().StartTime);
        Assert.Equal(840, slots.First().MaxAvailableDurationMinutes); // full day still open
        Assert.Equal(90, slots.Last().MaxAvailableDurationMinutes); // exactly the package's duration

        // Gap-free interior starts are offered...
        Assert.Contains(PolandTime(2026, 9, 1, 10, 30), starts);
        // ...but starts whose leftover is an unfillable 30 / 60 / 150-min gap are not.
        Assert.DoesNotContain(PolandTime(2026, 9, 1, 9, 30), starts);  // 30-min gap before
        Assert.DoesNotContain(PolandTime(2026, 9, 1, 10, 0), starts);  // 60-min gap before
        Assert.DoesNotContain(PolandTime(2026, 9, 1, 11, 30), starts); // 150-min gap before
    }

    [Fact]
    public async Task GetAvailableTimeSlotsAsync_OneExistingBooking_OffersInteriorTileableStartsNotJustEdges()
    {
        // Existing booking 14:00-15:30 splits the day into [09:00,14:00) = 300 min and
        // [15:30,23:00) = 450 min. S2 blocks 90. The packer offers every gap-free start in each
        // window - including interior ones that tile cleanly, not only the flush edges.
        SetLocalBookings(LocalBooking(PolandTime(2026, 9, 1, 14, 0), 90));
        var calculator = CreateCalculator();
        var date = PolandTime(2026, 9, 1, 12, 0);

        var slots = (await calculator.GetAvailableTimeSlotsAsync(date, PackageType.S2)).ToList();
        var starts = slots.Select(s => s.StartTime).ToHashSet();

        Assert.Equal(11, slots.Count);
        // First window: flush edges plus tileable interiors.
        Assert.Contains(PolandTime(2026, 9, 1, 9, 0), starts);   // flush start
        Assert.Contains(PolandTime(2026, 9, 1, 10, 30), starts); // interior: 90|90|120
        Assert.Contains(PolandTime(2026, 9, 1, 11, 0), starts);  // interior: 120|90|90
        Assert.Contains(PolandTime(2026, 9, 1, 12, 30), starts); // flush end
        // Second window edges.
        Assert.Contains(PolandTime(2026, 9, 1, 15, 30), starts);
        Assert.Contains(PolandTime(2026, 9, 1, 21, 30), starts);
        // Orphaning starts are still excluded.
        Assert.DoesNotContain(PolandTime(2026, 9, 1, 9, 30), starts);
        Assert.DoesNotContain(PolandTime(2026, 9, 1, 10, 0), starts);
    }

    [Fact]
    public async Task GetAvailableTimeSlotsAsync_AwkwardGapThatCannotTile_StillOffersLeastWastefulSlots()
    {
        // A booking 11:30-23:00 leaves a lone 150-minute morning window [09:00,11:30). 150 can't be
        // tiled exactly by 90/120, so some arena time is unavoidably stranded - but the packer must
        // still offer bookable slots (the least-wasteful placements), not refuse the window outright.
        SetLocalBookings(LocalBooking(PolandTime(2026, 9, 1, 11, 30), 690));
        var calculator = CreateCalculator();
        var date = PolandTime(2026, 9, 1, 8, 0);

        var slots = (await calculator.GetAvailableTimeSlotsAsync(date, PackageType.S2)).ToList();

        Assert.NotEmpty(slots);
        Assert.All(slots, s => Assert.True(
            s.StartTime >= PolandTime(2026, 9, 1, 9, 0) && s.StartTime <= PolandTime(2026, 9, 1, 10, 0)));
    }

    [Fact]
    public async Task GetAvailableTimeSlotsAsync_WindowSmallerThanPackageDuration_OffersNothingForThatWindow()
    {
        // Booking occupies 09:00-22:00, leaving only a 60-minute window - too small for Max (120 min).
        SetLocalBookings(LocalBooking(PolandTime(2026, 9, 1, 9, 0), 780));
        var calculator = CreateCalculator();
        var date = PolandTime(2026, 9, 1, 8, 0);

        var slots = await calculator.GetAvailableTimeSlotsAsync(date, PackageType.Max);

        Assert.Empty(slots);
    }

    [Fact]
    public async Task GetAvailableTimeSlotsAsync_SundayArenaHours_CloseAt16NotTheUsual23()
    {
        // 2026-09-06 is a Sunday - close time should be 16:00, not the weekday 23:00.
        var calculator = CreateCalculator();
        var date = PolandTime(2026, 9, 6, 10, 0);

        var slots = (await calculator.GetAvailableTimeSlotsAsync(date, PackageType.S1)).ToList();

        Assert.Equal(PolandTime(2026, 9, 6, 14, 30), slots.Last().StartTime); // 16:00 - 90min
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
