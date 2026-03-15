import { ComponentFixture, TestBed } from '@angular/core/testing';

import { BookingWizard } from './booking-wizard';

describe('BookingWizard', () => {
  let component: BookingWizard;
  let fixture: ComponentFixture<BookingWizard>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [BookingWizard]
    })
    .compileComponents();

    fixture = TestBed.createComponent(BookingWizard);
    component = fixture.componentInstance;
    await fixture.whenStable();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
