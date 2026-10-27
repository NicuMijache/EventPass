import { Component, computed, input, output, signal } from '@angular/core';
import { FilterOption, VenueOption } from '../../core/models/event.models';

// Panoul lateral cu bife pentru oraș și locație.
// Nu ține starea filtrelor: primește ce e selectat și anunță pagina când se schimbă ceva.
@Component({
  selector: 'app-filter-panel',
  templateUrl: './filter-panel.html',
  styleUrl: './filter-panel.scss',
})
export class FilterPanel {
  readonly cities = input.required<FilterOption[]>();
  readonly venues = input.required<VenueOption[]>();
  readonly selectedCities = input.required<string[]>();
  readonly selectedVenues = input.required<string[]>();

  readonly cityToggled = output<string>();
  readonly venueToggled = output<string>();
  readonly citiesCleared = output<void>();
  readonly venuesCleared = output<void>();

  // Câte locații se văd înainte de „Vezi mai multe”
  private readonly collapsedVenueCount = 6;
  protected readonly showAllVenues = signal(false);

  protected readonly visibleVenues = computed(() =>
    this.showAllVenues() ? this.venues() : this.venues().slice(0, this.collapsedVenueCount)
  );
  protected readonly hasMoreVenues = computed(() => this.venues().length > this.collapsedVenueCount);
}
