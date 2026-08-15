#!/usr/bin/env node
/**
 * validate-agent-definitions.mjs — the agent as code, checked like code.
 *
 * `agents/absence-concierge/definition.json` is behaviour with no code diff. It
 * names the tools the agent may call, which of them need human approval, and
 * which version of the specification it implements — and nothing compiled it,
 * nothing typed it, and nothing until now compared it to the service it
 * describes. That is precisely the artefact that regresses an agent while every
 * test stays green.
 *
 * Four checks, in increasing order of what they would have caught:
 *
 *   1. The schema (agents/schema/agent-definition.schema.json), which is strict:
 *      `additionalProperties: false` everywhere, so a mistyped key is an error
 *      rather than a field the provisioner ignores.
 *
 *   2. Internal consistency: directory name matches `slug`, `specPath` and
 *      `evalSuite` exist on disk, a null `model` carries the reasoning that
 *      says it is unchosen rather than forgotten.
 *
 *   3. ONE VERSION IN THREE PLACES. `version`, `metadata.specVersion` and the
 *      "Spec version" line in the specification document must agree. They did
 *      not when this check was written — the definition claimed to implement
 *      1.0.0 of a specification that had been at 1.2.0 for two phases.
 *
 *   4. THE TOOL CATALOGUE, AGAINST THE SERVICE'S OWN SOURCE. `allowedTools`
 *      must be exactly the tools in WorkforceToolCatalog, and `requireApproval`
 *      must split them read/write exactly as the catalogue classifies them. The
 *      definition is the third place the confirmation gate is enforced (after
 *      the agent's pipeline and the tool boundary); a definition that drifts
 *      from the catalogue is a layer that has quietly stopped agreeing with the
 *      other two about which call books somebody's leave.
 *
 * And the rule that outranks the rest, from AZURE-AI-FOUNDRY-AGENTS.md §6's
 * provisioner: a run that found nothing must not exit 0.
 *
 * Usage: node scripts/validate-agent-definitions.mjs
 * Exit:  0 = every definition valid; 1 = at least one problem.
 */

import { readFileSync, existsSync, readdirSync, statSync } from 'node:fs';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import Ajv2020 from 'ajv/dist/2020.js';

const REPO_ROOT = join(dirname(fileURLToPath(import.meta.url)), '..');
const AGENT_DIR = join(REPO_ROOT, 'agents');
const SCHEMA_PATH = join(AGENT_DIR, 'schema', 'agent-definition.schema.json');

/**
 * The service's tool catalogue, which is the source of truth for both the tool
 * names and the read/write classification (docs/SPEC.md §2.1).
 *
 * Read out of the C# rather than re-declared here. A second list would be the
 * one that goes stale, and the whole value of this check is that it compares two
 * artefacts nobody keeps in sync by hand.
 */
const CATALOGUE_PATH = join(
  REPO_ROOT,
  'src',
  'AbsenceConcierge.AgentService',
  'Workforce',
  'IWorkforceTools.cs',
);

const problems = [];

function fail(file, message) {
  problems.push({ file, message });
}

// ── The catalogue, from the source ──────────────────────────────────────────

function readCatalogue() {
  if (!existsSync(CATALOGUE_PATH)) {
    fail('scripts/validate-agent-definitions.mjs', `The tool catalogue is not where this script expects it: ${CATALOGUE_PATH}`);
    return null;
  }

  const source = readFileSync(CATALOGUE_PATH, 'utf8');

  // `public const string GetCurrentUser = "get_current_user";`
  const names = new Map();
  for (const match of source.matchAll(/public const string (\w+)\s*=\s*"([^"]+)"/g)) {
    names.set(match[1], match[2]);
  }

  // `[GetCurrentUser] = WorkforceToolKind.Read,`
  const kinds = new Map();
  for (const match of source.matchAll(/\[(\w+)\]\s*=\s*WorkforceToolKind\.(Read|Write)/g)) {
    const name = names.get(match[1]);
    if (name) {
      kinds.set(name, match[2].toLowerCase());
    }
  }

  if (kinds.size === 0) {
    // The regexes stopped matching, which is a silent pass waiting to happen:
    // an empty catalogue would make every allow-list check vacuously true.
    fail(
      'src/.../IWorkforceTools.cs',
      'No tools were read out of WorkforceToolCatalog. The catalogue moved or was rewritten, and this script now checks nothing.',
    );
    return null;
  }

  return kinds;
}

// ── The specification's version ─────────────────────────────────────────────

function readSpecVersion(specPath) {
  const absolute = join(REPO_ROOT, specPath);

  if (!existsSync(absolute)) {
    return { error: `metadata.specPath points at '${specPath}', which does not exist.` };
  }

  const match = readFileSync(absolute, 'utf8').match(/^-\s*\*\*Spec version\*\*:\s*([0-9]+\.[0-9]+\.[0-9]+)\s*$/m);

  if (!match) {
    return { error: `${specPath} has no '- **Spec version**: x.y.z' line, so the version cannot be compared.` };
  }

  return { version: match[1] };
}

// ── Per-definition checks ───────────────────────────────────────────────────

