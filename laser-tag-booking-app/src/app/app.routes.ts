import { Routes } from '@angular/router';
import { BookingWizard } from './features/booking-wizard/booking-wizard';
import { BookingSuccessComponent } from './features/booking-success/booking-success';

export const routes: Routes = [
  { path: '', component: BookingWizard },
  { path: 'sukces', component: BookingSuccessComponent },
  { path: '**', redirectTo: '' }
];
