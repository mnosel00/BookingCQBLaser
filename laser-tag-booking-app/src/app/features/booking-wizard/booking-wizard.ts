import { CommonModule, DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, ElementRef, inject } from '@angular/core';
import {
  FormBuilder,
  FormControl,
  ReactiveFormsModule,
  Validators
} from '@angular/forms';
import { BookingApiService } from '../../services/booking-api';
import { CreateBookingRequest, PackageType, TimeSlot } from '../../models/booking.models';
import { BookingHero } from './booking-hero/booking-hero';
import { BookingSummaryBar, SummaryBarMode } from './booking-summary-bar/booking-summary-bar';

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
// Accepts plain "123456789" as well as the common "123 456 789" / "123-456-789" groupings;
// normalized down to 9 raw digits before it's sent to the API (see submitBooking).
const PHONE_PATTERN = /^\d{3}[\s-]?\d{3}[\s-]?\d{3}$/;

@Component({
  selector: 'app-booking-wizard',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, DatePipe, BookingHero, BookingSummaryBar],
  templateUrl: './booking-wizard.html',
  styleUrl: './booking-wizard.scss'
})
export class BookingWizard {
  private readonly bookingApiService = inject(BookingApiService);
  private readonly formBuilder = inject(FormBuilder);
  private readonly hostRef = inject<ElementRef<HTMLElement>>(ElementRef);

  // Spoken by a visually-hidden aria-live region when slot loading finishes, so screen-reader
  // users are told the result (sighted users just see the tiles appear). Empty while loading.
  slotsAnnouncement = '';

  currentStep = 1;
  selectedPackage: PackageType | null = null;
  selectedDate: string | Date = '';
  availableSlotsCompatible: TimeSlot[] = [];
  availableSlotsIncompatible: TimeSlot[] = [];
  selectedSlot: TimeSlot | null = null;
  selectedCategory: 'regular' | 'birthday' = 'regular';

  isLoadingSlots = false;
  slotsLoadError = false;
  isSubmitting = false;
  submitErrorMessage: string | null = null;
  successMessage = '';
  showTermsModal = false;
  hasOpenedTerms = false;
  showPhoneContactWarning = false;

  // Fetched from GET /api/packages - the backend (PackageTypeExtensions.GetBaseDurationMinutes)
  // is the single source of truth for blocked duration, not a hardcoded client-side copy.
  private packageBlockMinutes: Partial<Record<PackageType, number>> = {};

  readonly minDateValue: string = this.toLocalIsoDate(new Date());

  // Venue booking phone, in one place: shown in the step-2 call banner and the
  // over-26 group message. `venuePhoneTel` is the dialable tel: href form.
  readonly venuePhone = '+48 509 595 199';
  readonly venuePhoneTel = 'tel:+48509595199';

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
    // Nullable so it can start empty (showing the placeholder); the customer must
    // enter the count — there is no pre-filled default. `required` gates submission.
    participantsCount: new FormControl<number | null>(null, [Validators.required, Validators.min(8), Validators.max(26)]),
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

