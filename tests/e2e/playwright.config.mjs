// The browser suite's harness.
//
// It starts the REAL service — the same binary the demo deploys, `dotnet run`,
// no test doubles — and drives the one page a visitor sees. Configuration is
// environment variables, exactly as production sets them, with three deliberate
// differences a test needs and a deployment must never have:
//
//   WorkforceTools__Fixture=e2e-showcase   the world with the hostile leave
//                                          type name in it (see that file)
//   Agent__PinnedUtcNow                    "today and tomorrow" typed on a
//                                          Saturday resolves onto a weekend,
//                                          and a suite green Monday-to-Friday
//                                          is not a suite
//   small Demo__* ceilings                 so the 429 paths are reachable in
//                                          seconds rather than after a minute
//                                          of hammering
//
// NO BROWSER DOWNLOAD. CI images and dev containers already carry a Chromium;
// the resolver below finds it rather than fetching another one (issue #13's
// explicit instruction). Set CHROMIUM_EXECUTABLE to override.

import { defineConfig } from '@playwright/test';
import { existsSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

const here = dirname(fileURLToPath(import.meta.url));
const repoRoot = join(here, '..', '..');

function chromium() {
  const candidates = [
    process.env.CHROMIUM_EXECUTABLE,
    '/opt/pw-browsers/chromium',
    '/usr/bin/google-chrome',
    '/usr/bin/chromium-browser',
    '/usr/bin/chromium',
  ].filter(Boolean);

  for (const candidate of candidates) {
    if (existsSync(candidate)) return candidate;
  }

  // Let Playwright resolve its own managed browser as the last resort — a dev
  // machine that has run `npx playwright install` at some point.
  return undefined;
}

const port = Number(process.env.E2E_PORT ?? 5233);
const baseURL = `http://127.0.0.1:${port}`;

export default defineConfig({
  testDir: here,
  outputDir: join(here, '.artifacts'),

  // One worker, tests in file order. The service holds real state — the mock
  // world accumulates approved bookings, and the last test deliberately spends
  // the rate limit, which would poison anything that ran after it in parallel.
  workers: 1,
  fullyParallel: false,

  // A failed e2e re-run to green is the false regression net TESTING-STRATEGY.md
  // §6 names. No retries, same as the eval harness.
  retries: 0,

  timeout: 30_000,
  expect: { timeout: 5_000 },

  reporter: process.env.CI ? [['list'], ['github']] : [['list']],

  use: {
    baseURL,
    launchOptions: { executablePath: chromium() },
    trace: 'retain-on-failure',
  },

  webServer: {
    command: [
      'dotnet run',
      '--project', join(repoRoot, 'src', 'AbsenceConcierge.AgentService'),
      '--configuration', 'Release',
      '--no-build',
      // Without this, `dotnet run` applies Properties/launchSettings.json's
      // "AbsenceConcierge.AgentService" profile — ASPNETCORE_ENVIRONMENT=Development
      // and applicationUrl https://localhost:62378;http://localhost:62379 — over
      // the env block below. The server comes up fine on those ports; Playwright
      // polls baseURL (127.0.0.1:E2E_PORT) and never sees it, so every run times
      // out at 120s with a server that was never actually broken.
      '--no-launch-profile',
    ].join(' '),
    url: `${baseURL}/health`,
    reuseExistingServer: false,
    timeout: 120_000,

    // Piped rather than the default ignore/pipe split: when the server never
    // reaches /health, "Timed out waiting 120000ms from config.webServer" is the
    // whole error otherwise — whatever the process printed on its way to not
    // starting (a bind failure, a config exception, a crash) is exactly what a
    // timeout does not tell you.
    stdout: 'pipe',
    stderr: 'pipe',

    env: {
      ASPNETCORE_ENVIRONMENT: 'Production',
      ASPNETCORE_URLS: baseURL,
      WorkforceTools__Mode: 'Mock',
      WorkforceTools__Fixture: 'e2e-showcase',
      Agent__Timezone: 'Europe/Madrid',

      // The Tuesday hap-001 pins, for the same reason hap-001 pins it: two
      // ordinary working days, no weekend or holiday interactions.
      Agent__PinnedUtcNow: '2026-08-11T09:15:00+02:00',

      // Reachable ceilings. The turn cap is exercised by one test with its own
      // conversation; the per-minute window is spent by the LAST test, so its
      // number only needs to be small enough to cross in a burst — and large
      // enough that the rest of the suite cannot cross it by accident. The
      // ordinary tests spend ~28 limited requests worst-case; 30 left a
      // two-request margin before an unrelated test started failing with a
      // 429 it never asserts, and 40 is still well inside the burst's 60.
      Demo__MaxTurnsPerConversation: '8',
      Demo__RequestsPerMinutePerClient: '40',
    },
  },
});
