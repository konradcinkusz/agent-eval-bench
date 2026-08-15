#!/usr/bin/env node
//
// Renders the one comment a pull request gets about its evals.
//
// A DIFF, NOT A DASHBOARD. The number a reviewer needs is not "32 scenarios
// passed" — it is "nothing changed against the baseline", or the two lines
// naming what did. A comment that restates the same totals on every push is a
// comment people stop reading, and the first regression arrives underneath one
// nobody read.
//
// It is also ONE comment, updated in place. Ten identical comments on a
// ten-push pull request is the same failure wearing a different hat.
//
// Reads:
//   TestResults/eval-report.json          Layer 1, written by the suite
//   TestResults/eval-report-layer2.json   Layer 2, ditto (may be absent)
//   evals/baselines/layer1.json           the recorded pass state
//
// Writes the markdown to stdout, and exits non-zero only when it could not do
// its job — never because the evals failed. Failing the build is the suite's
// business; this script reports.

import { readFileSync, existsSync } from 'node:fs';
import path from 'node:path';

const MARKER = '<!-- agent-eval-bench:evals -->';

const root = process.cwd();
const layer1Path = path.join(root, 'TestResults', 'eval-report.json');
const layer2Path = path.join(root, 'TestResults', 'eval-report-layer2.json');
const baselinePath = path.join(root, 'evals', 'baselines', 'layer1.json');

function readJson(file) {
  return JSON.parse(readFileSync(file, 'utf8'));
}

if (!existsSync(layer1Path)) {
  // Loudly. A comment that silently omits Layer 1 reads as "Layer 1 is fine".
  console.error(`eval-comment: no Layer 1 report at ${layer1Path}`);
  process.exit(2);
}

const layer1 = readJson(layer1Path);
const baseline = existsSync(baselinePath) ? readJson(baselinePath) : null;
const layer2 = existsSync(layer2Path) ? readJson(layer2Path) : null;

const lines = [MARKER, '', '### Agent evals', ''];

// ── Layer 1 ─────────────────────────────────────────────────────────────────

const scenarios = layer1.scenarios ?? [];
const passed = scenarios.filter((s) => s.status === 'pass');
const failed = scenarios.filter((s) => s.status === 'fail' || s.status === 'error');
const skipped = scenarios.filter((s) => String(s.status).startsWith('skipped:'));

const constraints = scenarios.filter((s) => s.gate === 'constraint');
const behaviours = scenarios.filter((s) => s.gate === 'behaviour');
const constraintsPassed = constraints.filter((s) => s.status === 'pass').length;
const behavioursPassed = behaviours.filter((s) => s.status === 'pass').length;

const verdict = failed.length === 0 ? '**green**' : `**${failed.length} failing**`;

lines.push(
  `**Layer 1** — ${verdict}, spec \`${layer1.specVersion}\`, ` +
    `interpreter \`${layer1.interpreter}\`, ${layer1.durationMs} ms`,
  '',
  '| gate | passed | what a failure means |',
  '|---|---|---|',
  `| constraint | ${constraintsPassed}/${constraints.length} | blocks the merge at 100% |`,
  `| behaviour | ${behavioursPassed}/${behaviours.length} | measured against the recorded baseline |`,
  '',
);

if (skipped.length > 0) {
  // Two kinds, reported separately, never as one number (SPEC §8.5).
  const kinds = new Map();
  for (const scenario of skipped) {
    kinds.set(scenario.status, (kinds.get(scenario.status) ?? 0) + 1);
  }

  lines.push(
    'Skipped: ' + [...kinds].map(([kind, count]) => `\`${kind}\` ${count}`).join(', '),
    '',
  );
}

// ── The diff, which is the point ────────────────────────────────────────────

