#!/usr/bin/env node
/**
 * validate-fixtures.mjs — the gate that stops a fixture key from lying.
 *
 * Scenarios have had a strict schema since Phase 1. Fixtures did not, and both
 * C# loaders build their deserializer with `.IgnoreUnmatchedProperties()`, so a
 * key nothing reads is dropped in silence rather than refused. Five were:
 * `balances`, `tool_policy`, `email_domain_handle`, `role` and `half_day` — and
 * the comment above `tool_policy` told every reader it was "what the mock
 * enforces at the boundary", which a scenario's `why` then repeated.
 *
 * That is the documentation-that-lies failure this repository exists to
 * demonstrate against, sitting one directory from where its own checks look.
 *
 * The schema is strict, so a key the loader does not read fails here instead.
 * Adding a key to a fixture now means adding it to `FixtureFile` and to
 * `evals/schema/fixture.schema.json` — which is the point.
 *
 * Usage: node scripts/validate-fixtures.mjs
 * Exit:  0 = every fixture valid; 1 = at least one problem, or none found.
 */

import { readFileSync, readdirSync, existsSync } from 'node:fs';
import { join, basename, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { parse } from 'yaml';
import Ajv2020 from 'ajv/dist/2020.js';
import addFormats from 'ajv-formats';

const ROOT = dirname(dirname(fileURLToPath(import.meta.url)));
const FIXTURES = join(ROOT, 'evals', 'fixtures');
const SCHEMA = join(ROOT, 'evals', 'schema', 'fixture.schema.json');

const problems = [];

if (!existsSync(FIXTURES)) {
  console.error(`validate-fixtures: no fixture directory at ${FIXTURES}`);
  process.exit(1);
}

const ajv = new Ajv2020({ allErrors: true, strict: false });
addFormats(ajv);
const validate = ajv.compile(JSON.parse(readFileSync(SCHEMA, 'utf8')));

const files = readdirSync(FIXTURES).filter(f => f.endsWith('.yaml')).sort();

// A validator that passes an empty set is the failure it exists to prevent.
if (files.length === 0) {
  console.error('validate-fixtures: no fixtures found. An empty set is not a passing run.');
  process.exit(1);
}

for (const file of files) {
  const path = join(FIXTURES, file);
  let doc;

  try {
    doc = parse(readFileSync(path, 'utf8'));
  } catch (error) {
    problems.push(`${file}: not parseable — ${error.message}`);
    continue;
  }

  if (!validate(doc)) {
    for (const error of validate.errors) {
      const where = error.instancePath || '(root)';
      const extra = error.params?.additionalProperty
        ? ` — '${error.params.additionalProperty}' is read by no loader, so it would be dropped in silence`
        : '';
      problems.push(`${file}: ${where} ${error.message}${extra}`);
    }
  }

  // The name is what a scenario's `fixture.base` resolves by, so a mismatch
  // makes the file unreachable under the name it declares.
  const expected = basename(file, '.yaml');
  if (doc?.name !== expected) {
    problems.push(`${file}: declares name '${doc?.name}' but must match its filename '${expected}'`);
  }

  // Every leave referenced must name a leave type this world actually has,
  // otherwise the conflict check reads a booking against nothing.
  const typeIds = new Set((doc?.leave_types ?? []).map(t => t.id));
  for (const leave of doc?.existing_leaves ?? []) {
    if (!typeIds.has(leave.leave_type_id)) {
      problems.push(`${file}: existing_leaves '${leave.id}' names unknown leave_type_id '${leave.leave_type_id}'`);
    }
  }
}

if (problems.length > 0) {
  console.error(`validate-fixtures: ${problems.length} problem(s).\n`);
  for (const problem of problems) console.error(`  ${problem}`);
  process.exit(1);
}

console.log(`validate-fixtures: ${files.length} fixture(s) valid — every key reaches the loader.`);
