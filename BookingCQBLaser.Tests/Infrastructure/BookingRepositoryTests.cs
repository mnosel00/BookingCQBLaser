using System;
using System.Threading;
using System.Threading.Tasks;
using BookingCQBLaser.Domain.Entities;
using BookingCQBLaser.Domain.Enums;
using BookingCQBLaser.Domain.Exceptions;
using BookingCQBLaser.Domain.ValueObject;
using BookingCQBLaser.Infrastructure.Persistence.Configurations;
using BookingCQBLaser.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BookingCQBLaser.Tests.Infrastructure;

/// <summary>
/// The Postgres exclusion constraint (EX_Bookings_NoOverlap) is the final line of defense
/// against the booking race: two concurrent requests can both pass the application-level
/// availability check, but only one insert survives. The repository must translate that
/// database-level rejection into an <see cref="InvalidOperationException"/>, which the API
/// maps to 409 Conflict (so the client's "slot just taken" recovery flow fires) rather than
/// leaking a raw 500.
/// </summary>
public class BookingRepositoryTests
{
    // AddAsync only stages the entity and then calls SaveChangesAsync; overriding SaveChangesAsync
    // lets us simulate the database raising (or not raising) the exclusion violation without a
    // real PostgreSQL connection. UseNpgsql is only needed so the model builds; nothing connects.
    private sealed class ThrowingDbContext : AppDbContext
    {
        private readonly Exception _toThrow;

        public ThrowingDbContext(DbContextOptions<AppDbContext> options, Exception toThrow)
            : base(options) => _toThrow = toThrow;

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
            => throw _toThrow;
    }

    private static DbContextOptions<AppDbContext> BuildOptions() =>
        new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=unit_test;Username=u;Password=p")
            .Options;

    private static Booking ValidBooking(Guid? idempotencyKey = null) =>
        new(
            new CustomerInfo("Jan", "Kowalski", "jan@example.com", "123456789"),
            participantsCount: 10,
            package: PackageType.S2,
            startTime: new DateTimeOffset(2026, 9, 1, 10, 0, 0, TimeSpan.Zero),
            totalBlockedDurationMinutes: 90,
            isAdultGroup: true,
            ageRange: null,
            idempotencyKey: idempotencyKey);

    private static DbUpdateException DbUpdateWith(string sqlState, string? constraintName = null) =>
        new(
            "update failed",
            new PostgresException("conflict", "ERROR", "ERROR", sqlState, constraintName: constraintName));

    [Fact]
    public async Task AddAsync_translates_a_Postgres_exclusion_violation_into_InvalidOperationException()
    {
        var overlap = DbUpdateWith(PostgresErrorCodes.ExclusionViolation); // 23P01
        await using var context = new ThrowingDbContext(BuildOptions(), overlap);
        var repository = new BookingRepository(context);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => repository.AddAsync(ValidBooking()));

        // The original DbUpdateException is preserved as the inner exception for diagnostics.
        Assert.Same(overlap, ex.InnerException);
    }

    [Fact]
    public async Task AddAsync_translates_an_idempotency_unique_violation_into_DuplicateBookingSubmissionException()
    {
        var key = Guid.NewGuid();
        // A unique violation on the idempotency index = the same submission arrived twice.
        var duplicate = DbUpdateWith(PostgresErrorCodes.UniqueViolation, BookingConfiguration.IdempotencyKeyIndexName);
        await using var context = new ThrowingDbContext(BuildOptions(), duplicate);
        var repository = new BookingRepository(context);

        var ex = await Assert.ThrowsAsync<DuplicateBookingSubmissionException>(
            () => repository.AddAsync(ValidBooking(key)));

        Assert.Equal(key, ex.IdempotencyKey);
        Assert.Same(duplicate, ex.InnerException);
    }

    [Fact]
    public async Task AddAsync_does_not_translate_an_unrelated_unique_violation()
    {
        // A unique violation on some other constraint must not be disguised as a slot conflict or
        // a duplicate submission; it propagates unchanged.
        var other = DbUpdateWith(PostgresErrorCodes.UniqueViolation, "IX_Bookings_SomethingElse");
        await using var context = new ThrowingDbContext(BuildOptions(), other);
        var repository = new BookingRepository(context);

        var ex = await Assert.ThrowsAsync<DbUpdateException>(
            () => repository.AddAsync(ValidBooking(Guid.NewGuid())));

        Assert.Same(other, ex);
    }
}
