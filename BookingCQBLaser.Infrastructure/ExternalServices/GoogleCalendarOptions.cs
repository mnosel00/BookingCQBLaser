namespace BookingCQBLaser.Infrastructure.ExternalServices;

public class GoogleCalendarOptions
{
    public string CalendarId { get; set; } = string.Empty;
    public string ServiceAccountJson { get; set; } = string.Empty;
}