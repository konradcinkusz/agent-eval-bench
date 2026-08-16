#!/usr/bin/env node
/**
 * check-doc-parity.mjs — the bilingual documents come in pairs, and neither half
 * may be edited without the other.
 *
 * Why this exists:
 *
 *   Most of this repository is English only. A small, named set of documents —
 *   the front door, the tutorials and the how-to guides — is deliberately
 *   bilingual, because the person maintaining it and the people reviewing it do
 *   not read the same language.
 *
 *   Two copies of a document is a drift surface, and the failure mode is
 *   specific: somebody fixes a wrong command in the English guide, the Polish
 *   guide keeps telling its reader to run the wrong thing, and nothing goes red.
 *   That is worse than having no translation, because the reader trusts it.
 *
 *   So there are two rules, and the second is the one that matters:
 *
 *     R1  STRUCTURAL — every bilingual document has both halves.
 *     R2  COUPLING   — a commit that edits one half must edit the other.
 *
 *   R2 cannot check that a translation is *correct*; no script can. It checks
 *   that somebody looked. That is the same bargain check-change-coupling.mjs
 *   makes for prompts and the spec, and the same reasoning: a convention about
 *   remembering is a convention that fails on the busy week.
 *
 * R2 only runs when a base ref is given, because it needs a diff. CI passes
 * origin/main; locally you can pass anything `git diff` understands, or omit it
 * and get R1 alone.
 *
 * Zero dependencies: this runs in the same job as check-links.mjs.
 *
 * Usage: node scripts/check-doc-parity.mjs [base-ref]
 * Exit:  0 = paired, and coupled if a base ref was given; 1 = not.
 */

import { readdirSync, existsSync, statSync } from 'node:fs';
import { execFileSync } from 'node:child_process';
import { join, relative } from 'node:path';

const repoRoot = execFileSync('git', ['rev-parse', '--show-toplevel'], { encoding: 'utf8' }).trim();
const baseRef = process.argv[2];

/**
 * Where bilingual documents live. A directory listed here is bilingual in full:
 * every `*.md` in it must have a `*.pl.md` twin. Adding a directory here is how
 * you opt a new area in.
 */
const BILINGUAL = [
  'docs/tutorials',
  'docs/how-to',
];

/** Individual files that are bilingual without their whole directory being so. */
const BILINGUAL_FILES = [
  'docs/START-HERE.md',
];

const PL = '.pl.md';

function englishHalves() {
  const found = [];

  for (const dir of BILINGUAL) {
    const absolute = join(repoRoot, dir);

    if (!existsSync(absolute) || !statSync(absolute).isDirectory()) {
      console.error(`check-doc-parity: '${dir}' is declared bilingual but does not exist.`);
      process.exit(1);
    }

    for (const name of readdirSync(absolute)) {
      if (name.endsWith('.md') && !name.endsWith(PL)) {
        found.push(`${dir}/${name}`);
      }
    }
  }

  for (const file of BILINGUAL_FILES) {
    if (!existsSync(join(repoRoot, file))) {
      console.error(`check-doc-parity: '${file}' is declared bilingual but does not exist.`);
      process.exit(1);
    }

    found.push(file);
  }

  return found.sort();
}

/** `docs/how-to/x.md` → `docs/how-to/x.pl.md` */
const polishOf = (english) => `${english.slice(0, -'.md'.length)}${PL}`;

const failures = [];
const english = englishHalves();

if (english.length === 0) {
  console.error('check-doc-parity: no bilingual documents found. That cannot be right — failing loudly.');
  process.exit(1);
}

// ---- R1: both halves exist -------------------------------------------------

for (const file of english) {
  if (!existsSync(join(repoRoot, polishOf(file)))) {
    failures.push(`${file} has no Polish half. Expected ${polishOf(file)}.`);
  }
}

// A stray `.pl.md` with no English original is the same defect, mirrored.
for (const dir of BILINGUAL) {
  for (const name of readdirSync(join(repoRoot, dir))) {
    if (!name.endsWith(PL)) {
      continue;
    }

    const englishName = `${name.slice(0, -PL.length)}.md`;

    if (!existsSync(join(repoRoot, dir, englishName))) {
      failures.push(`${dir}/${name} has no English original. Expected ${dir}/${englishName}.`);
    }
  }
}

// ---- R2: neither half moves alone ------------------------------------------

if (baseRef) {
  let changed;

  try {
    changed = new Set(
      execFileSync('git', ['diff', '--name-only', `${baseRef}...HEAD`], { encoding: 'utf8', cwd: repoRoot })
        .split('\n')
        .map((line) => line.trim())
        .filter(Boolean),
    );
  } catch {
    // No merge base — a fork with no shared history, or a shallow clone that
    // does not reach it. Structural parity still held, and reporting a coupling
    // failure nobody can act on would train people to ignore this check.
    console.log('check-doc-parity: no merge base with ' + baseRef + ' — structural parity only.');
    changed = null;
  }

  if (changed) {
    for (const file of english) {
      const polish = polishOf(file);
      const movedEnglish = changed.has(file);
      const movedPolish = changed.has(polish);

      if (movedEnglish && !movedPolish) {
        failures.push(`${file} changed but ${polish} did not. Update the translation, or say in the pull request why it did not need it.`);
      }

      if (movedPolish && !movedEnglish) {
        failures.push(`${polish} changed but ${file} did not. Update the English, or say in the pull request why it did not need it.`);
      }
    }
  }
}

if (failures.length > 0) {
  console.error('check-doc-parity: FAILED\n');

  for (const failure of failures) {
    console.error(`  - ${failure}`);
  }

  console.error('\nA translation that drifts is worse than no translation: the reader trusts it.');
  process.exit(1);
}

console.log(
  `check-doc-parity: ${english.length} bilingual document(s), both halves present`
  + `${baseRef ? ' and neither edited alone' : ''}.`,
);
