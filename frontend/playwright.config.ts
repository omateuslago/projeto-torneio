import { defineConfig } from '@playwright/test';

export default defineConfig({
  testDir: './e2e',
  testMatch: '**/*.e2e.ts',
  fullyParallel: false,
  workers: 1,
  use: {
    baseURL: 'http://localhost:4200',
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
    launchOptions: { executablePath: process.env['CHROMIUM_PATH'] || undefined },
  },
  webServer: {
    command: 'npm start -- --host 127.0.0.1',
    url: 'http://localhost:4200',
    reuseExistingServer: !process.env['CI'],
    timeout: 60_000,
  },
});
