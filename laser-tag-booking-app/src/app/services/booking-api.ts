import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { TimeSlot, CreateBookingRequest, PackageType } from '../models/booking.models';

@Injectable({
  providedIn: 'root'
})
export class BookingApiService {
  private readonly httpClient = inject(HttpClient);
  private readonly apiUrl = 'https://localhost:7083/api/bookings';

  getAvailableSlots(date: string, packageType: PackageType): Observable<TimeSlot[]> {
    const params = new HttpParams()
      .set('date', date)
      .set('packageType', packageType.toString());

    return this.httpClient.get<TimeSlot[]>(`${this.apiUrl}/available-slots`, { params });
  }

  createBooking(request: CreateBookingRequest): Observable<{ bookingId: string }> {
    return this.httpClient.post<{ bookingId: string }>(this.apiUrl, request);
  }
}