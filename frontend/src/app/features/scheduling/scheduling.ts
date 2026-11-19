import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Observable, forkJoin, finalize } from 'rxjs';
import { TournamentApi } from '../../core/tournament-api';
import { Registration } from '../../core/models';
import { apiError } from '../../core/api-error';
import { Repair } from './repair';
import { Autopilot } from './autopilot';
import { trimmedRequired } from '../../shared/trimmed-required';
import { AuthService } from '../../core/auth.service';
import {
  Area,
  Availability,
  Referee,
  ResourceKind,
  ScheduledMatch,
  SchedulingApi,
  Venue,
} from './scheduling-api';

@Component({
  selector: 'app-scheduling',
  imports: [ReactiveFormsModule, RouterLink, Autopilot, Repair],
  templateUrl: './scheduling.html',
})
export class Scheduling {
  readonly auth = inject(AuthService);
  readonly id = inject(ActivatedRoute).snapshot.paramMap.get('id')!;
  private api = inject(SchedulingApi);
  private tournaments = inject(TournamentApi);
  private destroy = inject(DestroyRef);
  private fb = inject(FormBuilder);
  readonly browserZone = Intl.DateTimeFormat().resolvedOptions().timeZone;
  readonly name = signal('');
  readonly zone = signal('UTC');
  readonly loading = signal(true);
  readonly busy = signal(false);
  readonly error = signal('');
  readonly message = signal('');
  readonly venues = signal<Venue[]>([]);
  readonly areas = signal<Area[]>([]);
  readonly referees = signal<Referee[]>([]);
  readonly teams = signal<Registration[]>([]);
  readonly matches = signal<ScheduledMatch[]>([]);
  readonly windows = signal<Availability[]>([]);
  readonly windowsLoading = signal(false);
  readonly editingWindow = signal<string | null>(null);
  readonly editingMatch = signal<ScheduledMatch | null>(null);
  readonly kind = signal<ResourceKind>('teams');
  readonly resource = signal('');
  readonly canManageTournament = computed(() => this.auth.canManageTournament(this.id));
  readonly canEditAvailability = computed(
    () =>
      this.canManageTournament() ||
      (this.kind() === 'teams' && this.auth.canManageTeam(this.resource())),
  );
  readonly options = computed(() =>
    this.kind() === 'teams'
      ? this.teams()
          .filter((t) => this.canManageTournament() || this.auth.canManageTeam(t.teamId))
          .map((t) => ({ id: t.teamId, name: t.teamName }))
      : this.kind() === 'playing-areas'
        ? this.areas().map((a) => ({
            ...a,
            name: `${this.venues().find((v) => v.id === a.venueId)?.name} / ${a.name}`,
          }))
        : this.referees(),
  );
  readonly unscheduled = computed(() => this.matches().filter((m) => !m.startsAt));
  readonly groups = computed(() => {
    const groups = new Map<string, ScheduledMatch[]>();
    for (const m of this.matches()
      .filter((m) => m.startsAt)
      .sort((a, b) => Date.parse(a.startsAt!) - Date.parse(b.startsAt!))) {
      const date = new Intl.DateTimeFormat('pt-BR', {
        timeZone: this.zone(),
        dateStyle: 'full',
      }).format(new Date(m.startsAt!));
      groups.set(date, [...(groups.get(date) ?? []), m]);
    }
    return [...groups].map(([date, matches]) => ({ date, matches }));
  });
  readonly settingsForm = this.fb.nonNullable.group({
    matchDurationMinutes: [
      60,
      [Validators.required, Validators.min(5), Validators.max(480), Validators.pattern(/^\d+$/)],
    ],
    timeZoneId: ['UTC', trimmedRequired],
  });
  readonly venueForm = this.fb.nonNullable.group({
    name: ['', [trimmedRequired, Validators.maxLength(120)]],
    address: ['', [trimmedRequired, Validators.maxLength(300)]],
  });
  readonly areaForm = this.fb.nonNullable.group({
    venueId: ['', Validators.required],
    name: ['', [trimmedRequired, Validators.maxLength(120)]],
  });
  readonly refereeForm = this.fb.nonNullable.group({
    name: ['', [trimmedRequired, Validators.maxLength(120)]],
  });
  readonly windowForm = this.fb.nonNullable.group({
    startsAt: ['', Validators.required],
    endsAt: ['', Validators.required],
  });
  readonly matchForm = this.fb.nonNullable.group({
    startsAt: ['', Validators.required],
    playingAreaId: ['', Validators.required],
    refereeId: [''],
  });
  constructor() {
    this.load();
  }
  load() {
    this.loading.set(true);
    this.error.set('');
    forkJoin({
      tournament: this.tournaments.getTournament(this.id),
      teams: this.tournaments.listRegistrations(this.id),
      settings: this.api.settings(this.id),
      venues: this.api.venues(this.id),
      areas: this.api.areas(this.id),
      referees: this.api.referees(this.id),
      matches: this.api.schedule(this.id),
    })
      .pipe(
        takeUntilDestroyed(this.destroy),
        finalize(() => this.loading.set(false)),
      )
      .subscribe({
        next: (d) => {
          this.name.set(d.tournament.name);
          this.teams.set(d.teams);
          this.zone.set(d.settings.timeZoneId);
          this.settingsForm.reset(d.settings);
          this.venues.set(d.venues);
          this.areas.set(d.areas);
          this.referees.set(d.referees);
          this.matches.set(d.matches);
        },
        error: (e) => this.error.set(apiError(e)),
      });
  }
  private save<T>(request: Observable<T>, done: (value: T) => void) {
    if (this.busy()) return;
    this.busy.set(true);
    this.error.set('');
    this.message.set('');
    request
      .pipe(
        takeUntilDestroyed(this.destroy),
        finalize(() => this.busy.set(false)),
      )
      .subscribe({
        next: (value) => {
          done(value);
          this.message.set('Alteração salva.');
        },
        error: (e) => this.error.set(apiError(e)),
      });
  }
  saveSettings() {
    this.settingsForm.markAllAsTouched();
    if (this.settingsForm.invalid) return;
    this.save(this.api.saveSettings(this.id, this.settingsForm.getRawValue()), (v) =>
      this.zone.set(v.timeZoneId),
    );
  }
  createVenue() {
    this.venueForm.markAllAsTouched();
    if (this.venueForm.invalid) return;
    this.save(this.api.createVenue(this.id, this.venueForm.getRawValue()), (v) => {
      this.venues.update((rows) => [...rows, v]);
      this.venueForm.reset();
    });
  }
  createArea() {
    this.areaForm.markAllAsTouched();
    if (this.areaForm.invalid) return;
    const v = this.areaForm.getRawValue();
    this.save(this.api.createArea(this.id, v.venueId, v.name), (a) => {
      this.areas.update((rows) => [...rows, a]);
      this.areaForm.controls.name.reset();
    });
  }
  createReferee() {
    this.refereeForm.markAllAsTouched();
    if (this.refereeForm.invalid) return;
    this.save(this.api.createReferee(this.id, this.refereeForm.getRawValue().name), (r) => {
      this.referees.update((rows) => [...rows, r]);
      this.refereeForm.reset();
    });
  }
  chooseKind(value: string) {
    this.kind.set(value as ResourceKind);
    this.chooseResource('');
  }
  chooseResource(id: string) {
    this.resource.set(id);
    this.editingWindow.set(null);
    this.windowForm.reset();
    this.windows.set([]);
    if (id) this.loadWindows();
  }
  loadWindows() {
    const id = this.resource(),
      kind = this.kind();
    this.windowsLoading.set(true);
    this.error.set('');
    this.api
      .availability(this.id, kind, id)
      .pipe(takeUntilDestroyed(this.destroy))
      .subscribe({
        next: (windows) => {
          if (id === this.resource() && kind === this.kind()) {
            this.windows.set(windows);
            this.windowsLoading.set(false);
          }
        },
        error: (e) => {
          if (id === this.resource() && kind === this.kind()) {
            this.error.set(apiError(e));
            this.windowsLoading.set(false);
          }
        },
      });
  }
  local(value: string) {
    const d = new Date(value);
    return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}T${String(d.getHours()).padStart(2, '0')}:${String(d.getMinutes()).padStart(2, '0')}`;
  }
  private instant(value: string) {
    const d = new Date(value);
    if (!Number.isFinite(d.getTime()) || this.local(d.toISOString()) !== value)
      throw new Error('Horário local inválido neste fuso.');
    return d.toISOString();
  }
  editWindow(w: Availability) {
    this.editingWindow.set(w.id);
    this.windowForm.setValue({ startsAt: this.local(w.startsAt), endsAt: this.local(w.endsAt) });
  }
  saveWindow() {
    this.windowForm.markAllAsTouched();
    if (this.windowForm.invalid || !this.resource()) return;
    try {
      const v = this.windowForm.getRawValue();
      const data = { startsAt: this.instant(v.startsAt), endsAt: this.instant(v.endsAt) };
      this.save(
        this.api.saveAvailability(
          this.id,
          this.kind(),
          this.resource(),
          this.editingWindow(),
          data,
        ),
        () => {
          this.editingWindow.set(null);
          this.windowForm.reset();
          this.loadWindows();
        },
      );
    } catch (e) {
      this.error.set((e as Error).message);
    }
  }
  deleteWindow(w: Availability) {
    this.save(this.api.deleteAvailability(this.id, this.kind(), this.resource(), w.id), () => {
      this.windows.update((rows) => rows.filter((x) => x.id !== w.id));
      if (this.editingWindow() === w.id) {
        this.editingWindow.set(null);
        this.windowForm.reset();
      }
    });
  }
  editMatch(m: ScheduledMatch) {
    this.editingMatch.set(m);
    this.error.set('');
    this.matchForm.reset({
      startsAt: m.startsAt ? this.local(m.startsAt) : '',
      playingAreaId: m.playingAreaId ?? '',
      refereeId: m.refereeId ?? '',
    });
  }
  saveMatch() {
    this.matchForm.markAllAsTouched();
    const m = this.editingMatch();
    if (!m || this.matchForm.invalid) return;
    try {
      const v = this.matchForm.getRawValue();
      this.save(
        this.api.saveSchedule(m.matchId, {
          startsAt: this.instant(v.startsAt),
          playingAreaId: v.playingAreaId,
          refereeId: v.refereeId || null,
        }),
        (saved) => {
          this.matches.update((rows) => rows.map((x) => (x.matchId === saved.matchId ? saved : x)));
          this.editingMatch.set(null);
        },
      );
    } catch (e) {
      this.error.set((e as Error).message);
    }
  }
  display(value: string) {
    return new Intl.DateTimeFormat('pt-BR', {
      timeZone: this.zone(),
      dateStyle: 'short',
      timeStyle: 'short',
    }).format(new Date(value));
  }
}
