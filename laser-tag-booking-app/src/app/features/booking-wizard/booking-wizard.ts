import { CommonModule, DatePipe } from '@angular/common';
import { Component, inject } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { BookingApiService } from '../../services/booking-api';
import { CreateBookingRequest, PackageType, TimeSlot } from '../../models/booking.models';

export interface PackageDetails {
  type: PackageType;
  name: string;
  price: string;
  duration: string;
  features: string[];
  minPersons?: number;
}

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

  readonly packagesList: PackageDetails[] = [
    {
      type: PackageType.S1,
      name: 'S1',
      price: '55 PLN',
      duration: '50 min',
      features: [
        'Przygotowanie do gry',
        '30 min gry (2 gry po 15 min)',
        '250 strzałów'
      ],
      minPersons: 10
    },
    {
      type: PackageType.S2,
      name: 'S2',
      price: '65 PLN',
      duration: '60 min',
      features: [
        'Przygotowanie do gry',
        '40 minut gry (2 gry po 20 min)',
        '250 strzałów'
      ]
    },
    {
      type: PackageType.Premium,
      name: 'Premium',
      price: '85 PLN',
      duration: '70 min',
      features: [
        'Przygotowanie do gry',
        '50 minut gry (5 gier po 10 min lub 2 gry po 25 min)',
        'No limit strzałów'
      ]
    },
    {
      type: PackageType.Max,
      name: 'Max',
      price: '95 PLN',
      duration: '80 min',
      features: [
        'Przygotowanie do gry',
        '60 minut gry (6 gier po 10 min lub 2 gry po 30 min)',
        'No limit strzałów'
      ]
    },
    {
      type: PackageType.U1,
      name: 'U1',
      price: '55 PLN',
      duration: '80 min',
      features: [
        'Przygotowanie do gry',
        '30 minut gry (2 gry po 15 min)',
        '250 strzałów',
        '30 minut w salce'
      ],
      minPersons: 10
    },
    {
      type: PackageType.U2,
      name: 'U2',
      price: '65 PLN',
      duration: '90 min',
      features: [
        'Przygotowanie do gry',
        '40 minut gry (2 gry po 20 min)',
        '250 strzałów',
        '30 minut w salce'
      ]
    },
    {
      type: PackageType.U3,
      name: 'U3',
      price: '85 PLN',
      duration: '100 min',
      features: [
        'Przygotowanie do gry',
        '50 minut gry (5 gier po 10 min lub 2 gry po 25 min)',
        'No limit strzałów',
        '30 minut w salce'
      ]
    },
    {
      type: PackageType.Combat,
      name: 'Combat',
      price: '95 PLN',
      duration: '110 min',
      features: [
        'Przygotowanie do gry',
        '60 minut gry (6 gier po 10 min lub 2 gry po 30 min)',
        'No limit strzałów',
        '30 minut w salce'
      ]
    }
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
