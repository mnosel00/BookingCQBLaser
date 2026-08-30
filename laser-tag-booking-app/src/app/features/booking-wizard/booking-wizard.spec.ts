import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { Observable, of, throwError, NEVER } from 'rxjs';
import { BookingWizard } from './booking-wizard';
import { BookingApiService } from '../../services/booking-api';
import { PackageType, TimeSlot, PackageDuration, CreateBookingResponse } from '../../models/booking.models';

const DEFAULT_DURATIONS: PackageDuration[] = [
  { type: PackageType.S1, blockedDurationMinutes: 90 },
  { type: PackageType.S2, blockedDurationMinutes: 90 },
  { type: PackageType.Max, blockedDurationMinutes: 120 },
];

function futureWeekday(): string {
  const d = new Date();
  d.setDate(d.getDate() + 14); // safely past every lead-time rule regardless of when the suite runs
  while (d.getDay() === 0 || d.getDay() === 6) {
    d.setDate(d.getDate() + 1);
  }
  return d.toISOString().slice(0, 10);
}

function slot(startTime: string, maxAvailableDurationMinutes: number): TimeSlot {
  return { startTime, endTime: startTime, maxAvailableDurationMinutes };
}

const VALID_FORM_VALUE = {
  firstName: 'Jan', lastName: 'Kowalski', email: 'jan@example.com', phone: '123456789',
  participantsCount: 10, acceptTerms: true, acceptLegal: true, isAdultGroup: true, ageRange: ''
};

describe('BookingWizard', () => {
  let apiSpy: {
    getAvailableSlots: ReturnType<typeof vi.fn>;
    createBooking: ReturnType<typeof vi.fn>;
    getBookingStatus: ReturnType<typeof vi.fn>;
    getPackageDurations: ReturnType<typeof vi.fn>;
  };

  function createComponent(durationsSource: Observable<PackageDuration[]> = of(DEFAULT_DURATIONS)) {
    apiSpy = {
      getAvailableSlots: vi.fn(),
      createBooking: vi.fn(),
      getBookingStatus: vi.fn(),
      getPackageDurations: vi.fn().mockReturnValue(durationsSource),
    };

    TestBed.configureTestingModule({
      imports: [BookingWizard],
      providers: [{ provide: BookingApiService, useValue: apiSpy }],
    });

    return TestBed.createComponent(BookingWizard).componentInstance;
  }

  beforeEach(() => {
    sessionStorage.clear();
  });

  it('should create', () => {
    expect(createComponent()).toBeTruthy();
  });

  it('defaults participantsCount to the package minimum on selection (10 for S1/U1, 8 for others)', () => {
    const component = createComponent();

    component.selectPackage(PackageType.S1);
    expect(component.customerForm.get('participantsCount')?.value).toBe(10);

    component.selectPackage(PackageType.S2);
    expect(component.customerForm.get('participantsCount')?.value).toBe(8);

    component.selectPackage(PackageType.U1);
    expect(component.customerForm.get('participantsCount')?.value).toBe(10);
  });

  it('splits slots into compatible/incompatible using durations fetched from the API', () => {
    const component = createComponent();
    component.selectedPackage = PackageType.Max; // needs 120 min per the fetched durations

    apiSpy.getAvailableSlots.mockReturnValue(of([
      slot('2026-09-01T09:00:00+02:00', 90),  // too short for Max
      slot('2026-09-01T12:30:00+02:00', 120), // exactly enough
    ]));

    component.onDateChange(futureWeekday());

    expect(component.availableSlotsCompatible.length).toBe(1);
    expect(component.availableSlotsCompatible[0].maxAvailableDurationMinutes).toBe(120);
    expect(component.availableSlotsIncompatible.length).toBe(1);
    expect(component.availableSlotsIncompatible[0].maxAvailableDurationMinutes).toBe(90);
  });

  it('treats a slot as compatible when the fetched duration map has no entry for it (fail open)', () => {
    const component = createComponent(of([])); // durations resolve, but with no entries at all
    component.selectedPackage = PackageType.Max;

    apiSpy.getAvailableSlots.mockReturnValue(of([slot('2026-09-01T09:00:00+02:00', 5)]));
    component.onDateChange(futureWeekday());

    expect(component.availableSlotsCompatible.length).toBe(1);
    expect(component.availableSlotsIncompatible.length).toBe(0);
  });

  it('never crashes when the durations fetch itself never resolves', () => {
    const component = createComponent(NEVER);
    component.selectedPackage = PackageType.S1;

    apiSpy.getAvailableSlots.mockReturnValue(of([slot('2026-09-01T09:00:00+02:00', 5)]));

    expect(() => component.onDateChange(futureWeekday())).not.toThrow();
    expect(component.availableSlotsCompatible.length).toBe(1);
  });

  it('does not call the API when the form/terms/selection are incomplete', () => {
    const component = createComponent();
    component.hasOpenedTerms = false;

    component.submitBooking();

    expect(apiSpy.createBooking).not.toHaveBeenCalled();
  });

  describe('on successful booking creation', () => {
    it('stores the booking id and redirects to the payment URL', () => {
      const component = createComponent();
      component.hasOpenedTerms = true;
      component.selectedPackage = PackageType.S1;
      component.selectedSlot = slot('2026-09-01T09:00:00+02:00', 90);
      component.customerForm.setValue(VALID_FORM_VALUE);

      const response: CreateBookingResponse = { bookingId: 'abc-123', paymentUrl: 'https://pay.example/x' };
      apiSpy.createBooking.mockReturnValue(of(response));

      const originalLocation = window.location;
      // jsdom throws on real navigation; replace location with a plain settable stub.
      Object.defineProperty(window, 'location', { value: { href: '' }, writable: true });

      component.submitBooking();

      expect(sessionStorage.getItem('lastBookingId')).toBe('abc-123');
      expect(window.location.href).toBe('https://pay.example/x');

      Object.defineProperty(window, 'location', { value: originalLocation, writable: true });
    });
  });

  describe('when the slot was taken by someone else (409)', () => {
    it('clears the stale selection, returns to step 2, and reloads slots', () => {
      const component = createComponent();
      component.hasOpenedTerms = true;
      component.selectedPackage = PackageType.S1;
      component.selectedDate = futureWeekday();
      component.selectedSlot = slot('2026-09-01T09:00:00+02:00', 90);
      component.currentStep = 3;
      component.customerForm.setValue(VALID_FORM_VALUE);

      apiSpy.createBooking.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 409 })));
      apiSpy.getAvailableSlots.mockReturnValue(of([]));
      vi.spyOn(window, 'alert').mockImplementation(() => {});

      component.submitBooking();

      expect(component.selectedSlot).toBeNull();
      expect(component.currentStep).toBe(2);
      expect(apiSpy.getAvailableSlots).toHaveBeenCalled();
    });
  });

  describe('on a generic booking failure', () => {
    it('shows an alert but does not reset the wizard state', () => {
      const component = createComponent();
      component.hasOpenedTerms = true;
      component.selectedPackage = PackageType.S1;
      const originalSlot = slot('2026-09-01T09:00:00+02:00', 90);
      component.selectedSlot = originalSlot;
      component.currentStep = 3;
      component.customerForm.setValue(VALID_FORM_VALUE);

      apiSpy.createBooking.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 500 })));
      const alertSpy = vi.spyOn(window, 'alert').mockImplementation(() => {});

      component.submitBooking();

      expect(alertSpy).toHaveBeenCalled();
      expect(component.selectedSlot).toBe(originalSlot);
      expect(component.currentStep).toBe(3);
    });
  });
});
