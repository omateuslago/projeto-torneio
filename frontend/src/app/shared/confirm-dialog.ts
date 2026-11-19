import { Component, ElementRef, afterRenderEffect, input, output, viewChild } from '@angular/core';

@Component({
  selector: 'app-confirm-dialog',
  templateUrl: './confirm-dialog.html',
})
export class ConfirmDialog {
  readonly title = input.required<string>();
  readonly description = input.required<string>();
  readonly confirmLabel = input('Confirmar');
  readonly busy = input(false);
  readonly confirmed = output<void>();
  readonly cancelled = output<void>();
  private readonly cancelButton = viewChild<ElementRef<HTMLButtonElement>>('cancelButton');

  constructor() {
    afterRenderEffect(() => this.cancelButton()?.nativeElement.focus());
  }

  onKeydown(event: KeyboardEvent) {
    if (event.key === 'Escape' && !this.busy()) {
      event.preventDefault();
      this.cancelled.emit();
    }
  }
}
