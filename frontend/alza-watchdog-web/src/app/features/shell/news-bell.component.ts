import { ChangeDetectionStrategy, Component, ElementRef, computed, inject, signal, viewChild } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AccountService } from '../../core/account.service';
import { ClockService } from '../../core/clock.service';
import { formatAvailability, formatExact, formatPrice, formatRelative } from '../../core/format';
import { compactGuid } from '../../core/guid';
import { NewsItem } from '../../core/models';
import { NewsService } from '../../core/news.service';
import { payableAt } from '../../core/pricing';

interface NewsRow {
  item: NewsItem;
  price: string;
  /** "▼ 2,00 €", or null when the payable price did not move. */
  change: string | null;
  isDrop: boolean;
  /** Set only when availability is what changed. */
  availability: string | null;
}

/**
 * The bell in the navbar. Its badge counts products that moved since it was last
 * opened; opening it lists each one once, at its newest reading, and marks them
 * seen. The list stays on screen until the panel closes, so it does not vanish
 * while being read.
 */
@Component({
  selector: 'app-news-bell',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink],
  templateUrl: './news-bell.component.html',
  styleUrl: './news-bell.component.scss',
})
export class NewsBellComponent {
  private readonly news = inject(NewsService);
  private readonly account = inject(AccountService);
  private readonly clock = inject(ClockService);

  private readonly panel = viewChild<ElementRef<HTMLDetailsElement>>('panel');

  protected readonly count = this.news.count;

  /** What the open panel is showing, held apart from the service which is cleared on open. */
  private readonly shown = signal<NewsItem[]>([]);

  protected readonly rows = computed<NewsRow[]>(() => {
    const hasAlzaPlus = this.account.hasAlzaPlus();

    return this.shown().map((item) => {
      const now = payableAt(item.latest, hasAlzaPlus);
      const before = item.previous ? payableAt(item.previous, hasAlzaPlus) : null;
      const delta = now !== null && before !== null ? now - before : 0;
      const availabilityMoved = item.previous !== null && item.previous.availability !== item.latest.availability;

      return {
        item,
        price: formatPrice(now, item.currency),
        change: delta === 0 ? null : `${delta < 0 ? '▼' : '▲'} ${formatPrice(Math.abs(delta), item.currency)}`,
        isDrop: delta < 0,
        availability: availabilityMoved ? formatAvailability(item.latest.availability) : null,
      };
    });
  });

  protected readonly label = computed(() =>
    this.count() > 0 ? `Notifications, ${this.count()} new` : 'Notifications',
  );

  protected onToggle(open: boolean): void {
    if (open) {
      this.shown.set(this.news.items());
      void this.news.markSeen();
    } else {
      this.shown.set([]);
    }
  }

  protected close(): void {
    this.panel()?.nativeElement.removeAttribute('open');
  }

  protected listLink(listId: string): unknown[] {
    return ['/user', compactGuid(this.account.key()), 'list', compactGuid(listId)];
  }

  protected relative(iso: string): string {
    return formatRelative(iso, this.clock.now());
  }

  protected exact(iso: string): string {
    return formatExact(iso);
  }
}
