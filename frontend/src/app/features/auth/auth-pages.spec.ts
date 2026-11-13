import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { ActivatedRoute, Router } from '@angular/router';
import { of, throwError } from 'rxjs';
import { AuthService } from '../../core/auth.service';
import { Login } from './login';
import { Register } from './register';

describe('páginas de autenticação', () => {
  const auth = { login: vi.fn(), register: vi.fn() };
  const router = { navigate: vi.fn(), navigateByUrl: vi.fn() };

  beforeEach(() => {
    auth.login.mockReset();
    auth.register.mockReset();
    router.navigate.mockReset();
    router.navigateByUrl.mockReset();
    TestBed.configureTestingModule({
      providers: [
        { provide: AuthService, useValue: auth },
        { provide: Router, useValue: router },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { queryParamMap: { get: () => '/agenda' } } },
        },
      ],
    });
  });

  it('envia login com rememberMe e respeita returnUrl', () => {
    auth.login.mockReturnValue(of({}));
    const fixture = TestBed.createComponent(Login);
    fixture.componentInstance.form.setValue({
      email: 'mateus@example.com',
      password: 'password',
      rememberMe: true,
    });
    fixture.componentInstance.submit();
    expect(auth.login).toHaveBeenCalledWith('mateus@example.com', 'password', true);
    expect(router.navigateByUrl).toHaveBeenCalledWith('/agenda');
  });

  it('bloqueia cadastro quando a confirmação diverge', () => {
    const fixture = TestBed.createComponent(Register);
    fixture.componentInstance.form.setValue({
      displayName: 'Mateus',
      email: 'mateus@example.com',
      password: 'password',
      confirmation: 'different',
    });
    fixture.componentInstance.submit();
    fixture.detectChanges();
    expect(auth.register).not.toHaveBeenCalled();
    expect(fixture.nativeElement.textContent).toContain('As senhas não coincidem');
  });

  it('cadastra usuário comum e exibe falha da API', () => {
    auth.register.mockReturnValueOnce(of({}));
    const success = TestBed.createComponent(Register);
    fillRegistration(success);
    success.componentInstance.submit();
    expect(auth.register).toHaveBeenCalledWith('mateus@example.com', 'password', 'Mateus');
    expect(router.navigate).toHaveBeenCalledWith(['/campeonatos']);

    auth.register.mockReturnValueOnce(
      throwError(
        () =>
          new HttpErrorResponse({
            error: { detail: 'Email já cadastrado.' },
            status: 400,
            statusText: 'Bad Request',
          }),
      ),
    );
    const failure = TestBed.createComponent(Register);
    fillRegistration(failure);
    failure.componentInstance.submit();
    failure.detectChanges();
    expect(failure.nativeElement.textContent).toContain('Email já cadastrado');
  });

  function fillRegistration(fixture: ComponentFixture<Register>) {
    fixture.componentInstance.form.setValue({
      displayName: ' Mateus ',
      email: 'mateus@example.com',
      password: 'password',
      confirmation: 'password',
    });
  }
});
