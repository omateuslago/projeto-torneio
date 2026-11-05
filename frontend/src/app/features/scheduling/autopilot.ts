import { Component, DestroyRef, computed, inject, input, output, signal } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { finalize } from 'rxjs';
import { environment } from '../../../environments/environment';
import { apiError } from '../../core/api-error';
import { Area, Referee, ScheduledMatch } from './scheduling-api';

export interface Proposal {
  status: 'optimal' | 'feasible' | 'infeasible' | 'timeout' | 'error';
  proposalToken: string | null;
  expiresAt: string | null;
  warnings: string[];
  scheduledMatches: {
    matchId: string;
    startsAt: string;
    playingAreaId: string;
    refereeId: string | null;
  }[];
  metrics: {
    scheduledCount: number;
    preservedCount: number;
    startsAt: string | null;
    endsAt: string | null;
    playingAreasUsed: number;
    refereesUsed: number;
  };
}
@Component({
  selector: 'app-autopilot',
  imports: [ReactiveFormsModule],
  templateUrl: './autopilot.html',
})
export class Autopilot {
  readonly tournamentId = input.required<string>();
  readonly matches = input.required<ScheduledMatch[]>();
  readonly areas = input.required<Area[]>();
  readonly referees = input.required<Referee[]>();
  readonly zone = input('UTC');
  readonly blocked = input(false);
  readonly applied = output<void>();
  private http = inject(HttpClient);
  private destroy = inject(DestroyRef);
  readonly browserZone = Intl.DateTimeFormat().resolvedOptions().timeZone;
  readonly calculating = signal(false);
  readonly applying = signal(false);
  readonly proposal = signal<Proposal | null>(null);
  readonly error = signal('');
  readonly message = signal('');
  readonly pending = computed(
    () => this.matches().filter((m) => !m.startsAt && m.matchStatus !== 'Finished').length,
  );
  readonly form = inject(FormBuilder).nonNullable.group({
    horizonStart: ['', Validators.required],
    horizonEnd: ['', Validators.required],
    granularityMinutes: [15, [Validators.required, Validators.min(1), Validators.max(60)]],
    requireReferee: [false],
  });
  readonly preview = computed(() => {
    const groups = new Map<
      string,
      { match: string; area: string; referee: string; time: string }[]
    >();
    for (const row of [...(this.proposal()?.scheduledMatches ?? [])].sort(
      (a, b) => Date.parse(a.startsAt) - Date.parse(b.startsAt),
    )) {
      const m = this.matches().find((m) => m.matchId === row.matchId);
      const date = new Intl.DateTimeFormat('pt-BR', {
        timeZone: this.zone(),
        dateStyle: 'full',
      }).format(new Date(row.startsAt));
      groups.set(date, [
        ...(groups.get(date) ?? []),
        {
          match: m ? `${m.homeTeam} × ${m.awayTeam}` : row.matchId,
          area: this.areas().find((a) => a.id === row.playingAreaId)?.name ?? row.playingAreaId,
          referee: this.referees().find((r) => r.id === row.refereeId)?.name ?? 'Sem árbitro',
          time: this.display(row.startsAt),
        },
      ]);
    }
    return [...groups].map(([date, rows]) => ({ date, rows }));
  });
  display(value: string) {
    return new Intl.DateTimeFormat('pt-BR', {
      timeZone: this.zone(),
      dateStyle: 'short',
      timeStyle: 'short',
    }).format(new Date(value));
  }
  private url(action: string) {
    return `${environment.apiUrl}/tournaments/${this.tournamentId()}/schedule/${action}`;
  }
  generate() {
    this.form.markAllAsTouched();
    if (this.form.invalid || this.calculating() || this.applying() || this.blocked()) return;
    const v = this.form.getRawValue();
    const start = new Date(v.horizonStart),
      end = new Date(v.horizonEnd);
    if (
      !Number.isFinite(start.getTime()) ||
      !Number.isFinite(end.getTime()) ||
      end <= start ||
      end.getTime() - start.getTime() > 31 * 86400000
    ) {
      this.error.set('Informe um período válido de até 31 dias.');
      return;
    }
    this.calculating.set(true);
    this.proposal.set(null);
    this.error.set('');
    this.message.set('');
    this.http
      .post<Proposal>(this.url('generate'), {
        ...v,
        horizonStart: start.toISOString(),
        horizonEnd: end.toISOString(),
      })
      .pipe(
        takeUntilDestroyed(this.destroy),
        finalize(() => this.calculating.set(false)),
      )
      .subscribe({ next: (p) => this.proposal.set(p), error: (e) => this.error.set(apiError(e)) });
  }
  apply() {
    const p = this.proposal();
    if (!p?.proposalToken || this.applying() || this.blocked()) return;
    this.applying.set(true);
    this.error.set('');
    this.http
      .post<{ appliedCount: number }>(this.url('apply'), { proposalToken: p.proposalToken })
      .pipe(
        takeUntilDestroyed(this.destroy),
        finalize(() => this.applying.set(false)),
      )
      .subscribe({
        next: (r) => {
          this.proposal.set(null);
          this.message.set(`Agenda aplicada: ${r.appliedCount} partidas agendadas.`);
          this.applied.emit();
        },
        error: (e) => {
          this.error.set(apiError(e));
          if (e instanceof HttpErrorResponse && e.status === 409) this.proposal.set(null);
        },
      });
  }
  discard() {
    if (this.applying()) return;
    this.proposal.set(null);
    this.error.set('');
    this.message.set('Proposta descartada. A agenda foi preservada.');
  }
}
