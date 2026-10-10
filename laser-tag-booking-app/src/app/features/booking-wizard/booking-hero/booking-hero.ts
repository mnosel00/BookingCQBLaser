import { Component, Input } from '@angular/core';

/**
 * Presentational dark hero header for the booking wizard.
 *
 * Holds no logic. The `.hero__bg` layer is currently just a gradient; it exists
 * as a dedicated layer so a venue photograph can be dropped in later (see issue #2)
 * without restructuring the hero.
 */
@Component({
  selector: 'app-booking-hero',
  standalone: true,
  templateUrl: './booking-hero.html',
  styleUrl: './booking-hero.scss'
})
export class BookingHero {
  @Input() title = 'Zarezerwuj swoją grę w Comboarena.pl';
}
