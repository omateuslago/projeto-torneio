import { APIRequestContext, Page, expect } from '@playwright/test';

const api = 'http://localhost:5094/api';

export async function registerOrganizer(request: APIRequestContext, page?: Page) {
  const email = `e2e-${Date.now()}-${Math.random().toString(16).slice(2)}@brakvyo.local`;
  let response = await request.get(`${api}/auth/csrf`);
  expect(response.ok()).toBe(true);
  let token = (await response.json()).token as string;
  response = await request.post(`${api}/auth/register`, {
    headers: xsrf(token),
    data: { email, password: 'E2e-password-2026', displayName: 'E2E Organizer' },
  });
  expect(response.ok()).toBe(true);
  response = await request.get(`${api}/auth/csrf`);
  token = (await response.json()).token as string;
  if (page) await page.context().addCookies((await request.storageState()).cookies);
  return token;
}

export function xsrf(token: string) {
  return { 'X-XSRF-TOKEN': token };
}
