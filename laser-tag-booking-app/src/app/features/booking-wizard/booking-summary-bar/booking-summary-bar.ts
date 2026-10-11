import { Component, Input } from '@angular/core';

export type SummaryBarMode = 'hidden' | 'full';

/**
 * Presentational "booking so far" bar. Holds no logic and does no pricing
 * calculation — all values arrive via inputs from BookingWizard, which owns the
 * tested `summaryBarMode` decision. Uses the glossary terms Package / Total Price
 * / Deposit.
 *
 * Shows Package + Total Price + Deposit. The parent only renders it when the mode
 * is `full` (i.e. not `hidden`), so the footer reads the same on steps 2 and 3.
 */
@Component({
  selector: 'app-booking-summary-bar',
  standalone: true,
  templateUrl: './booking-summary-bar.html',
  styleUrl: './booking-summary-bar.scss'
})
export class BookingSummaryBar {
  @Input() packageName = '';
  @Input() totalPrice = 0;
  @Input() depositAmount = 0;
}
