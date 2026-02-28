using BookingCQBLaser.Domain.Entities;
using BookingCQBLaser.Domain.Interfaces;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Calendar.v3;
using Google.Apis.Calendar.v3.Data;
using Google.Apis.Services;
using Microsoft.Extensions.Options;

namespace BookingCQBLaser.Infrastructure.ExternalServices;

public class GoogleCalendarService : IGoogleCalendarService
{
    private readonly GoogleCalendarOptions _options;
    private readonly string[] _scopes = { CalendarService.Scope.Calendar };
    private readonly string _applicationName = "BookingCQBLaser";

    public GoogleCalendarService(IOptions<GoogleCalendarOptions> options)
    {
        _options = options.Value;
    }

    private CalendarService CreateCalendarService()
    {
        var credential = GoogleCredential.FromJson(_options.ServiceAccountJson)
            .CreateScoped(_scopes);

        return new CalendarService(new BaseClientService.Initializer()
        {
            HttpClientInitializer = credential,
            ApplicationName = _applicationName,
        });
    }

    public async Task<IEnumerable<(DateTimeOffset Start, DateTimeOffset End)>> GetBusyPeriodsAsync(DateTimeOffset startDate, DateTimeOffset endDate, CancellationToken cancellationToken = default)
    {
        var service = CreateCalendarService();

        var request = new FreeBusyRequest
        {
            TimeMin = startDate.UtcDateTime,
            TimeMax = endDate.UtcDateTime,
            Items = new List<FreeBusyRequestItem> { new FreeBusyRequestItem { Id = _options.CalendarId } }
        };

        var query = service.Freebusy.Query(request);
        var response = await query.ExecuteAsync(cancellationToken);

        var busyList = new List<(DateTimeOffset Start, DateTimeOffset End)>();

        if (response.Calendars.TryGetValue(_options.CalendarId, out var calendarBusy) && calendarBusy.Busy != null)
        {
            foreach (var busyPeriod in calendarBusy.Busy)
            {
                if (busyPeriod.Start.HasValue && busyPeriod.End.HasValue)
                {
                    busyList.Add((busyPeriod.Start.Value, busyPeriod.End.Value));
                }
            }
        }

        return busyList;
    }

    public async Task<string> CreateEventAsync(Booking booking, CancellationToken cancellationToken = default)
    {
        var service = CreateCalendarService();

        var calendarEvent = new Event
        {
            Summary = $"[{booking.Package}] {booking.Customer.FirstName} {booking.Customer.LastName}",
            Description = $"Package: {booking.Package}\n" +
                          $"Participants: {booking.ParticipantsCount}\n" +
                          $"Phone: {booking.Customer.Phone}\n" +
                          $"Email: {booking.Customer.Email}",
            Start = new EventDateTime
            {
                DateTime = booking.StartTime.UtcDateTime,
                TimeZone = "UTC"
            },
            End = new EventDateTime
            {
                DateTime = booking.EndTime.UtcDateTime,
                TimeZone = "UTC"
            },
        };

        var request = service.Events.Insert(calendarEvent, _options.CalendarId);
        var createdEvent = await request.ExecuteAsync(cancellationToken);

        return createdEvent.Id;
    }
}