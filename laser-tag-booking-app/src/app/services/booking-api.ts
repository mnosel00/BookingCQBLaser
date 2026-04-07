import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpHeaders } from '@angular/common/http';
import { Observable } from 'rxjs';
import { TimeSlot, CreateBookingRequest, PackageType } from '../models/booking.models';

@Injectable({
  providedIn: 'root'
})
export class BookingApiService {
  private readonly httpClient = inject(HttpClient);
  //private readonly apiUrl = 'https://localhost:7083/api/bookings';
  private readonly apiUrl = 'https://nontumultuous-rosamaria-discernably.ngrok-free.dev/api/bookings';

  // getAvailableSlots(date: string, packageType: PackageType): Observable<TimeSlot[]> {
  //   const url = `${this.apiUrl}/available-slots?date=${encodeURIComponent(date)}&package=${packageType}`;
  //   return this.httpClient.get<TimeSlot[]>(url);
  // }

  getAvailableSlots(date: string, packageType: PackageType): Observable<TimeSlot[]> {
  const headers = new HttpHeaders().set('ngrok-skip-browser-warning', 'true');
  const url = `${this.apiUrl}/available-slots?date=${encodeURIComponent(date)}&package=${packageType}`;
  
  return this.httpClient.get<TimeSlot[]>(url, { headers });
}

  createBooking(request: CreateBookingRequest): Observable<{ bookingId: string }> {
    return this.httpClient.post<{ bookingId: string }>(this.apiUrl, request);
  }
}