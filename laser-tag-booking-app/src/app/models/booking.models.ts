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
}

export interface CreateBookingRequest {
  firstName: string;
  lastName: string;
  email: string;
  phone: string;
  participantsCount: number;
  package: PackageType;
  startTime: string;
}

export interface CreateBookingResponse {
  bookingId: string;
  paymentUrl: string;
}