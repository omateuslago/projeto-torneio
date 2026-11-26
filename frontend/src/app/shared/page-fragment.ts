import { DOCUMENT } from '@angular/common';
import { Directive, ElementRef, afterRenderEffect, inject, input } from '@angular/core';

@Directive({
  selector: 'a[pageFragment]',
  host: { '[attr.href]': 'href' },
})
export class PageFragment {
  readonly pageFragment = input.required<string>();
  private readonly document = inject(DOCUMENT);

  get href() {
    // A bare fragment resolves against the application's <base href>.
    const { pathname, search } = this.document.location;
    return `${pathname}${search}#${this.pageFragment()}`;
  }
}

@Directive({ selector: '[restoreFragment]' })
export class RestoreFragment {
  readonly restoreFragment = input.required<boolean>();
  private readonly document = inject(DOCUMENT);
  private readonly element = inject(ElementRef<HTMLElement>).nativeElement;
  private restored = false;

  constructor() {
    afterRenderEffect(() => {
      if (this.restored || !this.restoreFragment()) return;
      this.restored = true;
      if (this.document.location.hash === `#${this.element.id}`) {
        this.element.scrollIntoView?.();
      }
    });
  }
}
