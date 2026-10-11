using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BookingCQBLaser.Application.DTOs;
using BookingCQBLaser.Application.Services;
using BookingCQBLaser.Domain.Entities;
using BookingCQBLaser.Domain.Enums;
using BookingCQBLaser.Domain.Exceptions;
using BookingCQBLaser.Domain.Interfaces;
using BookingCQBLaser.Domain.ValueObject;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace BookingCQBLaser.Tests.Application;

/// <summary>
/// Idempotency of CreateBooking: a retried or double-clicked submission carrying the same key
/// must resolve to the single booking already created, never a second one.
/// </summary>
public class BookingServiceTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 1, 10, 0, 0, TimeSpan.Zero);

    private readonly Mock<IBookingRepository> _repository = new();
    private readonly Mock<IAvailabilityCalculator> _availability = new();
    private readonly Mock<IGoogleCalendarService> _calendar = new();
    private readonly Mock<IEmailService> _email = new();
    private readonly Mock<IHotPayService> _hotpay = new();

    private BookingService CreateService() => new(
        _repository.Object, _availability.Object, _calendar.Object, _email.Object,
        _hotpay.Object, NullLogger<BookingService>.Instance);

    private static CreateBookingDto Dto(Guid? key) => new(
        "Jan", "Kowalski", "jan@example.com", "123456789",
        ParticipantsCount: 10, Package: PackageType.S2, StartTime: Start,
        IsAdultGroup: true, AgeRange: null, IdempotencyKey: key);

    private static Booking BookingWithKey(Guid key) => new(
        new CustomerInfo("Jan", "Kowalski", "jan@example.com", "123456789"),
        10, PackageType.S2, Start, 90, true, null, key);

    [Fact]
    public async Task CreateBookingAsync_with_a_known_idempotency_key_returns_the_existing_booking_without_creating_another()
    {
        var key = Guid.NewGuid();
        var existing = BookingWithKey(key);
        _repository.Setup(r => r.GetByIdempotencyKeyAsync(key, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        _hotpay.Setup(h => h.GeneratePaymentUrl(existing, It.IsAny<int>())).Returns("https://pay/existing");

        var result = await CreateService().CreateBookingAsync(Dto(key));

        Assert.Equal(existing.Id, result.BookingId);
        Assert.Equal("https://pay/existing", result.PaymentUrl);
        // No second booking, and we didn't even re-run availability.
        _repository.Verify(r => r.AddAsync(It.IsAny<Booking>(), It.IsAny<CancellationToken>()), Times.Never);
        _availability.Verify(
            a => a.GetAvailableTimeSlotsAsync(It.IsAny<DateTimeOffset>(), It.IsAny<PackageType>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CreateBookingAsync_resolves_a_concurrent_duplicate_submission_to_the_winning_booking()
    {
        var key = Guid.NewGuid();
        var winner = BookingWithKey(key);

        // First lookup finds nothing (we proceed to create); after the insert loses the race to
        // the unique index, the second lookup returns the booking that won.
        _repository.SetupSequence(r => r.GetByIdempotencyKeyAsync(key, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Booking?)null)
            .ReturnsAsync(winner);
        _availability.Setup(a => a.IsOnlineBookingAllowed(Start)).Returns(true);
        _availability.Setup(a => a.GetAvailableTimeSlotsAsync(Start, PackageType.S2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TimeSlotDto> { new(Start, Start.AddMinutes(90), 120) });
        _repository.Setup(r => r.AddAsync(It.IsAny<Booking>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DuplicateBookingSubmissionException(key));
        _hotpay.Setup(h => h.GeneratePaymentUrl(winner, It.IsAny<int>())).Returns("https://pay/winner");

        var result = await CreateService().CreateBookingAsync(Dto(key));

        Assert.Equal(winner.Id, result.BookingId);
        Assert.Equal("https://pay/winner", result.PaymentUrl);
    }
}
