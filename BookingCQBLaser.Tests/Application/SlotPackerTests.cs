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
        => _packer.Pack(Open, end, busy, blocked, client);

    [Fact]
    public void EmptyDay_OffersEveryGapFreeStart_AndNoOrphaningOne()
    {
        // 09:00-23:00 = 840 min, 90-min package. 20 of the 26 grid starts tile cleanly.
        var slots = Pack(At(23, 0), blocked: 90, client: 50, NoBusy);
        var starts = slots.Select(s => s.StartTime).ToHashSet();

        Assert.Equal(20, slots.Count);
        Assert.Contains(At(9, 0), starts);
        Assert.Contains(At(10, 30), starts);  // interior, tiles 90|...
        Assert.Contains(At(21, 30), starts);  // flush end
        Assert.DoesNotContain(At(9, 30), starts);   // would strand 30 before
        Assert.DoesNotContain(At(10, 0), starts);   // would strand 60 before
        Assert.DoesNotContain(At(11, 30), starts);  // would strand 150 (unfillable) before
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
    public void Awkward150MinuteWindow_StillOffersLeastWastefulStarts()
    {
        // 150 can't be tiled exactly by 120; every placement strands 30, so offer them rather than
        // refusing the window.
        var slots = Pack(At(11, 30), blocked: 120, client: 80, NoBusy); // 150-min window

        Assert.NotEmpty(slots);
        Assert.All(slots, s => Assert.True(s.StartTime >= At(9, 0) && s.StartTime <= At(9, 30)));
    }

    [Fact]
    public void BusyPeriodSplitsTheDay_InteriorTileableStartsOfferedInEachWindow()
    {
        // Busy 14:00-15:30 → windows [09:00,14:00)=300 and [15:30,23:00)=450.
        var slots = Pack(At(23, 0), blocked: 90, client: 50, (At(14, 0), At(15, 30)));
        var starts = slots.Select(s => s.StartTime).ToHashSet();

        Assert.Contains(At(10, 30), starts); // interior of first window
        Assert.Contains(At(11, 0), starts);  // interior of first window
        Assert.DoesNotContain(At(10, 0), starts); // would strand 60
        Assert.Contains(At(15, 30), starts); // flush start of second window
    }

    [Fact]
    public void SlotDisplayEnd_UsesClientGameDurationNotTheBlockedDuration()
    {
        var slots = Pack(At(23, 0), blocked: 90, client: 50, NoBusy);
        var first = slots[0];

        Assert.Equal(At(9, 0), first.StartTime);
        Assert.Equal(At(9, 50), first.EndTime);               // client-facing 50 min, not 90
        Assert.Equal(840, first.MaxAvailableDurationMinutes); // room to end of window
    }

    [Fact]
    public void OverlappingBusyPeriods_AreMergedBeforePacking()
    {
        // Two overlapping blocks 12:00-13:00 and 12:30-14:00 collapse to one 12:00-14:00 busy period,
        // so the morning window is [09:00,12:00)=180 → tiles as 90|90, offering 09:00 and 10:30.
        var slots = Pack(At(23, 0), blocked: 90, client: 50,
            (At(12, 0), At(13, 0)), (At(12, 30), At(14, 0)));
        var morning = slots.Where(s => s.StartTime < At(12, 0)).Select(s => s.StartTime).ToHashSet();

        Assert.Equal(new HashSet<DateTimeOffset> { At(9, 0), At(10, 30) }, morning);
    }
}
