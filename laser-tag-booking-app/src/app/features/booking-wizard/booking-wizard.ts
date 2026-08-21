import { CommonModule, DatePipe } from '@angular/common';
import { Component, inject } from '@angular/core';
import {
  FormBuilder,
  ReactiveFormsModule,
  Validators
} from '@angular/forms';
import { BookingApiService } from '../../services/booking-api';
import { CreateBookingRequest, PackageType, TimeSlot } from '../../models/booking.models';

export interface PackageDetails {
  type: PackageType;
  name: string;
  price: number;
  duration: string;
  features: string[];
  category: 'regular' | 'birthday';
  minPersons?: number;
}

const NAME_PATTERN = /^[a-zA-ZąćęłńóśźżĄĆĘŁŃÓŚŹŻ\s\-]+$/;
const PHONE_PATTERN = /^\d{9}$/;

// Mapping of PackageType to actual game duration in minutes (excluding prep time)
const PACKAGE_BLOCK_MINUTES: Record<PackageType, number> = {
  [PackageType.S1]: 90,
  [PackageType.S2]: 90,
  [PackageType.Premium]: 120,
  [PackageType.Max]: 120,
  [PackageType.U1]: 90,
  [PackageType.U2]: 120,
  [PackageType.U3]: 120,
  [PackageType.Combat]: 120
};

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
  availableSlotsCompatible: TimeSlot[] = [];
  availableSlotsIncompatible: TimeSlot[] = [];
  selectedSlot: TimeSlot | null = null;
  selectedCategory: 'regular' | 'birthday' = 'regular';

  isLoadingSlots = false;
  isSubmitting = false;
  successMessage = '';
  showTermsModal = false;
  hasOpenedTerms = false;
  showPhoneContactWarning = false;

  readonly minDateValue: string = new Date().toISOString().slice(0, 10);

  readonly packagesList: PackageDetails[] = [
    {
      type: PackageType.S1,
      name: 'S1',
      price: 55,
      duration: '50 min',
      category: 'regular',
      features: ['Przygotowanie do gry', '30 min gry (2 gry po 15 min)', '250 strzałów'],
      minPersons: 10
    },
    {
      type: PackageType.S2,
      name: 'S2',
      price: 65,
      duration: '60 min',
      category: 'regular',
      features: ['Przygotowanie do gry', '40 minut gry (2 gry po 20 min)', '250 strzałów']
    },
    {
      type: PackageType.Premium,
      name: 'Premium',
      price: 85,
      duration: '70 min',
      category: 'regular',
      features: ['Przygotowanie do gry', '50 minut gry (5 gier po 10 min lub 2 gry po 25 min)', 'No limit strzałów']
    },
    {
      type: PackageType.Max,
      name: 'Max',
      price: 95,
      duration: '80 min',
      category: 'regular',
      features: ['Przygotowanie do gry', '60 minut gry (6 gier po 10 min lub 2 gry po 30 min)', 'No limit strzałów']
    },
    {
      type: PackageType.U1,
      name: 'U1',
      price: 55,
      duration: '80 min',
      category: 'birthday',
      features: ['Przygotowanie do gry', '30 minut gry (2 gry po 15 min)', '250 strzałów', '30 minut w salce'],
      minPersons: 10
    },
    {
      type: PackageType.U2,
      name: 'U2',
      price: 65,
      duration: '90 min',
      category: 'birthday',
      features: ['Przygotowanie do gry', '40 minut gry (2 gry po 20 min)', '250 strzałów', '30 minut w salce']
    },
    {
      type: PackageType.U3,
      name: 'U3',
      price: 85,
      duration: '100 min',
      category: 'birthday',
      features: ['Przygotowanie do gry', '50 minut gry (5 gier po 10 min lub 2 gry po 25 min)', 'No limit strzałów', '30 minut w salce']
    },
    {
      type: PackageType.Combat,
      name: 'Combat',
      price: 95,
      duration: '110 min',
      category: 'birthday',
      features: ['Przygotowanie do gry', '60 minut gry (6 gier po 10 min lub 2 gry po 30 min)', 'No limit strzałów', '30 minut w salce']
    }
  ];

  readonly customerForm = this.formBuilder.nonNullable.group({
    firstName: ['', [Validators.required, Validators.pattern(NAME_PATTERN)]],
    lastName: ['', [Validators.required, Validators.pattern(NAME_PATTERN)]],
    email: ['', [Validators.required, Validators.email]],
    phone: ['', [Validators.required, Validators.pattern(PHONE_PATTERN)]],
    participantsCount: [1, [Validators.required, Validators.min(8), Validators.max(26)]],
    acceptTerms: [false, Validators.requiredTrue],
    acceptLegal: [false, Validators.requiredTrue],
    isAdultGroup: [false], 
    ageRange: ['', Validators.required]
  });

  constructor() {
    this.customerForm.get('acceptTerms')?.disable();

    this.customerForm.get('isAdultGroup')?.valueChanges.subscribe((isAdult: boolean) => {
    const ageRangeControl = this.customerForm.get('ageRange');
    if (!ageRangeControl) return;

    if (isAdult) {
      ageRangeControl.setValue('');
      ageRangeControl.disable();
      ageRangeControl.clearValidators();
    } else {
      ageRangeControl.enable();
      ageRangeControl.setValidators([Validators.required]);
    }
    ageRangeControl.updateValueAndValidity();
    });
  }

  get filteredPackages(): PackageDetails[] {
    return this.packagesList.filter(p => p.category === this.selectedCategory);
  }

  get selectedPackageDetails(): PackageDetails | undefined {
    return this.packagesList.find(p => p.type === this.selectedPackage);
  }

  get selectedDateValue(): string {
    return typeof this.selectedDate === 'string'
      ? this.selectedDate
      : this.selectedDate.toISOString().slice(0, 10);
  }

  get progressPercent(): number {
    return (this.currentStep / 3) * 100;
  }

  get numericPrice(): number {
    return this.selectedPackageDetails?.price ?? 0;
  }

  get totalCost(): number {
    const participants = Number(this.customerForm.get('participantsCount')?.value ?? 0);
    if (!Number.isFinite(participants) || participants <= 0) {
      return 0;
    }
    return this.numericPrice * participants;
  }

  // CHANGED: fixed billing amounts
  get depositAmount(): number {
    return 300;
  }

  // CHANGED: fixed service fee
  get serviceFee(): number {
    return 4;
  }

  // CHANGED: amount paid online now (always 304 PLN)
  get totalToPayOnline(): number {
    return this.depositAmount + this.serviceFee;
  }

  // CHANGED: service fee does not reduce on-site balance
  get remainingBalance(): number {
    return Math.max(0, this.totalCost - this.depositAmount);
  }

  f(name: string) {
    return this.customerForm.get(name);
  }

  getIncompatibleSlotDisplay(slot: TimeSlot): string {
    return this.getIncompatibleSlotMessage(slot);
  }

  getActualEndTime(): string | null {
    if (!this.selectedSlot) return null;
    return this.selectedSlot.endTime; 
  }

  setCategory(cat: 'regular' | 'birthday'): void {
    this.selectedCategory = cat;
  }

  previousStep(): void {
    if (this.currentStep > 1) {
      this.currentStep--;
    }
  }

 private isSlotCompatible(slot: TimeSlot): boolean {
    if (this.selectedPackage === null) return true;
    const requiredDuration = PACKAGE_BLOCK_MINUTES[this.selectedPackage];
    // Zmieniono na maxAvailableDurationMinutes
    return slot.maxAvailableDurationMinutes >= requiredDuration;
  }

  private getIncompatibleSlotMessage(slot: TimeSlot): string {
    if (this.selectedPackage === null) return '';
    
    // Szukamy pakietów, które zmieszczą się w tym okienku
    const compatiblePackages = Object.entries(PACKAGE_BLOCK_MINUTES)
      .filter(([_, duration]) => duration <= slot.maxAvailableDurationMinutes)
      .map(([pkg]) => {
        const pkgType = Number(pkg) as PackageType;
        return this.packagesList.find(p => p.type === pkgType)?.name || '';
      })
      .filter(name => name.length > 0)
      .join(', ');
    
    return `Ten termin jest nie dostępny dla wybranego pakietu. Zmień pakiet na ${compatiblePackages}, aby go wybrać.`;
  }

  private calculateActualEndTime(startTime: Date, durationMinutes: number): Date {
    const endTime = new Date(startTime);
    endTime.setMinutes(endTime.getMinutes() + durationMinutes);
    return endTime;
  }

  openTerms(): void {
    this.showTermsModal = true;
    this.hasOpenedTerms = true;
    this.customerForm.get('acceptTerms')?.enable();
  }

  closeTerms(): void {
    this.showTermsModal = false;
  }

  selectPackage(pkg: PackageType): void {
    this.selectedPackage = pkg;
    this.selectedDate = '';
    this.availableSlotsCompatible = [];
    this.availableSlotsIncompatible = [];
    this.selectedSlot = null;
    this.successMessage = '';
    this.showPhoneContactWarning = false;

    const minPersons = this.packagesList.find(p => p.type === pkg)?.minPersons ?? 8;
    const participantsCtrl = this.customerForm.get('participantsCount')!;
    participantsCtrl.setValidators([
      Validators.required,
      Validators.min(minPersons),
      Validators.max(26)
    ]);
    participantsCtrl.updateValueAndValidity();

    this.currentStep = 2;
  }

  isOnlineBookingAllowed(selectedDate: Date): boolean {
    const now = new Date(); // Browser's local time
    const target = new Date(selectedDate);
    target.setHours(0, 0, 0, 0);

    const dayOfWeek = target.getDay(); // 0 is Sunday, 6 is Saturday

    if (dayOfWeek === 6) { // Saturday
        const deadline = new Date(target);
        deadline.setDate(deadline.getDate() - 1); // Friday
        deadline.setHours(22, 0, 0, 0);
        return now.getTime() <= deadline.getTime();
    }

    if (dayOfWeek === 0) { // Sunday
        const deadline = new Date(target);
        deadline.setDate(deadline.getDate() - 1); // Saturday
        deadline.setHours(22, 0, 0, 0);
        return now.getTime() <= deadline.getTime();
    }

    // Monday-Friday (3 days buffer)
    const today = new Date();
    today.setHours(0, 0, 0, 0);
    const diffTime = target.getTime() - today.getTime();
    const diffDays = Math.ceil(diffTime / (1000 * 60 * 60 * 24));
    return diffDays >= 3;
  }

  onDateChange(date: string): void {
    this.selectedDate = date;
    this.availableSlotsCompatible = [];
    this.availableSlotsIncompatible = [];
    this.selectedSlot = null;
    this.showPhoneContactWarning = false;

    if (!date || this.selectedPackage === null) return;

    if (!this.isOnlineBookingAllowed(new Date(date))) {
      this.showPhoneContactWarning = true;
      return;
    }

    this.isLoadingSlots = true;
    this.bookingApiService.getAvailableSlots(date, this.selectedPackage).subscribe({
      next: (slots) => { 
        this.availableSlotsCompatible = slots.filter(s => this.isSlotCompatible(s));
        this.availableSlotsIncompatible = slots.filter(s => !this.isSlotCompatible(s));
        this.isLoadingSlots = false; 
      },
      error: () => { this.isLoadingSlots = false; window.alert('Could not load available time slots.'); }
    });
  }

  selectSlot(slot: TimeSlot): void {
    if (!this.isSlotCompatible(slot)) {
      window.alert(this.getIncompatibleSlotMessage(slot));
      return;
    }
    this.selectedSlot = slot;
    this.currentStep = 3;
  }

  submitBooking(): void {
    if (!this.hasOpenedTerms || this.customerForm.invalid || this.selectedPackage === null || this.selectedSlot === null) {
      this.customerForm.markAllAsTouched();
      return;
    }

    const fv = this.customerForm.getRawValue();

    const request: CreateBookingRequest = {
      firstName: fv.firstName.trim(),
      lastName: fv.lastName.trim(),
      email: fv.email.trim(),
      phone: fv.phone.trim(),
      participantsCount: Number(fv.participantsCount),
      package: this.selectedPackage,
      startTime: this.selectedSlot.startTime,
      isAdultGroup: fv.isAdultGroup, 
      ageRange: fv.isAdultGroup ? null : fv.ageRange.trim()
    };

    this.isSubmitting = true;

    this.bookingApiService.createBooking(request).subscribe({
      next: (result) => {
        this.isSubmitting = false;
        sessionStorage.setItem('lastBookingId', result.bookingId);
        window.location.href = result.paymentUrl;
      },
      error: () => { 
        this.isSubmitting = false; 
        window.alert('Booking failed. Please try again.'); 
      }
    });
  }
}