    this.bookingApiService.getPackageDurations().subscribe({
      next: (durations) => {
        this.packageBlockMinutes = Object.fromEntries(
          durations.map(d => [d.type, d.blockedDurationMinutes])
        );
      },
      error: () => {
        // Fail open: isSlotCompatible treats an unknown duration as compatible rather than
        // blocking every slot if this fetch fails.
      }
    });
  }

  get filteredPackages(): PackageDetails[] {
    return this.packagesList.filter(p => p.category === this.selectedCategory);
  }

  get minParticipants(): number {
    return this.selectedPackageDetails?.minPersons ?? 8;
  }

  get selectedPackageDetails(): PackageDetails | undefined {
    return this.packagesList.find(p => p.type === this.selectedPackage);
  }

  get compatibleSlotGroups(): { label: string; slots: TimeSlot[] }[] {
    const buckets: { label: string; from: number; to: number; slots: TimeSlot[] }[] = [
      { label: 'Rano', from: 0, to: 12, slots: [] },
      { label: 'Popołudnie', from: 12, to: 17, slots: [] },
      { label: 'Wieczór', from: 17, to: 24, slots: [] }
    ];

    for (const slot of this.availableSlotsCompatible) {
      const hour = new Date(slot.startTime).getHours();
      const bucket = buckets.find(b => hour >= b.from && hour < b.to) ?? buckets[buckets.length - 1];
      bucket.slots.push(slot);
    }

    return buckets.filter(b => b.slots.length > 0);
  }

  // compatibleSlotGroups rebuilds its group/slot wrapper objects on every change-detection pass,
  // so without trackBy Angular would destroy and recreate every slot button each cycle - which
  // drops a click when CD fires mid-click (e.g. the participant input blurring). Track by the
  // stable slot start time / group label so the DOM is reused.
  trackByGroupLabel(_index: number, group: { label: string }): string {
    return group.label;
  }

  trackBySlotStart(_index: number, slot: TimeSlot): string | Date {
    return slot.startTime;
  }

  get selectedDateValue(): string {
    return typeof this.selectedDate === 'string'
      ? this.selectedDate
      : this.selectedDate.toISOString().slice(0, 10);
  }

  get progressPercent(): number {
    return (this.currentStep / 3) * 100;
  }

  // Drives the sticky summary bar's visibility. Pure function of currentStep + whether
  // a Package is selected. The bar shows the same full content (Package + Total Price +
  // Deposit) on steps 2 and 3 so the footer is consistent across the flow.
  //   step 1 / no Package → hidden (uncluttered Package comparison)
  //   steps 2 & 3         → full
  get summaryBarMode(): SummaryBarMode {
    if (this.selectedPackage === null || this.currentStep === 1) return 'hidden';
    return 'full';
  }

  // How many days the branded date strip offers before the "Inna data" fallback.
  readonly dateStripDays = 21;

  // The next bookable days (local ISO yyyy-mm-dd) for the date strip: calendar days
  // from today forward that pass isOnlineBookingAllowed, capped at dateStripDays and
  // never scanning past the 6-month maxBookingDate guard. Computed once (a booking
  // session is short-lived; we don't recompute across midnight) rather than rebuilt
  // on every change-detection read from the template.
  readonly bookableDateStrip: string[] = this.buildBookableDateStrip();

  // Upper guard for the native "Inna data" fallback picker (~6 months out). There is
  // no max-advance business rule; this only stops fat-finger far-future dates.
  readonly maxBookingDate: string = this.toLocalIsoDate(this.sixMonthsFromToday());

  private buildBookableDateStrip(): string[] {
    const out: string[] = [];
    const cursor = new Date();
    cursor.setHours(0, 0, 0, 0);
    const hardStop = this.sixMonthsFromToday();
    while (out.length < this.dateStripDays && cursor.getTime() <= hardStop.getTime()) {
      if (this.isOnlineBookingAllowed(new Date(cursor))) {
        out.push(this.toLocalIsoDate(cursor));
      }
      cursor.setDate(cursor.getDate() + 1);
    }
    return out;
  }

  // True when the chosen date came from the native "Inna data" fallback (i.e. it is
  // not one of the quick-pick strip days), so that chip can show as active.
  get isCustomDateSelected(): boolean {
    return !!this.selectedDateValue && !this.bookableDateStrip.includes(this.selectedDateValue);
  }

  private readonly polishDowShort = ['Niedz', 'Pon', 'Wt', 'Śr', 'Czw', 'Pt', 'Sob'];

  // Localised day-of-week / day labels for a strip chip, parsed as a local date so the
  // weekday and day never drift by a timezone offset.
  dateChipDow(iso: string): string {
    return this.polishDowShort[new Date(iso + 'T00:00:00').getDay()];
  }

  dateChipDay(iso: string): string {
    const d = new Date(iso + 'T00:00:00');
    return `${String(d.getDate()).padStart(2, '0')}.${String(d.getMonth() + 1).padStart(2, '0')}`;
  }

  // On desktop, clicking a date input's field does not open the calendar (only the
  // small indicator does), so the invisible overlay chip appeared dead. showPicker()
  // opens it from the click gesture; on mobile tapping already opened it.
  openNativeDatePicker(event: Event): void {
    const input = event.target as HTMLInputElement & { showPicker?: () => void };
    try {
      input.showPicker?.();
    } catch {
      // showPicker can throw (unsupported, or not a user gesture); the native field
      // still works on platforms where tapping it opens the picker directly.
    }
  }

  private sixMonthsFromToday(): Date {
    const d = new Date();
    d.setHours(0, 0, 0, 0);
    d.setMonth(d.getMonth() + 6);
    return d;
  }

  // Local calendar date as yyyy-mm-dd, without the UTC shift toISOString would apply.
  private toLocalIsoDate(d: Date): string {
    const year = d.getFullYear();
    const month = String(d.getMonth() + 1).padStart(2, '0');
    const day = String(d.getDate()).padStart(2, '0');
    return `${year}-${month}-${day}`;
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
      this.focusStepHeading(this.currentStep);
    }
  }

  // Move keyboard/screen-reader focus to the heading of the step we just switched to. The
  // headings carry tabindex="-1" so they can receive focus programmatically without joining
  // the tab order. setTimeout defers until after Angular has swapped the *ngIf sections in,
  // so the target heading exists; a missing heading (e.g. in a non-rendered test) is a no-op.
  private focusStepHeading(step: number): void {
    setTimeout(() => {
      this.hostRef.nativeElement
        .querySelector<HTMLElement>(`#step-${step}-title`)
        ?.focus();
    });
  }

  // Polish plural of "termin" (slot): 1 → termin, 2–4 → terminy, else → terminów,
  // with the usual 12–14 exception that forces "terminów".
  private pluralTermin(n: number): string {
    const mod10 = n % 10;
    const mod100 = n % 100;
    if (n === 1) return 'termin';
    if (mod10 >= 2 && mod10 <= 4 && (mod100 < 12 || mod100 > 14)) return 'terminy';
    return 'terminów';
  }

  // Build the live-region message describing the just-loaded slots.
  private announceSlots(): void {
    const compatible = this.availableSlotsCompatible.length;
    const incompatible = this.availableSlotsIncompatible.length;

    if (compatible > 0) {
      this.slotsAnnouncement = `Załadowano ${compatible} ${this.pluralTermin(compatible)}.`;
    } else if (incompatible > 0) {
      this.slotsAnnouncement = 'Brak terminów dla wybranego pakietu. Dostępne terminy wymagają zmiany pakietu.';
    } else {
      this.slotsAnnouncement = 'Brak dostępnych terminów dla wybranej daty.';
    }
  }

 private isSlotCompatible(slot: TimeSlot): boolean {
    if (this.selectedPackage === null) return true;
    const requiredDuration = this.packageBlockMinutes[this.selectedPackage];
    if (requiredDuration === undefined) return true; // durations not loaded yet - fail open
    return slot.maxAvailableDurationMinutes >= requiredDuration;
  }

  private getIncompatibleSlotMessage(slot: TimeSlot): string {
    if (this.selectedPackage === null) return '';

    // Szukamy pakietów, które zmieszczą się w tym okienku
    const compatiblePackages = Object.entries(this.packageBlockMinutes)
      .filter(([_, duration]) => duration !== undefined && duration <= slot.maxAvailableDurationMinutes)
      .map(([pkg]) => {
        const pkgType = Number(pkg) as PackageType;
        return this.packagesList.find(p => p.type === pkgType)?.name || '';
      })
      .filter(name => name.length > 0)
      .join(', ');

    if (!compatiblePackages) {
      return 'Ten termin jest za krótki dla każdego z dostępnych pakietów.';
    }

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
    this.submitErrorMessage = null;
    this.showPhoneContactWarning = false;
    this.slotsLoadError = false;

    const minPersons = this.packagesList.find(p => p.type === pkg)?.minPersons ?? 8;
    const participantsCtrl = this.customerForm.get('participantsCount')!;
    participantsCtrl.setValidators([
      Validators.required,
      Validators.min(minPersons),
      Validators.max(26)
    ]);
    // Clear the field so the customer enters the count themselves (the placeholder
    // prompts them); don't pre-fill a number. Also drops any leftover value from a
    // previously selected package, whose minimum may differ.
    participantsCtrl.setValue(null);
    participantsCtrl.updateValueAndValidity();

    this.currentStep = 2;
    this.focusStepHeading(2);
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
    this.slotsLoadError = false;
    this.slotsAnnouncement = '';

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
        this.announceSlots();
      },
      error: () => { this.isLoadingSlots = false; this.slotsLoadError = true; }
    });
  }

  // A fresh idempotency key per booking attempt (i.e. per slot selection). It is reused across
  // retries of the same submission so a double-click or a retry after an uncertain response
  // resolves to one booking, and regenerated when a new slot is chosen (a new attempt).
  private currentIdempotencyKey: string | null = null;

  selectSlot(slot: TimeSlot): void {
    // The participant count lives on step 2; don't let a slot click jump to step 3 until it's
    // valid. Surface the error and move focus to the field so the user knows why.
    const participantsCtrl = this.customerForm.get('participantsCount')!;
    if (participantsCtrl.invalid) {
      participantsCtrl.markAsTouched();
      this.focusParticipantsField();
      return;
    }

    this.selectedSlot = slot;
    this.submitErrorMessage = null;
    this.currentIdempotencyKey = crypto.randomUUID();
    this.currentStep = 3;
    this.focusStepHeading(3);
  }

  private focusParticipantsField(): void {
    setTimeout(() => {
      this.hostRef.nativeElement.querySelector<HTMLElement>('#participantsCount')?.focus();
    });
  }

  submitBooking(): void {
    if (!this.hasOpenedTerms || this.customerForm.invalid || this.selectedPackage === null || this.selectedSlot === null) {
      this.customerForm.markAllAsTouched();
      return;
    }

    this.submitErrorMessage = null;
    const fv = this.customerForm.getRawValue();

    const request: CreateBookingRequest = {
      firstName: fv.firstName.trim(),
      lastName: fv.lastName.trim(),
      email: fv.email.trim(),
      phone: fv.phone.replace(/\D/g, ''),
      participantsCount: Number(fv.participantsCount),
      package: this.selectedPackage,
      startTime: this.selectedSlot.startTime,
      isAdultGroup: fv.isAdultGroup,
      ageRange: fv.isAdultGroup ? null : fv.ageRange.trim(),
      idempotencyKey: (this.currentIdempotencyKey ??= crypto.randomUUID())
    };

    this.isSubmitting = true;

    this.bookingApiService.createBooking(request).subscribe({
      next: (result) => {
        this.isSubmitting = false;
        sessionStorage.setItem('lastBookingId', result.bookingId);
        window.location.href = result.paymentUrl;
      },
      error: (err: HttpErrorResponse) => {
        this.isSubmitting = false;

        if (err.status === 409) {
          // Someone else booked this slot in the time it took to fill out the form.
          this.submitErrorMessage = 'Ten termin został właśnie zarezerwowany przez kogoś innego. Wybierz proszę inny termin.';
          this.selectedSlot = null;
          this.currentStep = 2;
          this.focusStepHeading(2);
          if (this.selectedDate && this.selectedPackage !== null) {
            this.onDateChange(this.selectedDateValue);
          }
          return;
        }

        this.submitErrorMessage = 'Rezerwacja nie powiodła się. Spróbuj ponownie.';
      }
    });
  }
}
