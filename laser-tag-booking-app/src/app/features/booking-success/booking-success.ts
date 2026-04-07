import { Component } from '@angular/core';
import { RouterModule } from '@angular/router';

@Component({
  selector: 'app-booking-success',
  standalone: true,
  imports: [RouterModule],
  template: `
    <div class="success-container">
      <div class="success-card">
        <h1>Dziękujemy za rezerwację!</h1>
        <p>Twoja płatność jest przetwarzana. Gdy tylko otrzymamy potwierdzenie z banku, wyślemy na Twój adres e-mail oficjalne potwierdzenie rezerwacji oraz bilet wstępu.</p>
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
export class BookingSuccessComponent {}