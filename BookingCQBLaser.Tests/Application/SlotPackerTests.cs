using BookingCQBLaser.Application.DTOs;
using BookingCQBLaser.Application.Services;

namespace BookingCQBLaser.Tests.Application;

/// <summary>
/// Direct tests of the pure packing geometry - no database, Google Calendar or timezone involved.
/// Durations: S-class packages block 90 min, U/Max-class block 120; the grid is 30 min, so the only
/// leftover sizes that can't be filled exactly by {90,120} are 30, 60 and 150 minutes.
/// </summary>
public class SlotPackerTests
{
    private readonly SlotPacker _packer = new(new AvailabilityOptions());

    private static readonly DateTimeOffset Open = At(9, 0);
    private static DateTimeOffset At(int hour, int minute) =>
        new(2026, 9, 1, hour, minute, 0, TimeSpan.FromHours(2));

    private static readonly (DateTimeOffset, DateTimeOffset)[] NoBusy = Array.Empty<(DateTimeOffset, DateTimeOffset)>();

    private IReadOnlyList<TimeSlotDto> Pack(
        DateTimeOffset end,
        int blocked,
        int client,
        params (DateTimeOffset Start, DateTimeOffset End)[] busy)
        => _packer.PackTileable(Open, end, busy, blocked, client);

    [Fact]
    public void EmptyDay_OffersEveryGridStartThatFits()
    {
        // 09:00-23:00 = 840 min, 90-min package: every 30-min start from 09:00 to 21:30 = 26.
        var slots = Pack(At(23, 0), blocked: 90, client: 50, NoBusy);
        var starts = slots.Select(s => s.StartTime).ToHashSet();

        Assert.Equal(26, slots.Count);
        Assert.Equal(At(9, 0), slots.First().StartTime);
        Assert.Equal(At(21, 30), slots.Last().StartTime);
        // MaxAvailableDurationMinutes is the bookable game length (the 90-min package here), not room.
        Assert.All(slots, s => Assert.Equal(90, s.MaxAvailableDurationMinutes));
        // Starts that leave an idle gap are offered too - choice beats zero idle minutes.
        Assert.Contains(At(9, 30), starts);
        Assert.Contains(At(10, 0), starts);
        Assert.Contains(At(11, 30), starts);
    }

    [Fact]
    public void WindowExactlyOnePackageLong_OffersASingleFlushStart()
    {
        var slots = Pack(At(10, 30), blocked: 90, client: 50, NoBusy); // 90-min window

        Assert.Single(slots);
        Assert.Equal(At(9, 0), slots[0].StartTime);
        Assert.Equal(90, slots[0].MaxAvailableDurationMinutes);
    }

    [Fact]
    public void WindowSmallerThanPackage_OffersNothing()
    {
        var slots = Pack(At(10, 0), blocked: 90, client: 50, NoBusy); // 60-min window < 90

        Assert.Empty(slots);
    }

    [Fact]
    public void NearClose_WhereOnlyAShorterGameFits_OffersADowngradeSlot()
    {
        // 150-min window (09:00-11:30) for a 120-min package. 09:00/09:30 fit the full game; at 10:00
        // only a 90-min game still fits before close, so it's offered as a downgrade (MaxAvail = 90).
        var slots = Pack(At(11, 30), blocked: 120, client: 80, NoBusy);
        var byStart = slots.ToDictionary(s => s.StartTime, s => s.MaxAvailableDurationMinutes);

        Assert.Equal(120, byStart[At(9, 0)]);
        Assert.Equal(120, byStart[At(9, 30)]);
        Assert.Equal(90, byStart[At(10, 0)]); // downgrade: only a 1.5h game fits here
    }

    [Fact]
    public void WhereOnlyAShorterGameFitsBetweenTwoBookings_ItIsOfferedAsADowngrade()
    {
        // Two 120-min bookings leave a 90-min hole [11:00,12:30]. A Combat (120) can't fit, but a
        // 90-min game fits flush, so the start is offered with MaxAvailableDurationMinutes = 90.
        var slots = Pack(At(23, 0), blocked: 120, client: 110,
            (At(9, 0), At(11, 0)), (At(12, 30), At(14, 30)));

        var hole = slots.SingleOrDefault(s => s.StartTime == At(11, 0));
        Assert.NotNull(hole);
        Assert.Equal(90, hole!.MaxAvailableDurationMinutes);
    }

    [Fact]
    public void BusyPeriodSplitsTheDay_SmallDeadBreakNextToTheBookingIsNotOffered()
    {
        // Busy 14:00-15:30 → windows [09:00,14:00)=300 and [15:30,23:00)=450.
        var slots = Pack(At(23, 0), blocked: 90, client: 50, (At(14, 0), At(15, 30)));
        var starts = slots.Select(s => s.StartTime).ToHashSet();

        // First window: 10:00 leaves 120 min before the 14:00 booking (sellable) -> offered.
        Assert.Contains(At(10, 0), starts);
        Assert.Contains(At(12, 30), starts);      // flush to the 14:00 booking
        Assert.DoesNotContain(At(12, 0), starts); // would leave a 30-min dead break before 14:00
        Assert.DoesNotContain(At(11, 30), starts); // would leave a 60-min dead break before 14:00
        // Second window: 16:00 would leave a 30-min dead break after the booking ends (15:30) -> no.
        Assert.Contains(At(15, 30), starts);      // flush after the booking
        Assert.DoesNotContain(At(16, 0), starts);
        Assert.Contains(At(17, 0), starts);       // leaves 90 min after the booking (sellable)
    }

