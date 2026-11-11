import { HttpErrorResponse } from '@angular/common/http';
import { apiError } from './api-error';

describe('Erros da API', () => {
  it('mostra mensagens de validação retornadas em ProblemDetails', () => {
    const error = new HttpErrorResponse({
      status: 400,
      error: { errors: { Name: ['Name is required.'], Sport: ['Sport is required.'] } },
    });
    expect(apiError(error)).toBe('Name is required. Sport is required.');
  });
  it('diferencia erro de conexão de erro de negócio', () => {
    expect(apiError(new HttpErrorResponse({ status: 0 }))).toContain('conectar ao servidor');
    expect(apiError(new HttpErrorResponse({ status: 403 }))).toContain('não tem permissão');
  });
});
