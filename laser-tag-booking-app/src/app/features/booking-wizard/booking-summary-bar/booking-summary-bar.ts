import { Component, Input } from '@angular/core';

export type SummaryBarMode = 'hidden' | 'full' | 'collapsed';

/**
 * Presentational "booking so far" bar. Holds no logic and does no pricing
 * calculation — all values arrive via inputs from BookingWizard, which owns the
 * tested `summaryBarMode` decision. Uses the glossary terms Package / Total Price
 * / Deposit.
 *
 * - `full`      → Package + Total Price + Deposit (step 2).
 * - `collapsed` → Package name only (step 3, where the detailed cost receipt owns
 *                 the numbers).
 *
 * The parent only renders this component when the mode is not `hidden`.
 */
@Component({
  selector: 'app-booking-summary-bar',
  standalone: true,
  templateUrl: './booking-summary-bar.html',
  styleUrl: './booking-summary-bar.scss'
})
export class BookingSummaryBar {
  @Input() mode: Exclude<SummaryBarMode, 'hidden'> = 'full';
  @Input() packageName = '';
  @Input() totalCost = 0;
  @Input() depositAmount = 0;
}
