#!/usr/bin/env node
/**
 * validate-scenarios.mjs — the Phase 1 gate.
 *
 * The eval harness does not exist until Phase 4. This script is what stops the
 * scenario corpus being, for three phases, a directory of YAML that no CI
 * context executes:
 *
 *   "An unreferenced test config is not a latent capability; it is
 *    documentation that lies."  — TESTING-STRATEGY.md §9
 *
 * It checks three things, in increasing order of interest:
 *
 *   1. Every scenario validates against evals/schema/scenario.schema.json.
 *      The schema is strict (`additionalProperties: false`), so a mistyped
 *      assertion key is an error rather than a silently ignored line.
 *
 *   2. Corpus-level invariants a per-file schema cannot express: unique ids,
 *      ids matching filenames and class directories, fixtures that exist.
 *
 *   3. ASSERTION DISCIPLINE, enforced mechanically rather than by review —
 *      the rules this repository exists to demonstrate. Chiefly: every denied
 *      and adversarial scenario must carry an ABSENCE assertion. Asserting
 *      that the agent refused, without asserting that the forbidden call did
 *      not happen, is half a test; an agent that refuses politely and calls
 *      the tool anyway passes it.
 *
 * And one rule that outranks all of them, from AZURE-AI-FOUNDRY-AGENTS.md §6's
 * provisioner: a run that found nothing must not exit 0. A validator that
 * silently passes an empty corpus is the same failure it is here to prevent.
 *
 * Usage: node scripts/validate-scenarios.mjs
 * Exit:  0 = corpus valid; 1 = at least one problem.
 */

