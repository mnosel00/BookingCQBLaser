import { Component } from '@angular/core';
import { BookingWizard } from '../app/features/booking-wizard/booking-wizard';
import { RouterOutlet } from '@angular/router';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [RouterOutlet],
  templateUrl: './app.html',
  styleUrl: './app.scss'
})
export class App {}
