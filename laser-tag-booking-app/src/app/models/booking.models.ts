export enum PackageType {
  S1 = 0,
  S2 = 1,
  Premium = 2,
  Max = 3,
  U1 = 4,
  U2 = 5,
  U3 = 6,
  Combat = 7
}

export interface TimeSlot {
  startTime: string;
  endTime: string;
  maxAvailableDurationMinutes: number;
}

export interface CreateBookingRequest {
  firstName: string;
  lastName: string;
  email: string;
  phone: string;
  participantsCount: number;
  package: PackageType;
  startTime: string;
  isAdultGroup: boolean; 
  ageRange: string | null;
}

export interface CreateBookingResponse {
  bookingId: string;
  paymentUrl: string;
}

// Must match BookingCQBLaser.Domain.Enums.PaymentStatus exactly - the backend serializes this
// enum as a string specifically for this endpoint (BookingStatusDto).
export interface BookingStatus {
  bookingId: string;
  paymentStatus: 'Pending' | 'Paid' | 'Failed';
}

// Fetched from GET /api/packages - single source of truth is
// BookingCQBLaser.Domain.Enums.PackageTypeExtensions.GetBaseDurationMinutes(); do not
// hardcode a duplicate copy of this table client-side.
export interface PackageDuration {
  type: PackageType;
  blockedDurationMinutes: number;
}
