# 1. Dark booking form (reversing the light-surface decision)

Date: 2026-10-10

## Status

Accepted

## Context

The ComboArena booking wizard (epic #1, PR #4) wraps a dark branded shell around
the booking flow. That work deliberately kept the form itself — the step panels,
inputs, package cards, cost receipt and summary bar — on **light, high-contrast
surfaces**, to protect legibility (issue #2: "clear, light, high-contrast
surfaces"; user stories about readability on a phone).

In use, the owner found the light form "too white": the white panels against the
near-black shell read as a glaring sheet rather than a premium, coherent
experience. The request is to invert the form to dark surfaces so the whole page
is of a piece.

This reverses a decision that was explicitly made and documented for #2, so it is
worth recording why.

## Decision

Move **all** form surfaces to a dark theme — step panels, inputs, package cards,
the step-3 cost receipt, the selected-package summary, the deposit note, and the
sticky summary bar — with light text and the venue's orange (`#FF6900`) kept as an
accent (progress, selected states, primary actions, totals), never as body text.

Legibility is retained as a **hard acceptance gate rather than an assumption**:
body text meets WCAG AA (AAA where feasible), interactive controls meet AA, and
icons / feature-checks / placeholders are verified visible on dark in the browser
preview. `color-scheme: dark` is set so native controls (the date fallback picker,
scrollbars) render dark as well.

## Consequences

- The booking flow reads as one coherent, branded, premium experience; no bright
  panel pasted onto a dark page.
- The "light surfaces protect legibility" guarantee from #2 is replaced by
  "dark surfaces + enforced AA contrast." Legibility now depends on holding that
  contrast bar on every surface, which is checked in-browser and must be
  re-checked whenever a surface or token changes.
- The change touches nearly every surface in the feature, so it is costly to
  reverse — hence this record. Pricing, the deposit, booking/validation logic and
  the step-3 receipt's numbers are unchanged; only presentation moved.
- Future surfaces added to the form must start dark and AA-checked, not light.
