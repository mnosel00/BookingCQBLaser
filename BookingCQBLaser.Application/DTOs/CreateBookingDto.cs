using BookingCQBLaser.Domain.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BookingCQBLaser.Application.DTOs
{
    public record CreateBookingDto(
    string FirstName,
    string LastName,
    string Email,
    string Phone,
    int ParticipantsCount,
    PackageType Package,
    DateTimeOffset StartTime,
    bool IsAdultGroup,
    string? AgeRange);
}
