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
        Environment.SetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS", _options.ServiceAccountJson);
        var credential = GoogleCredential.GetApplicationDefault()
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
            TimeMinDateTimeOffset = startDate,
            TimeMaxDateTimeOffset = endDate,
            Items = new List<FreeBusyRequestItem> { new FreeBusyRequestItem { Id = _options.CalendarId } }
        };

        var query = service.Freebusy.Query(request);
        var response = await query.ExecuteAsync(cancellationToken);

        var busyList = new List<(DateTimeOffset Start, DateTimeOffset End)>();

        if (response.Calendars.TryGetValue(_options.CalendarId, out var calendarBusy) && calendarBusy.Busy != null)
        {
            foreach (var busyPeriod in calendarBusy.Busy)
            {
                if (busyPeriod.StartDateTimeOffset.HasValue && busyPeriod.EndDateTimeOffset.HasValue)
                {
                    busyList.Add((busyPeriod.StartDateTimeOffset.Value, busyPeriod.EndDateTimeOffset.Value));
                }
            }
        }

        return busyList;
    }

    public async Task<string> CreateEventAsync(Booking booking, CancellationToken cancellationToken = default)
    {
        var service = CreateCalendarService();

        var ageGroupInfo = booking.IsAdultGroup ? "+18 (Dorośli)" : booking.AgeRange;

        var calendarEvent = new Event
        {
            Summary = $"[{booking.Package}] {booking.Customer.FirstName} {booking.Customer.LastName}",
            Description = $"Package: {booking.Package}\n" +
                          $"Participants: {booking.ParticipantsCount}\n" +
                          $"Phone: {booking.Customer.Phone}\n" +
                          $"Age Group: {ageGroupInfo}\n" + 
                          $"Email: {booking.Customer.Email}",
            Start = new EventDateTime
            {
                DateTimeDateTimeOffset = booking.StartTime
            },
            End = new EventDateTime
            {
                DateTimeDateTimeOffset = booking.EndTime
            },
        };

        var request = service.Events.Insert(calendarEvent, _options.CalendarId);
        var createdEvent = await request.ExecuteAsync(cancellationToken);

        return createdEvent.Id;
    }
}