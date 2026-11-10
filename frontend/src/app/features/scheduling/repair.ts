import { Component, DestroyRef, inject, input, output, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { finalize } from 'rxjs';
import { Area, Referee, ScheduledMatch } from './scheduling-api';
import { Proposal } from './autopilot';
import { apiError } from '../../core/api-error';
import { environment } from '../../../environments/environment';
interface Row {
  matchId: string;
  startsAt: string;
  playingAreaId: string;
  refereeId: string | null;
}
export interface RepairProposal {
  status: string;
  incident: unknown;
  changes: { before: Row; after: Row }[];
  metrics: {
    impactedCount: number;
    changedCount: number;
    preservedCount: number;
    totalDisplacementMinutes: number;
    maxDisplacementMinutes: number;
    areaChanges: number;
    refereeChanges: number;
  };
  warnings: string[];
  fairness: Proposal['fairness'];
  resilience: Proposal['resilience'];
  proposalToken: string | null;
}
@Component({ selector: 'app-repair', imports: [ReactiveFormsModule], templateUrl: './repair.html' })
export class Repair {
  readonly tournamentId = input.required<string>();
  readonly matches = input.required<ScheduledMatch[]>();
  readonly areas = input.required<Area[]>();
  readonly referees = input.required<Referee[]>();
  readonly teams = input.required<{ teamId: string; teamName: string }[]>();
  readonly zone = input('UTC');
  readonly blocked = input(false);
  readonly applied = output<void>();
  readonly busy = signal(false);
  readonly proposal = signal<RepairProposal | null>(null);
  readonly error = signal('');
  readonly message = signal('');
  readonly browserZone = Intl.DateTimeFormat().resolvedOptions().timeZone;
  private http = inject(HttpClient);
  private destroy = inject(DestroyRef);
  readonly form = inject(FormBuilder).nonNullable.group({
    kind: ['playingAreaUnavailable'],
    resourceId: [''],
    matchId: [''],
    startsAt: [''],
    endsAt: [''],
    delayMinutes: [15],
    horizonStart: ['', Validators.required],
    horizonEnd: ['', Validators.required],
    granularityMinutes: [
      15,
      [Validators.required, Validators.min(1), Validators.max(60), Validators.pattern(/^\d+$/)],
    ],
  });
  resourceName(id: string | null, kind: 'area' | 'referee') {
    return (
      (kind === 'area' ? this.areas() : this.referees()).find((r) => r.id === id)?.name ??
      (id || 'Sem árbitro')
    );
  }
  matchName(id: string) {
    const m = this.matches().find((m) => m.matchId === id);
    return m ? `${m.homeTeam} × ${m.awayTeam}` : id;
  }
  display(value: string) {
    return new Intl.DateTimeFormat('pt-BR', {
      timeZone: this.zone(),
      dateStyle: 'short',
      timeStyle: 'short',
    }).format(new Date(value));
  }
  incidentSummary = signal('');
  private url(action: string) {
    return `${environment.apiUrl}/tournaments/${this.tournamentId()}/schedule/repair/${action}`;
  }
  generate() {
    if (this.busy() || this.blocked()) return;
    this.form.markAllAsTouched();
    this.error.set('');
    this.proposal.set(null);
    const v = this.form.getRawValue();
    const instant = (s: string) => new Date(s).toISOString();
    let payload;
    try {
      if (this.form.invalid) throw new Error('Informe horizonte e grade válidos.');
      const horizonStart = instant(v.horizonStart),
        horizonEnd = instant(v.horizonEnd);
      if (
        horizonStart >= horizonEnd ||
        Date.parse(horizonEnd) - Date.parse(horizonStart) > 31 * 86400000
      )
        throw new Error('Horizonte deve ter até 31 dias.');
      let incident;
      if (v.kind === 'matchDelayed') {
        if (
          !v.matchId ||
          !Number.isInteger(v.delayMinutes) ||
          v.delayMinutes < 1 ||
          v.delayMinutes > 240
        )
          throw new Error('Informe partida e atraso de 1–240 minutos.');
        incident = { kind: v.kind, matchId: v.matchId, delayMinutes: v.delayMinutes };
        this.incidentSummary.set(
          `${this.matchName(v.matchId)}: atraso de ${v.delayMinutes} minutos`,
        );
      } else {
        const startsAt = instant(v.startsAt),
          endsAt = instant(v.endsAt);
        if (!v.resourceId || startsAt >= endsAt)
          throw new Error('Informe recurso e intervalo válidos.');
        incident = { kind: v.kind, resourceId: v.resourceId, startsAt, endsAt };
        const name =
          v.kind === 'teamUnavailable'
            ? this.teams().find((t) => t.teamId === v.resourceId)?.teamName
            : this.resourceName(
                v.resourceId,
                v.kind === 'playingAreaUnavailable' ? 'area' : 'referee',
              );
        this.incidentSummary.set(
          `${name} indisponível: ${this.display(startsAt)} → ${this.display(endsAt)}`,
        );
      }
      payload = {
        horizon: { horizonStart, horizonEnd, granularityMinutes: v.granularityMinutes },
        incident,
      };
    } catch (e) {
      this.error.set(e instanceof Error ? e.message : 'Dados inválidos.');
      return;
    }
    this.busy.set(true);
    this.message.set('');
    this.http
      .post<RepairProposal>(this.url('generate'), payload)
      .pipe(
        takeUntilDestroyed(this.destroy),
        finalize(() => this.busy.set(false)),
      )
      .subscribe({ next: (p) => this.proposal.set(p), error: (e) => this.error.set(apiError(e)) });
  }
  apply() {
    const token = this.proposal()?.proposalToken;
    if (!token || this.busy() || this.blocked()) return;
    this.busy.set(true);
    this.error.set('');
    this.http
      .post(this.url('apply'), { proposalToken: token })
      .pipe(
        takeUntilDestroyed(this.destroy),
        finalize(() => this.busy.set(false)),
      )
      .subscribe({
        next: () => {
          this.proposal.set(null);
          this.message.set('Reparo aplicado.');
          this.applied.emit();
        },
        error: (e) => {
          this.error.set(apiError(e));
          if (e.status === 409) this.proposal.set(null);
        },
      });
  }
  discard() {
    if (!this.busy()) {
      this.proposal.set(null);
      this.message.set('Proposta descartada. Agenda preservada.');
    }
  }
}