if (!baseline) {
  lines.push('> No baseline recorded — a suite with no recorded state cannot tell a regression from a Tuesday.', '');
} else {
  const recorded = baseline.scenarios ?? {};
  const regressed = scenarios.filter((s) => s.status !== 'pass' && recorded[s.id] === 'pass');
  const improved = scenarios.filter((s) => s.status === 'pass' && recorded[s.id] && recorded[s.id] !== 'pass');
  const unrecorded = scenarios.filter((s) => !(s.id in recorded));

  const staleVersion = baseline.specVersion !== layer1.specVersion;

  if (regressed.length === 0 && improved.length === 0 && unrecorded.length === 0 && !staleVersion) {
    lines.push(
      `No change against the baseline (recorded ${baseline.recorded}, spec \`${baseline.specVersion}\`, ` +
        `interpreter \`${baseline.interpreter}\`).`,
      '',
    );
  } else {
    lines.push('#### Against the baseline', '');

    if (staleVersion) {
      lines.push(
        `- ⚠️ The baseline was recorded against spec \`${baseline.specVersion}\` and this run measured ` +
          `\`${layer1.specVersion}\`. Re-record it in this pull request — a baseline compared across a ` +
          'version boundary is a measuring stick that changed length between readings.',
      );
    }

    for (const scenario of regressed) {
      lines.push(`- ❌ **${scenario.id}** now failing — it passed at the baseline`);
      for (const assertion of (scenario.assertions ?? []).filter((a) => !a.passed)) {
        lines.push(`    - \`${assertion.assertion}\` — ${assertion.detail ?? 'no detail'}`);
      }
      if (scenario.error) {
        lines.push(`    - harness error: ${scenario.error}`);
      }
    }

    for (const scenario of improved) {
      lines.push(`- ✅ **${scenario.id}** now passing — the baseline records it failing. Re-record.`);
    }

    for (const scenario of unrecorded) {
      lines.push(`- 🆕 **${scenario.id}** is not in the baseline. Add it, or it is measured against nothing.`);
    }

    lines.push('');
  }

  // Failures that were ALREADY failing at the baseline are still failures, and a
  // diff-only view would hide them behind "no change". They get their own line.
  const known = failed.filter((s) => recorded[s.id] && recorded[s.id] !== 'pass');

  if (known.length > 0) {
    lines.push(
      `Still failing, and recorded as such: ${known.map((s) => `\`${s.id}\``).join(', ')}.`,
      '',
    );
  }
}

// ── Layer 2 ─────────────────────────────────────────────────────────────────

if (!layer2) {
  lines.push('**Layer 2** — no report. The judge did not run.', '');
} else {
  lines.push(
    `**Layer 2** — judge \`${layer2.judgeVersion}\`, rubrics \`${layer2.rubricsHash}\`, ` +
      `prompt \`${layer2.promptHash}\`, scope \`${layer2.scope}\``,
    '',
  );

  const graded = (layer2.scenarios ?? []).filter((s) => s.status === 'pass');

  if (graded.length === 0) {
    const noCredential = (layer2.scenarios ?? []).filter((s) => s.status === 'skipped:no-credential').length;

    lines.push(
      `\`skipped:no-credential\` — ${noCredential} scenario(s). This is legitimate on a pull request and ` +
        'nowhere else; the keyed run is `nightly.yml`.',
      '',
    );
  } else {
    lines.push(
      `Model \`${layer2.model}\`, ${layer2.inputTokens}+${layer2.outputTokens} tokens` +
        (layer2.estimatedCostUsd != null ? `, about $${Number(layer2.estimatedCostUsd).toFixed(4)}` : ''),
      '',
      '| criterion | mean | threshold | lowest | |',
      '|---|---|---|---|---|',
      ...(layer2.rubrics ?? []).map(
        (r) =>
          `| ${r.rubric} | ${r.mean.toFixed(2)} | ${r.threshold.toFixed(2)} | ${r.lowest} | ` +
          `${r.meetsThreshold && r.meetsFloor ? '✅' : '❌'} |`,
      ),
      '',
    );
  }

  lines.push(`Calibration: ${layer2.calibration?.reason ?? 'unknown'}`, '');
}

// ── What green does and does not mean ───────────────────────────────────────
//
// On the comment itself rather than in a document nobody opens from a pull
// request. The single most likely misreading of a green check here is that the
// agent understood the sentence, and it did not: on the gated path the
// interpreter is rule-based.

lines.push(
  '<details><summary>What a green Layer 1 means</summary>',
  '',
  'Tool ordering, the confirmation gate, grounding, termination and the absence of internal',
  'identifiers — all deterministic properties of the trace. It does **not** mean the agent',
  'understood the sentence: on the gated path the interpreter is rule-based, and language',
  'understanding is what the judge and the keyed nightly run are for. The two baselines are',
  'never merged (`docs/adr/0004-pin-the-model-and-never-fall-back-silently.md`).',
  '',
  '</details>',
);

process.stdout.write(lines.join('\n') + '\n');
