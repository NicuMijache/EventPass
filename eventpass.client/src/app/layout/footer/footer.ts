import { Component } from '@angular/core';

@Component({
  selector: 'app-footer',
  template: `
    <footer class="footer">
      <div class="container footer__inner">
        <span class="footer__brand">EVENTPASS</span>
        <span class="footer__note">Proiect universitar · plăți în mod test Stripe</span>
        <span class="footer__note">© {{ year }}</span>
      </div>
    </footer>
  `,
  styles: `
    .footer {
      margin-top: var(--space-24);
      border-top: 1px solid var(--color-border);
    }

    .footer__inner {
      display: flex;
      flex-wrap: wrap;
      justify-content: space-between;
      gap: var(--space-4);
      padding-block: var(--space-8);
      font-size: var(--text-sm);
    }

    .footer__brand {
      font-weight: 600;
      letter-spacing: 0.14em;
    }

    .footer__note {
      color: var(--color-text-muted);
    }
  `,
})
export class Footer {
  protected readonly year = new Date().getFullYear();
}
