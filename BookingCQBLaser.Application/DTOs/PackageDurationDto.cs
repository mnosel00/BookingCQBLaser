using BookingCQBLaser.Domain.Enums;

namespace BookingCQBLaser.Application.DTOs
{
    // Type is left as the default numeric PackageType encoding (not string) to match the
    // existing wire contract every other endpoint already uses for this enum
    // (CreateBookingRequest.package, TimeSlotDto, etc.) — only PaymentStatus was flagged for the
    // string-enum fix, so PackageType's format is deliberately left untouched here.
    public record PackageDurationDto(PackageType Type, int BlockedDurationMinutes);
}
