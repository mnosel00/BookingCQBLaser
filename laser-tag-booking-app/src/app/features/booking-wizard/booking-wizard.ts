import { CommonModule, DatePipe } from '@angular/common';
import { Component, inject } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { BookingApiService } from '../../services/booking-api';
import { CreateBookingRequest, PackageType, TimeSlot } from '../../models/booking.models';

@Component({
  selector: 'app-booking-wizard',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, DatePipe],
  templateUrl: './booking-wizard.html',
  styleUrl: './booking-wizard.scss'
})
export class BookingWizard {
  private readonly bookingApiService = inject(BookingApiService);
  private readonly formBuilder = inject(FormBuilder);

  currentStep = 1;
  selectedPackage: PackageType | null = null;
  selectedDate: string | Date = '';
  availableSlots: TimeSlot[] = [];
  selectedSlot: TimeSlot | null = null;

  isLoadingSlots = false;
  isSubmitting = false;
  successMessage = '';

  readonly packageOptions: Array<{ label: string; value: PackageType }> = [
    { label: 'S1', value: PackageType.S1 },
    { label: 'S2', value: PackageType.S2 },
    { label: 'Premium', value: PackageType.Premium },
    { label: 'Max', value: PackageType.Max },
    { label: 'U1', value: PackageType.U1 },
    { label: 'U2', value: PackageType.U2 },
    { label: 'U3', value: PackageType.U3 },
    { label: 'Combat', value: PackageType.Combat }
  ];

  readonly customerForm = this.formBuilder.nonNullable.group({
    firstName: ['', [Validators.required]],
    lastName: ['', [Validators.required]],
    email: ['', [Validators.required, Validators.email]],
    phone: ['', [Validators.required]],
    participantsCount: [1, [Validators.required, Validators.min(1)]]
  });

  get selectedDateValue(): string {
    return typeof this.selectedDate === 'string'
      ? this.selectedDate
      : this.selectedDate.toISOString().slice(0, 10);
  }

  selectPackage(pkg: PackageType): void {
    this.selectedPackage = pkg;
    this.selectedDate = '';
    this.availableSlots = [];
    this.selectedSlot = null;
    this.successMessage = '';
    this.currentStep = 2;
  }

  onDateChange(date: string): void {
    this.selectedDate = date;
    this.availableSlots = [];
    this.selectedSlot = null;

    if (!date || this.selectedPackage === null) {
      return;
    }

    this.isLoadingSlots = true;
    this.bookingApiService.getAvailableSlots(date, this.selectedPackage).subscribe({
      next: (slots) => {
        this.availableSlots = slots;
        this.isLoadingSlots = false;
      },
      error: () => {
        this.isLoadingSlots = false;
        window.alert('Could not load available time slots.');
      }
    });
  }

  selectSlot(slot: TimeSlot): void {
    this.selectedSlot = slot;
    this.currentStep = 3;
  }

  submitBooking(): void {
    if (this.customerForm.invalid || this.selectedPackage === null || this.selectedSlot === null) {
      this.customerForm.markAllAsTouched();
      window.alert('Please complete all required fields.');
      return;
    }

    const formValue = this.customerForm.getRawValue();

    const request: CreateBookingRequest = {
      firstName: formValue.firstName.trim(),
      lastName: formValue.lastName.trim(),
      email: formValue.email.trim(),
      phone: formValue.phone.trim(),
      participantsCount: Number(formValue.participantsCount),
      package: this.selectedPackage,
      startTime: this.selectedSlot.startTime
    };

    this.isSubmitting = true;

    this.bookingApiService.createBooking(request).subscribe({
      next: (result) => {
        this.isSubmitting = false;
        this.successMessage = `Booking confirmed. ID: ${result.bookingId}`;
        this.currentStep = 1;
        this.selectedPackage = null;
        this.selectedDate = '';
        this.availableSlots = [];
        this.selectedSlot = null;
        this.customerForm.reset({
          firstName: '',
          lastName: '',
          email: '',
          phone: '',
          participantsCount: 1
        });
      },
      error: () => {
        this.isSubmitting = false;
        window.alert('Booking failed. Please try again.');
      }
    });
  }
}
