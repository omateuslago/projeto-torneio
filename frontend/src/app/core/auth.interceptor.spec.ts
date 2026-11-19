import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { authInterceptor } from './auth.interceptor';

describe('authInterceptor', () => {
  const router = { url: '/agenda', navigate: vi.fn() };
  let http: HttpClient;
  let controller: HttpTestingController;

  beforeEach(() => {
    router.navigate.mockReset();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
        { provide: Router, useValue: router },
      ],
    });
    http = TestBed.inject(HttpClient);
    controller = TestBed.inject(HttpTestingController);
  });

  afterEach(() => controller.verify());

  it('redireciona 401 de operação protegida para login', () => {
    http.post('/api/teams', {}).subscribe({ error: () => undefined });
    controller.expectOne('/api/teams').flush({}, { status: 401, statusText: 'Unauthorized' });
    expect(router.navigate).toHaveBeenCalledWith(['/login'], {
      queryParams: { returnUrl: '/agenda' },
    });
  });

  it('preserva a sessão e entrega 403 ao componente', () => {
    let status = 0;
    http.post('/api/teams', {}).subscribe({ error: (error) => (status = error.status) });
    controller.expectOne('/api/teams').flush({}, { status: 403, statusText: 'Forbidden' });
    expect(status).toBe(403);
    expect(router.navigate).not.toHaveBeenCalled();
  });
});
