import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpHeaders } from '@angular/common/http';
import { Observable } from 'rxjs';
import { TimeSlot, CreateBookingRequest, CreateBookingResponse, BookingStatus, PackageDuration, PackageType } from '../models/booking.models';

@Injectable({
  providedIn: 'root'
})
export class BookingApiService {
  private readonly httpClient = inject(HttpClient);
  private readonly apiRoot = 'https://api.comboarena.pl/api';
  private readonly apiUrl = `${this.apiRoot}/bookings`;

  getAvailableSlots(date: string, packageType: PackageType): Observable<TimeSlot[]> {
    const url = `${this.apiUrl}/available-slots?date=${encodeURIComponent(date)}&package=${packageType}`;
    return this.httpClient.get<TimeSlot[]>(url);
  }

  createBooking(request: CreateBookingRequest): Observable<CreateBookingResponse> {
    return this.httpClient.post<CreateBookingResponse>(this.apiUrl, request);
  }

  getBookingStatus(bookingId: string): Observable<BookingStatus> {
    return this.httpClient.get<BookingStatus>(`${this.apiUrl}/${bookingId}/status`);
  }

  getPackageDurations(): Observable<PackageDuration[]> {
    return this.httpClient.get<PackageDuration[]>(`${this.apiRoot}/packages`);
  }
}
