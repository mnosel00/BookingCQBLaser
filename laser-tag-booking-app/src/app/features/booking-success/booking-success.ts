import { Component, OnInit, inject } from '@angular/core';
import { RouterModule } from '@angular/router';
import { BookingApiService } from '../../services/booking-api';

type SuccessViewState = 'checking' | 'paid' | 'pending' | 'failed' | 'unknown';

const STATUS_POLL_INTERVAL_MS = 2000;
const STATUS_POLL_MAX_ATTEMPTS = 5;

@Component({
  selector: 'app-booking-success',
  standalone: true,
  imports: [RouterModule],
  template: `
    <div class="success-container">
      <div class="success-card">
        @switch (state) {
          @case ('checking') {
            <h1>Sprawdzamy Twoją płatność…</h1>
            <p>To zajmie tylko chwilę.</p>
          }
          @case ('paid') {
            <h1>Dziękujemy za rezerwację!</h1>
            <p>Płatność została potwierdzona. Wysłaliśmy na Twój adres e-mail potwierdzenie rezerwacji wraz ze szczegółami.</p>
          }
          @case ('pending') {
            <h1>Otrzymaliśmy Twoje zgłoszenie</h1>
            <p>Wciąż czekamy na potwierdzenie płatności od operatora. Gdy tylko je otrzymamy, wyślemy potwierdzenie rezerwacji na Twój adres e-mail.</p>
          }
          @case ('failed') {
            <h1>Płatność się nie powiodła</h1>
            <p>Niestety nie udało się potwierdzić płatności, a rezerwacja nie została utrzymana. Prosimy spróbować ponownie lub skontaktować się z nami: 509 595 199.</p>
          }
          @case ('unknown') {
            <h1>Dziękujemy!</h1>
            <p>Gdy tylko otrzymamy płatność, wyślemy na Twój adres e-mail potwierdzenie rezerwacji wraz z szczegółami.</p>
          }
        }
        <a routerLink="/" class="home-btn">Wróć na stronę główną</a>
      </div>
    </div>
  `,
  styles: [`
    .success-container {
      display: flex;
      justify-content: center;
      align-items: center;
      min-height: 100dvh;
      background: #f8fafc;
      padding: 1.5rem;
      font-family: Inter, 'Segoe UI', Roboto, system-ui, sans-serif;
    }
    .success-card {
      background: white;
      padding: 3rem 2rem;
      border-radius: 24px;
      border: 1px solid rgba(226, 232, 240, 0.8);
      box-shadow: 0 24px 50px rgba(15, 23, 42, 0.08);
      text-align: center;
      max-width: 500px;
      width: 100%;
    }
    h1 {
      color: #0f172a;
      margin: 0 0 1rem;
      font-size: 1.8rem;
      font-weight: 800;
    }
    p {
      color: #475569;
      font-size: 1.05rem;
      line-height: 1.6;
      margin-bottom: 2.5rem;
    }
    .home-btn {
      display: inline-block;
      background: linear-gradient(135deg, #4f46e5, #7c3aed);
      color: white;
      text-decoration: none;
      padding: 0.85rem 1.5rem;
      border-radius: 12px;
      font-weight: 700;
      transition: transform 0.2s ease, box-shadow 0.2s ease;
    }
    .home-btn:hover {
      transform: translateY(-2px);
      box-shadow: 0 12px 24px rgba(79, 70, 229, 0.3);
    }
  `]
})
export class BookingSuccessComponent implements OnInit {
  private readonly bookingApiService = inject(BookingApiService);

  state: SuccessViewState = 'checking';

  ngOnInit(): void {
    const bookingId = sessionStorage.getItem('lastBookingId');

    if (!bookingId) {
      this.state = 'unknown';
      return;
    }

    this.pollStatus(bookingId, 1);
  }

  private pollStatus(bookingId: string, attempt: number): void {
    this.bookingApiService.getBookingStatus(bookingId).subscribe({
      next: (result) => {
        if (result.paymentStatus === 'Paid') {
          this.state = 'paid';
          sessionStorage.removeItem('lastBookingId');
          return;
        }

        if (result.paymentStatus === 'Failed') {
          this.state = 'failed';
          sessionStorage.removeItem('lastBookingId');
          return;
        }

        // Still Pending: the HotPay webhook may not have arrived yet, retry briefly.
        if (attempt < STATUS_POLL_MAX_ATTEMPTS) {
          setTimeout(() => this.pollStatus(bookingId, attempt + 1), STATUS_POLL_INTERVAL_MS);
        } else {
          this.state = 'pending';
        }
      },
      error: () => {
        this.state = 'unknown';
      }
    });
  }
}