import { readFileSync, existsSync, readdirSync, statSync } from 'node:fs';
import { join, basename, relative, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import Ajv2020 from 'ajv/dist/2020.js';
import addFormats from 'ajv-formats';
import { parse as parseYaml } from 'yaml';

const REPO_ROOT = join(dirname(fileURLToPath(import.meta.url)), '..');
const SCENARIO_DIR = join(REPO_ROOT, 'evals', 'scenarios');
const FIXTURE_DIR = join(REPO_ROOT, 'evals', 'fixtures');
const SCHEMA_PATH = join(REPO_ROOT, 'evals', 'schema', 'scenario.schema.json');

/**
 * Rubric ids the spec defines (docs/SPEC.md §5). Until Phase 5 creates
 * evals/rubrics/, this list is the authority; from Phase 5 the directory is,
 * and this constant is deleted rather than left to drift beside it.
 */
/**
 * The prefix ScenarioExtractor writes into a freshly extracted scenario's `title`
 * and `why`. Kept byte-identical to ScenarioExtractor.ReviewMarker — two places
 * depend on the exact string, and a rule enforced by a literal in one of them is a
 * rule that stops being enforced the day the other is reworded.
 */
const REVIEW_MARKER = 'REVIEW:';

const KNOWN_RUBRICS = new Set([
  'grounding',
  'confirmation-clarity',
  'refusal-clarity',
  'degradation-honesty',
  'tone',
]);

/** Class prefix → directory, and the id prefix each class must use. */
const CLASS_PREFIX = {
  happy: 'hap',
  ambiguity: 'amb',
  denied: 'den',
  adversarial: 'adv',
  degradation: 'deg',
};

/** Classes where an absence assertion is mandatory, and why. */
const ABSENCE_REQUIRED = {
  denied:
    'a refusal asserted without asserting the absence of the attempted call is half a test',
  adversarial:
    'an injection scenario that does not assert what did NOT happen is testing the reply, not the constraint',
};

const ABSENCE_ASSERTIONS = new Set(['tool_not_called', 'event_not_emitted']);

const problems = [];
const fail = (file, message) => problems.push({ file, message });

// ---------------------------------------------------------------------------
// Collect
// ---------------------------------------------------------------------------
function walk(dir) {
  if (!existsSync(dir)) return [];
  return readdirSync(dir).flatMap((entry) => {
    const full = join(dir, entry);
    if (statSync(full).isDirectory()) return walk(full);
    return /\.ya?ml$/.test(entry) ? [full] : [];
  });
}

const files = walk(SCENARIO_DIR).sort();

if (files.length === 0) {
  console.error('validate-scenarios: no scenario files found under evals/scenarios/.');
  console.error('A validator that passes an empty corpus is the failure it exists to prevent.');
  process.exit(1);
}

// ---------------------------------------------------------------------------
// Schema
// ---------------------------------------------------------------------------
const ajv = new Ajv2020({ allErrors: true, strict: false });
addFormats(ajv);
const validate = ajv.compile(JSON.parse(readFileSync(SCHEMA_PATH, 'utf8')));

const seenIds = new Map();
const byClass = Object.fromEntries(Object.keys(CLASS_PREFIX).map((c) => [c, 0]));
let constraintCount = 0;
let skipCount = 0;

for (const file of files) {
  const rel = relative(REPO_ROOT, file);
  let doc;

  try {
    doc = parseYaml(readFileSync(file, 'utf8'));
  } catch (error) {
    fail(rel, `YAML does not parse: ${error.message}`);
    continue;
  }

  if (doc === null || typeof doc !== 'object') {
    fail(rel, 'file is empty or is not a mapping');
    continue;
  }

  // --- 1. schema ---
  if (!validate(doc)) {
    for (const error of validate.errors ?? []) {
      const where = error.instancePath || '(root)';
      let message = `${where} ${error.message}`;
      if (error.params?.additionalProperty) {
        message += ` — unknown key "${error.params.additionalProperty}". `
          + 'The schema is strict on purpose: a key it does not know is a key the harness would ignore.';
      }
      if (error.params?.allowedValues) {
        message += ` (allowed: ${error.params.allowedValues.join(', ')})`;
      }
      fail(rel, message);
    }
    // Corpus checks below assume a well-formed document.
    if (!doc.id || !doc.class) continue;
  }

  // --- 2. corpus invariants ---
  if (seenIds.has(doc.id)) {
    fail(rel, `duplicate id "${doc.id}", already used by ${seenIds.get(doc.id)}. `
      + 'Ids are how a baseline diff refers to a scenario; two scenarios sharing one makes the diff lie.');
  } else {
    seenIds.set(doc.id, rel);
  }

  const expectedFilename = `${doc.id}.yaml`;
  if (basename(file) !== expectedFilename) {
    fail(rel, `filename does not match id — expected "${expectedFilename}"`);
  }

  const dirClass = basename(dirname(file));
  if (dirClass !== doc.class) {
    fail(rel, `lives in evals/scenarios/${dirClass}/ but declares class "${doc.class}"`);
  }

  const wantPrefix = CLASS_PREFIX[doc.class];
  if (wantPrefix && !doc.id.startsWith(`${wantPrefix}-`)) {
    fail(rel, `class "${doc.class}" requires ids to start with "${wantPrefix}-"`);
  }

  if (doc.class in byClass) byClass[doc.class] += 1;
  if (doc.gate === 'constraint') constraintCount += 1;
  if (doc.skip) skipCount += 1;

  const fixtureFile = join(FIXTURE_DIR, `${doc.fixture?.base}.yaml`);
  if (doc.fixture?.base && !existsSync(fixtureFile)) {
    fail(rel, `fixture.base "${doc.fixture.base}" has no file at evals/fixtures/${doc.fixture.base}.yaml`);
  }

  for (const rubric of doc.rubrics ?? []) {
    if (!KNOWN_RUBRICS.has(rubric)) {
      fail(rel, `unknown rubric "${rubric}" — docs/SPEC.md §5 defines: ${[...KNOWN_RUBRICS].join(', ')}`);
    }
  }

  // A scenario extracted from a trace (ScenarioExtractor) arrives with these
  // markers in its `title` and `why`. Extraction records what the agent DID; a
  // scenario says what it SHOULD do, and the gap between those two sentences is a
  // human's judgement. Committing an extraction unread would enshrine the incident
  // as the expected behaviour — the exact inversion AI-EVALS.md §3 is guarding
  // against when it asks for a scenario before a fix.
  for (const [field, value] of [['title', doc.title], ['why', doc.why]]) {
    if (typeof value === 'string' && value.trimStart().startsWith(REVIEW_MARKER)) {
      fail(rel, `${field} still carries the "${REVIEW_MARKER}" marker an extracted scenario is written with. `
        + 'Replace it with why this behaviour is correct — or change the assertions, and then the agent.');
    }
  }

  // --- 3. assertion discipline ---
  const asserts = (doc.expect ?? []).map((a) => a.assert);

  const reason = ABSENCE_REQUIRED[doc.class];
  if (reason && !asserts.some((a) => ABSENCE_ASSERTIONS.has(a))) {
    fail(rel, `class "${doc.class}" requires at least one absence assertion `
      + `(${[...ABSENCE_ASSERTIONS].join(' or ')}) — ${reason}`);
  }

  if ((doc.class === 'denied' || doc.class === 'adversarial') && doc.gate !== 'constraint') {
    fail(rel, `class "${doc.class}" must be gated as "constraint" — these hard-block at 100% (AI-EVALS.md §6)`);
  }

  if (!asserts.includes('termination')) {
    fail(rel, 'no `termination` assertion. Every scenario must prove the loop ended by decision '
      + 'rather than by hitting the iteration cap (C-4)');
  }

  if (!asserts.includes('output_excludes_internal_ids')) {
    fail(rel, 'no `output_excludes_internal_ids` assertion (C-3)');
  }

  // A scenario that reaches a write must prove the gate held and the id was grounded.
  const writesAfterConfirmation = (doc.expect ?? []).some(
    (a) => (a.assert === 'tool_called' || a.assert === 'tool_called_with') && a.tool === 'request_time_off',
  );
  if (writesAfterConfirmation) {
    const hasGateOrder = (doc.expect ?? []).some(
      (a) => a.assert === 'order'
        && a.first?.event === 'confirmation.received'
        && a.then?.tool === 'request_time_off',
    );
    if (!hasGateOrder) {
      fail(rel, 'asserts a write but does not assert that `confirmation.received` preceded it. '
        + 'C-1 is the constraint this repository exists to demonstrate; a scenario that writes '
        + 'without checking the ordering is where it would go unnoticed');
    }
    const hasGrounding = (doc.expect ?? []).some(
      (a) => a.assert === 'argument_grounded' && a.arg === 'leave_type_id',
    );
    if (!hasGrounding) {
      fail(rel, 'asserts a write but does not assert `argument_grounded` for `leave_type_id` (C-5)');
    }
  }

  if (doc.skip && !doc.skip.reason) {
    fail(rel, 'skip without a reason — an unimplemented scenario says so where the runner can see it');
  }
}

// ---------------------------------------------------------------------------
// 4. The spec's citations must be true
//
// docs/SPEC.md §3 maps each behaviour to the scenarios that prove it. Those
// citations are the reason a reader believes the spec is backed by something,
// and they rot silently: a scenario gets renamed or repurposed and the row goes
// on pointing at it. E2E-ACCEPTANCE-TESTING.md §8 names this exact tell in
// bulk-authored documentation — "references to files, folders, or tools that
// don't exist in the repo".
//
// KNOWN LIMIT, stated rather than left for a reader to discover: this catches a
// citation pointing at an id that does not exist. It does NOT catch a citation
// pointing at an id that exists and tests something else. Four of those were
// found in this spec while the scenarios were being written — by humans reading
// the pairs, not by this script — and no cheap check would have caught them.
// Saying so here is the point: a green validator is evidence of what it checks,
// and of nothing more.
// ---------------------------------------------------------------------------
const SPEC_PATH = join(REPO_ROOT, 'docs', 'SPEC.md');
const CITATION = /\b(hap|amb|den|adv|deg)-\d{3}\b/g;

if (existsSync(SPEC_PATH)) {
  const spec = readFileSync(SPEC_PATH, 'utf8');
  const cited = new Set(spec.match(CITATION) ?? []);
  const shortIds = new Set([...seenIds.keys()].map((id) => id.split('-').slice(0, 2).join('-')));

  for (const citation of [...cited].sort()) {
    if (!shortIds.has(citation)) {
      fail('docs/SPEC.md', `cites scenario "${citation}", which does not exist`);
    }
  }

  // The reverse is a warning, not an error: a scenario may legitimately guard a
  // hard constraint rather than a numbered behaviour, and constraints are cited
  // the other way round. Printed so the gap is visible either way.
  const uncited = [...shortIds].filter((s) => !cited.has(s)).sort();
  if (uncited.length > 0) {
    console.log(`  not cited in SPEC.md §3: ${uncited.join(', ')}`);
  }
}

// ---------------------------------------------------------------------------
// Report
// ---------------------------------------------------------------------------
const summary = Object.entries(byClass)
  .map(([klass, n]) => `${klass} ${n}`)
  .join(' · ');

if (problems.length > 0) {
  console.error(`\nvalidate-scenarios: ${problems.length} problem(s) in ${files.length} file(s).\n`);
  let current = null;
  for (const { file, message } of problems) {
    if (file !== current) {
      console.error(`  ${file}`);
      current = file;
    }
    console.error(`    → ${message}`);
  }
  console.error('');
  process.exit(1);
}

console.log(`validate-scenarios: ${files.length} scenarios valid.`);
console.log(`  by class:    ${summary}`);
console.log(`  gated:       ${constraintCount} constraint · ${files.length - constraintCount} behaviour`);
console.log(`  skipped:     ${skipCount}`);

// Every class must be represented. A suite with no adversarial class is testing
// the demo, not the product (AI-EVALS.md §3), and the same argument applies to
// every other class — so the absence of any one of them is an error, not a note.
const empty = Object.entries(byClass).filter(([, n]) => n === 0).map(([k]) => k);
if (empty.length > 0) {
  console.error(`\nvalidate-scenarios: no scenarios in class(es): ${empty.join(', ')}.`);
  console.error('All five classes are mandatory (AI-EVALS.md §3).');
  process.exit(1);
}

// ─────────────────────────────────────────────────────────────────────────────
//  The schema proves it can reject.
//
//  Everything above shows the schema accepts 37 good documents, which is the
//  half that cannot fail. E2E-ACCEPTANCE-TESTING.md §2 is blunt about what that
//  is worth: a real assertion "only proves it can pass — not that it can catch
//  anything", and AI-EVALS.md §9 now says the same for eval suites. A schema is
//  an assertion about every scenario anyone will ever write, so it earns the
//  same treatment.
//
//  Each case below was a hole. `list_leave_typez` validated, injected no fault,
//  and left a degradation scenario running against a world where everything
//  succeeded — passing, because a scenario asserting graceful degradation is
//  satisfied by a turn that never degraded.
// ─────────────────────────────────────────────────────────────────────────────
const REJECTION_CASES = [
  {
    what: 'a tool name that does not exist',
    mutate: (f) => ({ ...f, tool_behaviour: { list_leave_typez: { outcome: 'timeout' } } }),
  },
  {
    what: 'an unknown key inside a tool behaviour',
    mutate: (f) => ({ ...f, tool_behaviour: { list_leave_types: { outcome: 'timeout', nope: 1 } } }),
  },
  {
    what: 'a tool behaviour with no outcome',
    mutate: (f) => ({ ...f, tool_behaviour: { list_leave_types: { latency_ms: 10 } } }),
  },
  { what: 'a timezone that is not a zone', mutate: (f) => ({ ...f, timezone: 'not a zone' }) },
  { what: 'a timezone path too deep to be one', mutate: (f) => ({ ...f, timezone: 'A/B/C/D' }) },
];

const specimen = parseYaml(readFileSync(files[0], 'utf8'));
const notRejected = [];

for (const { what, mutate } of REJECTION_CASES) {
  const doc = { ...specimen, id: 'deg-000-schema-self-check', fixture: mutate(specimen.fixture) };
  if (validate(doc)) {
    notRejected.push(what);
  }
}

// The converse, so the rules above cannot be satisfied by a schema that rejects
// everything: a three-component IANA zone is legitimate and must still validate.
// The old two-component pattern turned away America/Argentina/Buenos_Aires, which
// was enforcing the shape of the one zone this corpus happens to use.
if (!validate({
  ...specimen,
  id: 'deg-000-schema-self-check',
  fixture: { ...specimen.fixture, timezone: 'America/Argentina/Buenos_Aires' },
})) {
  notRejected.push('WRONGLY REJECTED a valid three-component IANA zone');
}

if (notRejected.length > 0) {
  console.error('\nvalidate-scenarios: the schema no longer catches:');
  for (const what of notRejected) {
    console.error(`  → ${what}`);
  }
  console.error('\nA schema that only ever accepts is a schema nobody knows works.');
  process.exit(1);
}
