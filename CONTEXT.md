# ComboArena Booking

Domain glossary for a laser-tag arena booking system. A customer reserves the single physical arena for a block of time, pays a deposit online, and settles the remainder on-site.

## Language

**Booking**
A single reservation of the arena for a fixed time window, tied to one customer and one Package. A Booking blocks the *entire* facility — ComboArena operates one arena, so only one Booking can be active in any given time range. Once created, a Booking can never be cancelled or rescheduled; this is a permanent business policy, not a missing feature.
_Avoid_: Session, Reservation, Appointment

**Package**
The specific laser-tag game a Booking is for — determines its duration, price, and Package Category. Encoded today as one of eight variants (`S1, S2, Premium, Max, U1, U2, U3, Combat`).
_Avoid_: Product, Plan

**Package Category**
Which of the two product lines a Package belongs to: *Regular* (a standalone laser-tag game) or *Birthday* (a birthday-party package, includes room time for the party). Customers and staff think in these two lines even though the code only encodes it via UI grouping today.
_Avoid_: Product line, Booking type

**Participants**
The number of people in a Booking's group. Must be between 8 and 26 — except Packages `S1` and `U1`, which require at least 10.
_Avoid_: Group size, headcount

**Adult Group**
A Booking where every participant is 18 or older. A Booking that is not an Adult Group must specify an Age Range.
_Avoid_: —

**Age Range**
The age bracket of participants in a Booking that is not an Adult Group. Minimum participant age is 10; there is no bracket above 18 (that's an Adult Group instead).
_Avoid_: Birth year, age

**Deposit**
The fixed 300 PLN payment made online at booking time to hold the slot, regardless of group size or Package.
_Avoid_: —

**Service Fee**
The 4 PLN online-payment processing surcharge added on top of the Deposit (304 PLN charged online in total). It never reduces the Remaining Balance — it's payment-processing overhead, not part of the arena's price.
_Avoid_: —

**Total Price**
The full price of a Booking: the Package's per-person price multiplied by Participants.
_Avoid_: Total cost

**Remaining Balance**
The amount owed on-site: Total Price minus the Deposit (300 PLN — the Service Fee is never subtracted here).
_Avoid_: Balance due