    [Fact]
    public void TheUserScenarios_Combat10amAllowed_But5pmDeadBreakBlocked()
    {
        // Existing booking 14:00-16:00 (a 2h game). Open 09:00, close 23:00.
        var slots = Pack(At(23, 0), blocked: 120, client: 110, (At(14, 0), At(16, 0)));
        var starts = slots.Select(s => s.StartTime).ToHashSet();

        // Combat at 10:00 (10:00-12:00) leaves 12:00-14:00 = 120 min before the booking (sellable).
        Assert.Contains(At(10, 0), starts);
        // 17:00 would leave 16:00-17:00 = 60 min dead after the booking -> blocked.
        Assert.DoesNotContain(At(17, 0), starts);
        Assert.Contains(At(16, 0), starts); // flush after the booking
        Assert.Contains(At(18, 0), starts); // leaves 120 min after the booking (sellable)
    }

    [Fact]
    public void SlotDisplayEnd_UsesClientGameDurationNotTheBlockedDuration()
    {
        var slots = Pack(At(23, 0), blocked: 90, client: 50, NoBusy);
        var first = slots[0];

        Assert.Equal(At(9, 0), first.StartTime);
        Assert.Equal(At(9, 50), first.EndTime);             // client-facing 50 min, not 90
        Assert.Equal(90, first.MaxAvailableDurationMinutes); // the bookable game length
    }

    [Fact]
    public void OverlappingBusyPeriods_AreMergedBeforePacking()
    {
        // Two overlapping blocks 12:00-13:00 and 12:30-14:00 collapse to one 12:00-14:00 busy period,
        // so the morning window is [09:00,12:00)=180. 09:00 leaves a sellable 90 before the booking and
        // 10:30 is flush to it; 09:30/10:00 would strand a 60/30-min dead break, so they're dropped.
        var slots = Pack(At(23, 0), blocked: 90, client: 50,
            (At(12, 0), At(13, 0)), (At(12, 30), At(14, 0)));
        var morning = slots.Where(s => s.StartTime < At(12, 0)).Select(s => s.StartTime).ToHashSet();

        Assert.Equal(new HashSet<DateTimeOffset> { At(9, 0), At(10, 30) }, morning);
    }

    // ---- Anchored-then-flush (Sunday / weekday evening) ----

    private static readonly DateTimeOffset[] EveningAnchors =
        { At(17, 30), At(18, 0), At(18, 30), At(19, 30) };

    [Fact]
    public void AnchoredRegion_WhileEmpty_OffersEveryAnchorThatFits()
    {
        var slots = _packer.PackAnchoredThenFlush(
            regionFloor: At(17, 30), regionEnd: At(23, 0), EveningAnchors,
            regionHasGroup: false, NoBusy, packageBlockedDurationMinutes: 90, clientGameDurationMinutes: 50);

        Assert.Equal(EveningAnchors.ToHashSet(), slots.Select(s => s.StartTime).ToHashSet());
    }

    [Fact]
    public void AnchoredRegion_WhenAGroupExists_OffersFlushAfterButNotBelowTheFloor()
    {
        // First group took 17:30-19:00. The next must stick flush: 19:00 (after it). 16:00 (before it)
        // is below the 17:30 floor and must not be offered.
        var slots = _packer.PackAnchoredThenFlush(
            regionFloor: At(17, 30), regionEnd: At(23, 0), EveningAnchors,
            regionHasGroup: true, new[] { (At(17, 30), At(19, 0)) },
            packageBlockedDurationMinutes: 90, clientGameDurationMinutes: 50);

        Assert.Equal(new HashSet<DateTimeOffset> { At(19, 0) }, slots.Select(s => s.StartTime).ToHashSet());
    }

    [Fact]
    public void AnchoredRegion_FlushBeforeAnExistingGroup_IsOfferedWhenAtOrAboveTheFloor()
    {
        // First group chose the 19:30 anchor (19:30-21:00). A 90-min group can flush in before it at
        // 18:00 (>=17:30) and flush after it at 21:00.
        var slots = _packer.PackAnchoredThenFlush(
            regionFloor: At(17, 30), regionEnd: At(23, 0), EveningAnchors,
            regionHasGroup: true, new[] { (At(19, 30), At(21, 0)) },
            packageBlockedDurationMinutes: 90, clientGameDurationMinutes: 50);

        Assert.Equal(
            new HashSet<DateTimeOffset> { At(18, 0), At(21, 0) },
            slots.Select(s => s.StartTime).ToHashSet());
    }

    [Fact]
    public void AnchoredRegion_AnchorsThatWouldOverrunTheClose_AreExcluded()
    {
        // Region closes 20:00. A 120-min package can start at 18:00 (→20:00) but not 19:00 (→21:00).
        var slots = _packer.PackAnchoredThenFlush(
            regionFloor: At(17, 30), regionEnd: At(20, 0),
            new[] { At(18, 0), At(19, 0) }, regionHasGroup: false, NoBusy,
            packageBlockedDurationMinutes: 120, clientGameDurationMinutes: 80);

        Assert.Equal(new HashSet<DateTimeOffset> { At(18, 0) }, slots.Select(s => s.StartTime).ToHashSet());
    }
}
