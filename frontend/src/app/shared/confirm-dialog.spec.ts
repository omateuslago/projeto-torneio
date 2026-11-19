import { TestBed } from '@angular/core/testing';
import { ConfirmDialog } from './confirm-dialog';

describe('ConfirmDialog', () => {
  it('confirma e cancela uma ação destrutiva', () => {
    const fixture = TestBed.createComponent(ConfirmDialog);
    fixture.componentRef.setInput('title', 'Remover acesso?');
    fixture.componentRef.setInput('description', 'A pessoa perderá o acesso.');
    fixture.detectChanges();
    const confirmed = vi.fn();
    const cancelled = vi.fn();
    fixture.componentInstance.confirmed.subscribe(confirmed);
    fixture.componentInstance.cancelled.subscribe(cancelled);
    const buttons = fixture.nativeElement.querySelectorAll('button');
    buttons[1].click();
    expect(confirmed).toHaveBeenCalledOnce();
    fixture.componentInstance.onKeydown(new KeyboardEvent('keydown', { key: 'Escape' }));
    expect(cancelled).toHaveBeenCalledOnce();
  });
});