function checkDefinition(relativePath, definition, directoryName, catalogue) {
  if (definition.slug !== directoryName) {
    fail(relativePath, `slug is '${definition.slug}' but the directory is '${directoryName}'. One agent, one name.`);
  }

  if (definition.model === null && typeof definition.metadata?.modelSelection !== 'string') {
    fail(
      relativePath,
      'model is null with no metadata.modelSelection saying why. Null must mean "not chosen yet, refuse to provision" — '
      + 'and unexplained null is indistinguishable from a field somebody forgot.',
    );
  }

  const evalSuite = join(REPO_ROOT, definition.metadata.evalSuite);

  if (!existsSync(evalSuite)) {
    fail(relativePath, `metadata.evalSuite points at '${definition.metadata.evalSuite}', which does not exist.`);
  }

  // ── One version, three places ────────────────────────────────────────────

  const spec = readSpecVersion(definition.metadata.specPath);

  if (spec.error) {
    fail(relativePath, spec.error);
  } else if (definition.version !== definition.metadata.specVersion || definition.version !== spec.version) {
    fail(
      relativePath,
      'Three versions that must agree do not:\n'
      + `    version                 ${definition.version}\n`
      + `    metadata.specVersion    ${definition.metadata.specVersion}\n`
      + `    ${definition.metadata.specPath}          ${spec.version}\n`
      + '  The eval baseline is recorded against a version. Comparing across a disagreement compares nothing.',
    );
  }

  // ── The tool catalogue ───────────────────────────────────────────────────

  if (!catalogue) {
    return;
  }

  const expected = {
    all: [...catalogue.keys()].sort(),
    reads: [...catalogue.entries()].filter(([, kind]) => kind === 'read').map(([name]) => name).sort(),
    writes: [...catalogue.entries()].filter(([, kind]) => kind === 'write').map(([name]) => name).sort(),
  };

  for (const tool of definition.tools.filter((entry) => entry.type === 'mcp')) {
    compare(relativePath, `tools[mcp:${tool.serverLabel}].allowedTools`, tool.allowedTools, expected.all,
      'The agent may call exactly the tools the service implements — no more, and no fewer.');

    compare(relativePath, `tools[mcp:${tool.serverLabel}].requireApproval.never`, tool.requireApproval.never, expected.reads,
      'A read that needs approval is friction; a write that does not is C-1 defeated in the one layer a provisioner enforces.');

    compare(relativePath, `tools[mcp:${tool.serverLabel}].requireApproval.always`, tool.requireApproval.always, expected.writes,
      'These are the calls that change somebody\'s leave. The catalogue classifies them, and this list must say the same.');
  }
}

function compare(file, what, actual, expected, why) {
  const got = [...actual].sort();

  const missing = expected.filter((name) => !got.includes(name));
  const extra = got.filter((name) => !expected.includes(name));

  if (missing.length === 0 && extra.length === 0) {
    return;
  }

  const lines = [`${what} does not match WorkforceToolCatalog.`];

  if (missing.length > 0) {
    lines.push(`    missing: ${missing.join(', ')}`);
  }

  if (extra.length > 0) {
    lines.push(`    not in the catalogue: ${extra.join(', ')}`);
  }

  lines.push(`  ${why}`);

  fail(file, lines.join('\n'));
}

// ── Run ─────────────────────────────────────────────────────────────────────

if (!existsSync(SCHEMA_PATH)) {
  console.error(`validate-agent-definitions: the schema is missing (${SCHEMA_PATH}).`);
  process.exit(1);
}

const ajv = new Ajv2020({ allErrors: true, strict: true });
const validate = ajv.compile(JSON.parse(readFileSync(SCHEMA_PATH, 'utf8')));
const catalogue = readCatalogue();

const directories = existsSync(AGENT_DIR)
  ? readdirSync(AGENT_DIR)
      .filter((entry) => entry !== 'schema')
      .filter((entry) => statSync(join(AGENT_DIR, entry)).isDirectory())
  : [];

let checked = 0;

for (const directory of directories) {
  const path = join(AGENT_DIR, directory, 'definition.json');
  const relativePath = `agents/${directory}/definition.json`;

  if (!existsSync(path)) {
    fail(relativePath, 'The directory has no definition.json. An agent directory that defines no agent is a directory nothing provisions.');
    continue;
  }

  checked += 1;

  let definition;

  try {
    definition = JSON.parse(readFileSync(path, 'utf8'));
  } catch (error) {
    fail(relativePath, `Not valid JSON: ${error.message}`);
    continue;
  }

  if (!validate(definition)) {
    for (const error of validate.errors) {
      fail(relativePath, `${error.instancePath || '/'} ${error.message}`);
    }

    // The checks below index into the document. Running them against a shape the
    // schema rejected produces a second, confusing failure about the first one.
    continue;
  }

  checkDefinition(relativePath, definition, directory, catalogue);
}

if (checked === 0) {
  console.error('validate-agent-definitions: no agent definitions found under agents/.');
  console.error('A validator that passes an empty directory is the failure it exists to prevent.');
  process.exit(1);
}

if (problems.length === 0) {
  console.log(`validate-agent-definitions: ${checked} definition(s), schema and catalogue agree.`);
  process.exit(0);
}

for (const problem of problems) {
  console.error('');
  console.error(`  ${problem.file}`);
  console.error(`    ${problem.message}`);
}

console.error('');
console.error(`validate-agent-definitions: ${problems.length} problem(s).`);
process.exit(1);
