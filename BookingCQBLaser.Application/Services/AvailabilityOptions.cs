namespace BookingCQBLaser.Application.Services;

/// <summary>
/// Tunable knobs for availability: arena hours, the slot grid interval, and the online-booking
/// lead-time rule. Previously hardcoded constants spread across <see cref="AvailabilityCalculator"/>;
/// pulled into one place so hours or lead time can change by configuration rather than a recompile,
/// and so the pure <see cref="SlotPacker"/> can be driven with explicit values in tests.
/// Defaults reproduce the original hardcoded behaviour.
/// </summary>
public sealed class AvailabilityOptions
{
    /// <summary>Grid the arena books on; every offered start is aligned to this interval.</summary>
    public int SlotIntervalMinutes { get; init; } = 30;

    /// <summary>Daily arena opening time (local Poland wall-clock).</summary>
    public TimeOnly ArenaOpenTime { get; init; } = new(9, 0);

    /// <summary>Closing time on every day except Sunday.</summary>
    public TimeOnly WeekdayCloseTime { get; init; } = new(23, 0);

    /// <summary>Closing time on Sunday (the arena shuts earlier).</summary>
    public TimeOnly SundayCloseTime { get; init; } = new(16, 0);

    /// <summary>Minimum calendar days ahead a Monday–Friday booking must be made.</summary>
    public int WeekdayLeadTimeDays { get; init; } = 3;

    /// <summary>
    /// Hour (local Poland) on the day before a Saturday/Sunday after which online booking for that
    /// weekend day closes — Friday 22:00 for Saturday, Saturday 22:00 for Sunday.
    /// </summary>
    public int WeekendCutoffHour { get; init; } = 22;
}
