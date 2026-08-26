#!/usr/bin/env node
// Renders docs/diagrams/*.mmd to vector PDF for inclusion in the LaTeX papers.
//
//   node scripts/render-diagrams.mjs            render every diagram
//   node scripts/render-diagrams.mjs a4 d2      render only matching slugs
//
// Why this exists: docs/DIAGRAMS.md renders on GitHub because GitHub speaks
// Mermaid. A PDF does not. Rather than redraw the same pictures a second time in
// TikZ — two sources of truth for one diagram, guaranteed to drift — the papers
// include these rendered from the SAME .mmd files that check-diagrams.mjs already
// keeps identical to docs/DIAGRAMS.md. One source, two output formats.
//
// Output goes to docs/diagrams/rendered/ and is NOT committed: it is build
// output, exactly like the PDFs themselves (see .gitignore, and the
// "PDFs are build output" rule in architecture-standards'
// docs/research/00-RESEARCH-DOCUMENTATION.md).
//
// Requires @mermaid-js/mermaid-cli, which drives a headless Chromium. It is a
// pinned devDependency, so `npm ci` provides it and Dependabot tracks it — the
// same reasoning package.json gives for the lint tool, "a pinned version instead
// of an npx invocation nobody updates". This script resolves the LOCAL bin and
// never the registry: plain `npx mmdc` used to reach past a fresh clone's empty
// node_modules and resolve a squatter package literally named `mmdc`. If a
// browser is present at PUPPETEER_EXECUTABLE_PATH or CHROME_BIN it is used,
// otherwise mermaid-cli's own bundled download is left to resolve itself.

import { mkdirSync, readdirSync, writeFileSync, rmSync, existsSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { spawnSync } from 'node:child_process';

const ROOT = dirname(dirname(fileURLToPath(import.meta.url)));
const SRC = join(ROOT, 'docs', 'diagrams');
const OUT = join(SRC, 'rendered');

// The local bin, not `npx mmdc`. A fresh clone that has not run `npm ci` gets an
// actionable message here rather than npm silently fetching the `mmdc` squatter
// (a dummy package whose only content is a floating dependency on the real one)
// and failing with "could not determine executable to run".
const MMDC = join(ROOT, 'node_modules', '.bin', process.platform === 'win32' ? 'mmdc.cmd' : 'mmdc');

if (!existsSync(MMDC)) {
  console.error(
    'render-diagrams: @mermaid-js/mermaid-cli is not installed.\n' +
    "  It is a pinned devDependency — run `npm ci` (or `npm install`) first.",
  );
  process.exit(1);
}

const filters = process.argv.slice(2);

const sources = readdirSync(SRC)
  .filter(f => f.endsWith('.mmd'))
  .filter(f => filters.length === 0 || filters.some(k => f.includes(k)))
  .sort();

if (sources.length === 0) {
  console.error(
    filters.length
      ? `render-diagrams: no .mmd file matched ${filters.join(', ')}`
      : 'render-diagrams: no .mmd files found under docs/diagrams/',
  );
  process.exit(1);
}

mkdirSync(OUT, { recursive: true });

// A puppeteer config is only written when we actually have a browser to point at.
// --no-sandbox is required because CI containers and this repo's dev containers
// both run as root, where Chromium's sandbox refuses to start.
const browser = process.env.PUPPETEER_EXECUTABLE_PATH || process.env.CHROME_BIN || '';
const puppeteerConfig = join(OUT, '.puppeteer.json');
writeFileSync(
  puppeteerConfig,
  JSON.stringify({
    ...(browser ? { executablePath: browser } : {}),
    args: ['--no-sandbox', '--disable-setuid-sandbox', '--disable-dev-shm-usage'],
  }),
);

let failed = 0;
for (const file of sources) {
  const slug = file.replace(/\.mmd$/, '');
  const result = spawnSync(
    MMDC,
    [
      '-i', join(SRC, file),
      '-o', join(OUT, `${slug}.pdf`),
      '-p', puppeteerConfig,
      '--pdfFit',
      '-b', 'transparent',
    ],
    { cwd: ROOT, stdio: 'pipe', encoding: 'utf8' },
  );

  if (result.status === 0 && existsSync(join(OUT, `${slug}.pdf`))) {
    console.log(`  ok    ${slug}.pdf`);
  } else {
    failed += 1;
    console.error(`  FAIL  ${slug}`);
    // mermaid-cli reports the useful part on stderr; surface it rather than
    // leaving a bare exit code, because the usual cause (a missing browser) is
    // fixable only if you can read what it said.
    const detail = (result.stderr || result.stdout || '').trim();
    if (detail) console.error(detail.split('\n').map(l => `        ${l}`).join('\n'));
  }
}

rmSync(puppeteerConfig, { force: true });

console.log(
  `render-diagrams: ${sources.length - failed}/${sources.length} rendered into docs/diagrams/rendered/`,
);
process.exit(failed === 0 ? 0 : 1);
