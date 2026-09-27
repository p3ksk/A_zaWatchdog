import { ChangeDetectionStrategy, Component, ElementRef, computed, inject, signal, viewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AccountService } from '../../core/account.service';
import { describeError } from '../../core/watchdog-api.service';

/**
 * Where someone hands over an address for price alerts.
 *
 * An address is never believed on the strength of being typed here: the server
 * mails it a link and stays silent until that link is followed, so this dialog's
 * main job is to be clear about which of the two states the address is in.
 * The address itself is never shown: holding the account link is not proof of
 * owning the mailbox, so the server only says whether one is on file.
 */
@Component({
  selector: 'app-notifications-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule],
  templateUrl: './notifications-dialog.component.html',
  styleUrl: './notifications-dialog.component.scss',
})
export class NotificationsDialogComponent {
  private readonly account = inject(AccountService);
  private readonly dialog = viewChild.required<ElementRef<HTMLDialogElement>>('dialog');

  protected readonly saved = this.account.hasEmail;
  protected readonly confirmed = this.account.emailConfirmed;

  protected readonly draft = signal('');
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly note = signal<string | null>(null);

  protected readonly canSave = computed(() => this.draft().trim().length > 2 && !this.busy());

  open(): void {
    this.reset();
    this.dialog().nativeElement.showModal();
  }

  protected close(): void {
    this.dialog().nativeElement.close();
  }

  /** A native dialog's backdrop clicks land on the dialog element itself. */
  protected closeOnBackdrop(event: MouseEvent): void {
    if (event.target === this.dialog().nativeElement) {
      this.close();
    }
  }

  protected reset(): void {
    this.draft.set('');
    this.busy.set(false);
    this.error.set(null);
    this.note.set(null);
  }

  /** Also how a link that never arrived is re-sent: every save mints a new one. */
  protected async save(): Promise<void> {
    await this.run(this.draft().trim(), 'Check that inbox for the confirmation link.');
  }

  protected async remove(): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    this.note.set(null);

    try {
      await this.account.clearEmail();
      this.note.set('Address removed. No more emails will be sent.');
    } catch (error) {
      this.error.set(describeError(error, 'Could not remove that address.'));
    } finally {
      this.busy.set(false);
    }
  }

  private async run(address: string, success: string): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    this.note.set(null);

    try {
      await this.account.setEmail(address);
      this.draft.set('');
      this.note.set(success);
    } catch (error) {
      this.error.set(describeError(error, 'Could not save that address.'));
    } finally {
      this.busy.set(false);
    }
  }
}
