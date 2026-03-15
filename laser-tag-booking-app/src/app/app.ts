import { Component } from '@angular/core';
import { BookingWizard } from '../app/features/booking-wizard/booking-wizard';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [BookingWizard],
  templateUrl: './app.html',
  styleUrl: './app.scss'
})
export class AppComponent {}
