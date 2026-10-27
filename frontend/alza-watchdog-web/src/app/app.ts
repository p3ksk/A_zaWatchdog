import {ChangeDetectionStrategy, Component, computed, inject, viewChild} from '@angular/core';
import {toSignal} from '@angular/core/rxjs-interop';
import {RouterOutlet} from '@angular/router';
import {catchError, map, of} from 'rxjs';
import {ItemsStore} from './core/items.store';
import {WatchdogApi} from './core/watchdog-api.service';
import {AddItemDialogComponent} from './features/items/add-item-dialog.component';
import {AccessKeyComponent} from './features/account/access-key.component';
import {BackupDialogComponent} from './features/account/backup-dialog.component';
import {NotificationsDialogComponent} from './features/account/notifications-dialog.component';
import {NavbarComponent} from './features/shell/navbar.component';
import {BookmarkNoticeComponent} from './features/shell/bookmark-notice.component';

/**
 * Owns the navbar and the popups it opens. Keeping the dialogs here rather
 * than inside the routed page means they survive a list switch and there is only
 * ever one of each in the document.
 */
@Component({
  selector: 'app-root',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    RouterOutlet,
    NavbarComponent,
    BookmarkNoticeComponent,
    AddItemDialogComponent,
    AccessKeyComponent,
    BackupDialogComponent,
    NotificationsDialogComponent,
  ],
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App {
  private readonly store = inject(ItemsStore);

  private readonly addDialog = viewChild.required(AddItemDialogComponent);
  private readonly keysDialog = viewChild.required(AccessKeyComponent);
  private readonly backupDialog = viewChild.required(BackupDialogComponent);
  private readonly notificationsDialog = viewChild.required(NotificationsDialogComponent);

  protected readonly activeListId = computed(() => this.store.listId());

  protected readonly year = new Date().getFullYear();

  protected readonly version = toSignal(
    inject(WatchdogApi).getVersion().pipe(
      map((r) => r.version),
      catchError(() => of(null)),
    ),
    { initialValue: null },
  );

  protected openAdd(): void {
    this.addDialog().open();
  }

  protected openKeys(): void {
    this.keysDialog().open();
  }

  protected openBackup(): void {
    this.backupDialog().open();
  }

  protected openNotifications(): void {
    this.notificationsDialog().open();
  }
}
