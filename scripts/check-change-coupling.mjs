#!/usr/bin/env node
//
// Two rules the specification states and nothing enforced until now.
//
//  R1  A change to `agents/` or `prompts/` must come with a change to
//      docs/SPEC.md.
//
//      SPEC §10: "A pull request that edits `prompts/` or `agents/` without
//      touching this document is a pull request whose behaviour change nobody
//      wrote down." A prompt edit is a behaviour change with no code diff, and
//      the reason this is a check rather than a convention is that a convention
//      about remembering is a convention that fails on the busy week.
//
//  R2  A change to `evals/fixtures/` or `evals/rubrics/` must come with a suite
//      version bump.
//
//      SPEC §8.4: editing a fixture changes what the baseline measured without
//      changing a single scenario file — a measuring stick that changed length
//      between readings. §5 says the same about the judge's rubrics and prompt.
//      The version lives in agents/absence-concierge/definition.json and moves
//      with the spec version, so the bump is what the Layer 1 baseline check
//      then forces a re-record against.
//
// Usage: node scripts/check-change-coupling.mjs <base-ref>
//
// Needs full history for the base ref — `actions/checkout` with fetch-depth 0.

import { execFileSync } from 'node:child_process';

const DEFINITION = 'agents/absence-concierge/definition.json';

const base = process.argv[2];

if (!base) {
  console.error('usage: check-change-coupling.mjs <base-ref>');
  process.exit(2);
}

function git(...args) {
  // stderr is discarded on purpose. `git show base:file` for a file that did not
  // exist on the base prints a `fatal:` line, which is an expected answer here —
  // and a scary word in a green job is how people learn to stop reading output.
  return execFileSync('git', args, { encoding: 'utf8', stdio: ['ignore', 'pipe', 'ignore'] });
}

let changed;

try {
  // Three dots: what this branch changed, not what the base branch changed
  // underneath it. Two dots would blame this pull request for every commit that
  // landed on main since it was cut.
  changed = git('diff', '--name-only', `${base}...HEAD`).split('\n').filter(Boolean);
} catch (error) {
  console.error(`check-change-coupling: could not diff against '${base}'.`);
  console.error('The job needs the base branch fetched — actions/checkout with fetch-depth: 0.');
  console.error(String(error.message ?? error));
  process.exit(2);
}

if (changed.length === 0) {
  console.log('check-change-coupling: nothing changed against the base.');
  process.exit(0);
}

const touched = (prefix) => changed.filter((file) => file.startsWith(prefix));

const failures = [];

// ── R1 ──────────────────────────────────────────────────────────────────────

const behaviourAsCode = [...touched('agents/'), ...touched('prompts/')];
const specChanged = changed.includes('docs/SPEC.md');

if (behaviourAsCode.length > 0 && !specChanged) {
  failures.push(
    [
      'A behaviour change nobody wrote down.',
      '',
      `  Changed: ${behaviourAsCode.join(', ')}`,
      '  Unchanged: docs/SPEC.md',
      '',
      '  SPEC §10: a behaviour change starts in the specification, lands with its',
      '  scenarios in the same pull request, and moves the version. A prompt or an',
      '  agent definition is behaviour with no code diff — which is exactly why it',
      '  is the edit that regresses an agent without anything going red.',
      '',
      '  Two ways out, and both are fine: amend docs/SPEC.md (its changelog table',
      '  counts), or move the change somewhere that is not behaviour.',
    ].join('\n'),
  );
}

// ── R2 ──────────────────────────────────────────────────────────────────────

const measuringStick = [...touched('evals/fixtures/'), ...touched('evals/rubrics/')];

if (measuringStick.length > 0) {
  const versionNow = readVersion(() => git('show', `HEAD:${DEFINITION}`));
  const versionBefore = readVersion(() => git('show', `${base}:${DEFINITION}`));

  if (versionNow !== null && versionBefore !== null && versionNow === versionBefore) {
    failures.push(
      [
        'The measuring stick changed and the version did not.',
        '',
        `  Changed: ${measuringStick.join(', ')}`,
        `  Version: still ${versionNow}`,
        '',
        '  SPEC §8.4: editing a fixture changes what the baseline measured without',
        '  changing a single scenario file. AI-EVALS.md §5 says the same about the',
        '  judge\'s rubrics and prompt. Comparing a score across that edit is a',
        '  measuring stick that changed length between readings.',
        '',
        `  Bump "version" in ${DEFINITION} (and docs/SPEC.md with it), then`,
        '  re-record evals/baselines/layer1.json in this same pull request.',
      ].join('\n'),
    );
  }
}

function readVersion(read) {
  try {
    return JSON.parse(read()).version ?? null;
  } catch {
    // A definition that does not exist on one side of the diff is not a coupling
    // failure — it is a repository that did not have one yet.
    return null;
  }
}

// ── Report ──────────────────────────────────────────────────────────────────

if (failures.length === 0) {
  console.log(`check-change-coupling: ${changed.length} file(s) changed, both rules satisfied.`);
  process.exit(0);
}

for (const failure of failures) {
  console.error('');
  console.error(failure);
}

console.error('');
process.exit(1);
