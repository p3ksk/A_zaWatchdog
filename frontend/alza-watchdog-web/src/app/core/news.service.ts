import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { NewsItem } from './models';
import { WatchdogApi } from './watchdog-api.service';

/**
 * What the notification bell rings for: products whose price or availability
 * moved since the bell was last opened. The server keeps the mark, so every
 * device holding the key agrees on what is new.
 */
@Injectable({ providedIn: 'root' })
export class NewsService {
  private readonly api = inject(WatchdogApi);

  private readonly _items = signal<NewsItem[]>([]);
  private through = 0;

  readonly items = this._items.asReadonly();
  readonly count = computed(() => this._items().length);

  /** Quietly keeps what it had on failure: a missing badge is not worth an error. */
  async load(): Promise<void> {
    try {
      const news = await firstValueFrom(this.api.getNews());
      this.through = news.throughSnapshotId;
      this._items.set(news.items);
    } catch {
      // Left as it was.
    }
  }

  /**
   * Marks what was loaded as seen — only that, so a reading that arrived since
   * is still new next time.
   */
  async markSeen(): Promise<void> {
    if (this._items().length === 0) {
      return;
    }

    this._items.set([]);

    try {
      await firstValueFrom(this.api.markNewsSeen(this.through));
    } catch {
      // The server still holds the old mark, so the next load brings these back.
    }
  }

  reset(): void {
    this._items.set([]);
    this.through = 0;
  }
}
