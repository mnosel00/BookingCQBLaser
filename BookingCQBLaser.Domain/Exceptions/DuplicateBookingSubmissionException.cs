using System;

namespace BookingCQBLaser.Domain.Exceptions
{
    /// <summary>
    /// Raised when a booking insert is rejected because another booking already uses the same
    /// idempotency key — i.e. the same submission arrived twice (a double-click or a retry that
    /// raced the original). The caller resolves this by returning the already-created booking,
    /// so the client sees one booking rather than an error.
    /// </summary>
    public class DuplicateBookingSubmissionException : Exception
    {
        public Guid IdempotencyKey { get; }

        public DuplicateBookingSubmissionException(Guid idempotencyKey, Exception? innerException = null)
            : base($"A booking with idempotency key {idempotencyKey} already exists.", innerException)
        {
            IdempotencyKey = idempotencyKey;
        }
    }
}
