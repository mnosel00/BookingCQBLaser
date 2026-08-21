using System;
using System.Text.Json.Serialization;
using BookingCQBLaser.Domain.Enums;

namespace BookingCQBLaser.Application.DTOs
{
    public record BookingStatusDto(
        Guid BookingId,
        [property: JsonConverter(typeof(JsonStringEnumConverter))] PaymentStatus PaymentStatus);
}
