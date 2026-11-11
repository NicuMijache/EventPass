import { Component, computed, ElementRef, HostListener, inject, signal } from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';
import { ORGANIZER_ROLES, SCANNER_ROLES } from '../../core/models/auth.models';
import { AuthService } from '../../core/services/auth.service';

@Component({
  selector: 'app-header',
  imports: [RouterLink, RouterLinkActive],
  templateUrl: './header.html',
  styleUrl: './header.scss',
})
export class Header {
  protected readonly auth = inject(AuthService);
  private readonly host = inject(ElementRef<HTMLElement>);

  // „Scanează” apare doar pentru personal (Staff, Organizer, Admin), nu pentru clienți
  protected readonly canScan = computed(() => {
    const role = this.auth.user()?.role;
    return !!role && SCANNER_ROLES.includes(role);
  });

  // „Panou” (vânzări și participanți) apare doar pentru organizatori
  protected readonly canManage = computed(() => {
    const role = this.auth.user()?.role;
    return !!role && ORGANIZER_ROLES.includes(role);
  });

  // Meniul de pe telefon și meniul contului (deschis / închis)
  protected readonly menuOpen = signal(false);
  protected readonly accountOpen = signal(false);

  // Inițialele din nume, afișate în cercul contului (ex. „Ion Popescu” → „IP”)
  protected readonly initials = computed(() => {
    const name = this.auth.user()?.fullName ?? '';
    return name
      .split(/\s+/)
      .filter(Boolean)
      .slice(0, 2)
      .map((part) => part[0]!.toUpperCase())
      .join('');
  });

  protected toggleMenu(): void {
    this.menuOpen.update((open) => !open);
    this.accountOpen.set(false);
  }

  protected toggleAccount(): void {
    this.accountOpen.update((open) => !open);
    this.menuOpen.set(false);
  }

  protected closeMenus(): void {
    this.menuOpen.set(false);
    this.accountOpen.set(false);
  }

  protected logout(): void {
    this.closeMenus();
    this.auth.logout();
  }

  // Un click oriunde în afara antetului închide meniurile
  @HostListener('document:click', ['$event'])
  protected onDocumentClick(event: MouseEvent): void {
    if (!this.host.nativeElement.contains(event.target as Node)) {
      this.closeMenus();
    }
  }

  @HostListener('document:keydown.escape')
  protected onEscape(): void {
    this.closeMenus();
  }
}
