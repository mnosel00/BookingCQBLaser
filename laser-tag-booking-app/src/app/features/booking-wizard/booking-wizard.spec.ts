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

// N slots long enough to be compatible with PackageType.Max (needs 120 min per DEFAULT_DURATIONS).
function compatibleSlots(n: number): TimeSlot[] {
  return Array.from({ length: n }, () => slot('2026-09-01T09:00:00+02:00', 120));
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

  it('clears participantsCount on selection so the customer enters it (no pre-filled default)', () => {
    const component = createComponent();

    component.customerForm.get('participantsCount')?.setValue(12);
    component.selectPackage(PackageType.S1);
    expect(component.customerForm.get('participantsCount')?.value).toBeNull();
  });

  it('sets the minimum-participants validator from the selected package (10 for S1/U1, 8 for others)', () => {
    const component = createComponent();
    const ctrl = component.customerForm.get('participantsCount')!;

    component.selectPackage(PackageType.S1);
    ctrl.setValue(9);
    expect(ctrl.hasError('min')).toBe(true); // S1 requires 10
    expect(component.minParticipants).toBe(10);

    component.selectPackage(PackageType.S2);
    ctrl.setValue(8);
    expect(ctrl.hasError('min')).toBe(false); // S2 allows 8
    expect(component.minParticipants).toBe(8);
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

  describe('idempotency key', () => {
    const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

    function armSubmit(component: BookingWizard) {
      component.hasOpenedTerms = true;
      component.selectedPackage = PackageType.S1;
      component.customerForm.setValue(VALID_FORM_VALUE);
      apiSpy.createBooking.mockReturnValue(of({ bookingId: 'id', paymentUrl: 'https://pay/x' }));
    }

    it('sends a UUID idempotency key that is reused across retries of the same attempt', () => {
      const component = createComponent();
      armSubmit(component);
      component.selectSlot(slot('2026-09-01T09:00:00+02:00', 90)); // one attempt → one key

      const original = window.location;
      Object.defineProperty(window, 'location', { value: { href: '' }, writable: true });
      component.submitBooking();
      component.submitBooking(); // retry of the same attempt
      Object.defineProperty(window, 'location', { value: original, writable: true });

      const first = apiSpy.createBooking.mock.calls[0][0].idempotencyKey;
      const second = apiSpy.createBooking.mock.calls[1][0].idempotencyKey;
      expect(first).toMatch(UUID);
      expect(second).toBe(first);
    });

    it('regenerates the key when a new slot is selected (a fresh attempt)', () => {
      const component = createComponent();
      armSubmit(component);

      const original = window.location;
      Object.defineProperty(window, 'location', { value: { href: '' }, writable: true });
      component.selectSlot(slot('2026-09-01T09:00:00+02:00', 90));
      component.submitBooking();
      component.selectSlot(slot('2026-09-01T14:00:00+02:00', 90)); // new attempt
      component.submitBooking();
      Object.defineProperty(window, 'location', { value: original, writable: true });

      const first = apiSpy.createBooking.mock.calls[0][0].idempotencyKey;
      const second = apiSpy.createBooking.mock.calls[1][0].idempotencyKey;
      expect(second).not.toBe(first);
    });
  });

  describe('when the slot was taken by someone else (409)', () => {
    it('clears the stale selection, returns to step 2, reloads slots, and shows an inline error', () => {
      const component = createComponent();
      component.hasOpenedTerms = true;
      component.selectedPackage = PackageType.S1;
      component.selectedDate = futureWeekday();
      component.selectedSlot = slot('2026-09-01T09:00:00+02:00', 90);
      component.currentStep = 3;
      component.customerForm.setValue(VALID_FORM_VALUE);

      apiSpy.createBooking.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 409 })));
      apiSpy.getAvailableSlots.mockReturnValue(of([]));

      component.submitBooking();

      expect(component.selectedSlot).toBeNull();
      expect(component.currentStep).toBe(2);
      expect(apiSpy.getAvailableSlots).toHaveBeenCalled();
      expect(component.submitErrorMessage).toContain('zarezerwowany przez kogoś innego');
    });

    it('clears the 409 error message once the user picks a new slot', () => {
      const component = createComponent();
      component.hasOpenedTerms = true;
      component.selectedPackage = PackageType.S1;
      component.selectedDate = futureWeekday();
      component.selectedSlot = slot('2026-09-01T09:00:00+02:00', 90);
      component.currentStep = 3;
      component.customerForm.setValue(VALID_FORM_VALUE);

      apiSpy.createBooking.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 409 })));
      apiSpy.getAvailableSlots.mockReturnValue(of([]));
      component.submitBooking();
      expect(component.submitErrorMessage).not.toBeNull();

      component.selectSlot(slot('2026-09-01T14:00:00+02:00', 90));

      expect(component.submitErrorMessage).toBeNull();
    });
  });

  describe('summaryBarMode', () => {
    it('is hidden on step 1 (package comparison, no bar)', () => {
      const component = createComponent();
      component.selectPackage(PackageType.S1); // advances to step 2 and selects a package
      component.currentStep = 1;
      expect(component.summaryBarMode).toBe('hidden');
    });

    it('is hidden before any package is selected', () => {
      const component = createComponent();
      component.currentStep = 2;
      expect(component.selectedPackage).toBeNull();
      expect(component.summaryBarMode).toBe('hidden');
    });

    it('is full on step 2 with a package selected', () => {
      const component = createComponent();
      component.selectPackage(PackageType.S1);
      expect(component.currentStep).toBe(2);
      expect(component.summaryBarMode).toBe('full');
    });

    it('is full on step 3 too (footer is consistent across steps 2 and 3)', () => {
      const component = createComponent();
      component.selectPackage(PackageType.S1);
      component.currentStep = 3;
      expect(component.summaryBarMode).toBe('full');
    });
  });

  describe('Total Price (the SUMA shown on step 2 once participants are chosen)', () => {
    it('is zero until the customer enters a participant count', () => {
      const component = createComponent();
      component.selectPackage(PackageType.S2); // field starts empty (no pre-filled default)
      expect(component.totalCost).toBe(0);
    });

    it('is the package per-person price multiplied by the chosen participant count', () => {
      const component = createComponent();
      component.selectPackage(PackageType.S2); // 65 PLN/os
      component.customerForm.get('participantsCount')?.setValue(8);
      expect(component.totalCost).toBe(65 * 8);

      component.customerForm.get('participantsCount')?.setValue(12);
      expect(component.totalCost).toBe(65 * 12);
    });
  });

  describe('bookableDateStrip (branded date-strip options)', () => {
    it('offers at least one and at most dateStripDays entries', () => {
      const component = createComponent();
      expect(component.bookableDateStrip.length).toBeGreaterThan(0);
      expect(component.bookableDateStrip.length).toBeLessThanOrEqual(component.dateStripDays);
    });

    it('only includes dates that pass the online-booking lead-time rule', () => {
      const component = createComponent();
      for (const iso of component.bookableDateStrip) {
        expect(component.isOnlineBookingAllowed(new Date(iso))).toBe(true);
      }
    });

    it('returns strictly ascending dates, none in the past', () => {
      const component = createComponent();
      const strip = component.bookableDateStrip;
      const today = new Date();
      today.setHours(0, 0, 0, 0);

      for (let i = 0; i < strip.length; i++) {
        expect(new Date(strip[i]).getTime()).toBeGreaterThanOrEqual(today.getTime());
        if (i > 0) {
          // ISO yyyy-mm-dd strings sort chronologically as plain strings
          expect(strip[i] > strip[i - 1]).toBe(true);
        }
      }
    });

    it('exposes a maxBookingDate roughly six months out for the native fallback', () => {
      const component = createComponent();
      const today = new Date();
      today.setHours(0, 0, 0, 0);
      const diffDays =
        (new Date(component.maxBookingDate).getTime() - today.getTime()) / (1000 * 60 * 60 * 24);
      expect(diffDays).toBeGreaterThan(150);
      expect(diffDays).toBeLessThan(200);
    });
  });

  describe('slot-loading announcement (screen-reader live region)', () => {
    function loadWith(slots: TimeSlot[]) {
      const component = createComponent();
      component.selectedPackage = PackageType.Max; // needs 120 min
      apiSpy.getAvailableSlots.mockReturnValue(of(slots));
      component.onDateChange(futureWeekday());
      return component;
    }

    it('announces the compatible slot count with the correct Polish plural (termin/terminy/terminów)', () => {
      // One component, reloaded several times (TestBed can't be reconfigured mid-test).
      const component = createComponent();
      component.selectedPackage = PackageType.Max;
      const date = futureWeekday();
      const announceFor = (n: number) => {
        apiSpy.getAvailableSlots.mockReturnValue(of(compatibleSlots(n)));
        component.onDateChange(date);
        return component.slotsAnnouncement;
      };

      expect(announceFor(1)).toBe('Załadowano 1 termin.');
      expect(announceFor(3)).toBe('Załadowano 3 terminy.');
      expect(announceFor(6)).toBe('Załadowano 6 terminów.');
      expect(announceFor(12)).toBe('Załadowano 12 terminów.'); // 12–14 exception
      expect(announceFor(22)).toBe('Załadowano 22 terminy.');
    });

    it('tells the user when the only available slots need a different package', () => {
      const component = loadWith([slot('2026-09-01T09:00:00+02:00', 90)]); // < 120 → incompatible
      expect(component.availableSlotsCompatible.length).toBe(0);
      expect(component.slotsAnnouncement).toBe(
        'Brak terminów dla wybranego pakietu. Dostępne terminy wymagają zmiany pakietu.'
      );
    });

    it('announces an empty result when the date has no slots at all', () => {
      expect(loadWith([]).slotsAnnouncement).toBe('Brak dostępnych terminów dla wybranej daty.');
    });

    it('clears the previous announcement when a new load begins', () => {
      const component = createComponent();
      component.selectedPackage = PackageType.Max;
      apiSpy.getAvailableSlots.mockReturnValue(of(compatibleSlots(2)));
      component.onDateChange(futureWeekday());
      expect(component.slotsAnnouncement).toContain('Załadowano');

      apiSpy.getAvailableSlots.mockReturnValue(NEVER); // in-flight: never resolves
      component.onDateChange(futureWeekday());
      expect(component.slotsAnnouncement).toBe('');
    });
  });

  describe('on a generic booking failure', () => {
    it('shows an inline error message but does not reset the wizard state', () => {
      const component = createComponent();
      component.hasOpenedTerms = true;
      component.selectedPackage = PackageType.S1;
      const originalSlot = slot('2026-09-01T09:00:00+02:00', 90);
      component.selectedSlot = originalSlot;
      component.currentStep = 3;
      component.customerForm.setValue(VALID_FORM_VALUE);

      apiSpy.createBooking.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 500 })));

      component.submitBooking();

      expect(component.submitErrorMessage).toBe('Rezerwacja nie powiodła się. Spróbuj ponownie.');
      expect(component.selectedSlot).toBe(originalSlot);
      expect(component.currentStep).toBe(3);
    });
  });
});
