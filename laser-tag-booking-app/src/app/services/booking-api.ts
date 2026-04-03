import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { TimeSlot, CreateBookingRequest, PackageType } from '../models/booking.models';

@Injectable({
  providedIn: 'root'
})
export class BookingApiService {
  private readonly httpClient = inject(HttpClient);
  private readonly apiUrl = 'https://nontumultuous-rosamaria-discernably.ngrok-free.dev';

  getAvailableSlots(date: string, packageType: PackageType): Observable<TimeSlot[]> {
    const url = `${this.apiUrl}/available-slots?date=${encodeURIComponent(date)}&package=${packageType}`;
    return this.httpClient.get<TimeSlot[]>(url);
  }

  createBooking(request: CreateBookingRequest): Observable<{ bookingId: string }> {
    return this.httpClient.post<{ bookingId: string }>(this.apiUrl, request);
  }
}