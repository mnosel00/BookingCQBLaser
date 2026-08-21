import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Observable, of, throwError, NEVER } from 'rxjs';
import { BookingSuccessComponent } from './booking-success';
import { BookingApiService } from '../../services/booking-api';
import { BookingStatus } from '../../models/booking.models';

describe('BookingSuccessComponent', () => {
  let apiSpy: { getBookingStatus: ReturnType<typeof vi.fn> };

  function createComponent(statusSource: Observable<BookingStatus> = NEVER) {
    apiSpy = { getBookingStatus: vi.fn().mockReturnValue(statusSource) };

    TestBed.configureTestingModule({
      imports: [BookingSuccessComponent],
      providers: [{ provide: BookingApiService, useValue: apiSpy }, provideRouter([])],
    });

    return TestBed.createComponent(BookingSuccessComponent).componentInstance;
  }

  beforeEach(() => {
    sessionStorage.clear();
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('shows "unknown" and never calls the API when no booking id was stored', () => {
    const component = createComponent();

    component.ngOnInit();

    expect(component.state).toBe('unknown');
    expect(apiSpy.getBookingStatus).not.toHaveBeenCalled();
  });

  it('shows "paid" and clears the stored id when the booking is confirmed', () => {
    sessionStorage.setItem('lastBookingId', 'abc-123');
    const status: BookingStatus = { bookingId: 'abc-123', paymentStatus: 'Paid' };
    const component = createComponent(of(status));

    component.ngOnInit();

    expect(component.state).toBe('paid');
    expect(sessionStorage.getItem('lastBookingId')).toBeNull();
    expect(apiSpy.getBookingStatus).toHaveBeenCalledTimes(1);
  });

  it('shows "failed" and clears the stored id when payment failed', () => {
    sessionStorage.setItem('lastBookingId', 'abc-123');
    const status: BookingStatus = { bookingId: 'abc-123', paymentStatus: 'Failed' };
    const component = createComponent(of(status));

    component.ngOnInit();

    expect(component.state).toBe('failed');
    expect(sessionStorage.getItem('lastBookingId')).toBeNull();
  });

  it('polls up to 5 times while still Pending, then settles on "pending"', () => {
    sessionStorage.setItem('lastBookingId', 'abc-123');
    const status: BookingStatus = { bookingId: 'abc-123', paymentStatus: 'Pending' };
    const component = createComponent(of(status));

    vi.useFakeTimers();
    component.ngOnInit();

    // Attempt 1 fires synchronously from ngOnInit; 4 more are scheduled 2s apart.
    for (let i = 0; i < 4; i++) {
      vi.advanceTimersByTime(2000);
    }

    expect(component.state).toBe('pending');
    expect(apiSpy.getBookingStatus).toHaveBeenCalledTimes(5);
  });

  it('does not schedule a 6th poll once max attempts is reached', () => {
    sessionStorage.setItem('lastBookingId', 'abc-123');
    const status: BookingStatus = { bookingId: 'abc-123', paymentStatus: 'Pending' };
    const component = createComponent(of(status));

    vi.useFakeTimers();
    component.ngOnInit();
    for (let i = 0; i < 4; i++) {
      vi.advanceTimersByTime(2000);
    }
    vi.advanceTimersByTime(2000); // would trigger a 6th attempt if the bound were wrong

    expect(apiSpy.getBookingStatus).toHaveBeenCalledTimes(5);
  });

  it('shows "unknown" when the status check itself fails', () => {
    sessionStorage.setItem('lastBookingId', 'abc-123');
    const component = createComponent(throwError(() => new Error('network error')));

    component.ngOnInit();

    expect(component.state).toBe('unknown');
  });
});
