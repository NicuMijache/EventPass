import { Component, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable, toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Params, Router } from '@angular/router';
import { catchError, debounceTime, distinctUntilChanged, map, of, startWith, switchMap } from 'rxjs';
import { EventsService } from '../../core/services/events.service';
import { EventQuery, PublicEventCard } from '../../core/models/event.models';
import { EventCard } from '../../components/event-card/event-card';
import { FilterPanel } from '../../components/filter-panel/filter-panel';

type LoadState =
  | { status: 'loading' }
  | { status: 'error' }
  | { status: 'ready'; events: PublicEventCard[] };

const toggle = (list: string[], value: string) =>
  list.includes(value) ? list.filter((v) => v !== value) : [...list, value];

// Pagina principală: toate evenimentele publicate, cu căutare și filtre.
// Filtrele stau în adresa paginii (?city=...&venue=...&q=...), deci se pot trimite ca link.
@Component({
  selector: 'app-home',
  imports: [EventCard, FilterPanel],
  templateUrl: './home.html',
  styleUrl: './home.scss',
})
export class Home {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly eventsService = inject(EventsService);

  private readonly params = toSignal(this.route.queryParamMap, { requireSync: true });

  protected readonly selectedCities = computed(() => this.params().getAll('city'));
  protected readonly selectedVenues = computed(() => this.params().getAll('venue'));
  private readonly searchTerm = computed(() => this.params().get('q') ?? '');

  // Textul din căutare; adresa se actualizează la 300 ms după ce utilizatorul se oprește din scris
  protected readonly searchText = signal(this.searchTerm());

  protected readonly filtersOpen = signal(false);
  private readonly reloadCount = signal(0);

  protected readonly filters = toSignal(
    this.eventsService.getFilters().pipe(catchError(() => of({ cities: [], venues: [] }))),
    { initialValue: null }
  );

  // Locațiile afișate: doar cele din orașele bifate (sau toate, dacă nu e bifat niciun oraș)
  protected readonly visibleVenues = computed(() => {
    const venues = this.filters()?.venues ?? [];
    const cities = this.selectedCities();
    return cities.length ? venues.filter((v) => cities.includes(v.city)) : venues;
  });

  protected readonly totalEvents = computed(
    () => this.filters()?.cities.reduce((sum, city) => sum + city.count, 0) ?? 0
  );

  protected readonly activeFilters = computed(() => [
    ...this.selectedCities().map((name) => ({ kind: 'city' as const, name })),
    ...this.selectedVenues().map((name) => ({ kind: 'venue' as const, name })),
  ]);

  private readonly query = computed<EventQuery & { reload: number }>(
    () => ({
      cities: this.selectedCities(),
      venues: this.selectedVenues(),
      search: this.searchTerm(),
      reload: this.reloadCount(),
    }),
    { equal: (a, b) => JSON.stringify(a) === JSON.stringify(b) }
  );

  // De fiecare dată când se schimbă filtrele, cerem din nou lista (cererea veche se anulează)
  protected readonly state = toSignal(
    toObservable(this.query).pipe(
      switchMap((query) =>
        this.eventsService.getEvents(query).pipe(
          map((events): LoadState => ({ status: 'ready', events })),
          catchError(() => of<LoadState>({ status: 'error' })),
          startWith<LoadState>({ status: 'loading' })
        )
      )
    ),
    { initialValue: { status: 'loading' } as LoadState }
  );

  protected readonly skeletons = Array.from({ length: 8 }, (_, i) => i);

  constructor() {
    toObservable(this.searchText)
      .pipe(
        debounceTime(300),
        map((text) => text.trim()),
        distinctUntilChanged(),
        takeUntilDestroyed()
      )
      .subscribe((q) => {
        if (q !== this.searchTerm()) {
          this.updateQuery({ q: q || null });
        }
      });
  }

  protected toggleCity(name: string): void {
    const cities = toggle(this.selectedCities(), name);
    // Păstrăm doar locațiile care mai aparțin orașelor rămase bifate
    const venues = cities.length
      ? this.selectedVenues().filter((venue) =>
          this.filters()?.venues.some((v) => v.name === venue && cities.includes(v.city))
        )
      : this.selectedVenues();
    this.updateQuery({ city: cities, venue: venues });
  }

  protected toggleVenue(name: string): void {
    this.updateQuery({ venue: toggle(this.selectedVenues(), name) });
  }

  protected removeFilter(filter: { kind: 'city' | 'venue'; name: string }): void {
    if (filter.kind === 'city') {
      this.toggleCity(filter.name);
    } else {
      this.toggleVenue(filter.name);
    }
  }

  protected clearCities(): void {
    this.updateQuery({ city: [], venue: [] });
  }

  protected clearVenues(): void {
    this.updateQuery({ venue: [] });
  }

  protected resetAll(): void {
    this.searchText.set('');
    this.router.navigate([], { relativeTo: this.route, queryParams: {}, replaceUrl: true });
  }

  protected retry(): void {
    this.reloadCount.update((n) => n + 1);
  }

  private updateQuery(changes: Params): void {
    // Listele goale dispar din adresă (null = parametrul se șterge)
    const queryParams = Object.fromEntries(
      Object.entries(changes).map(([key, value]) => [
        key,
        Array.isArray(value) && value.length === 0 ? null : value,
      ])
    );
    this.router.navigate([], {
      relativeTo: this.route,
      queryParams,
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });
  }
}
