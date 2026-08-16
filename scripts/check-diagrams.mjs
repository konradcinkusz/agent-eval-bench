#!/usr/bin/env node
/**
 * check-diagrams.mjs — verify that every Mermaid diagram exists in exactly two
 * places and that the two agree.
 *
 * Why two places at all:
 *
 *   `docs/DIAGRAMS.md` must carry the diagram source inline, because that is the
 *   only form GitHub renders. `docs/diagrams/*.mmd` must exist separately,
 *   because a diagram nobody can open on its own is a diagram nobody reuses —
 *   in a slide, in an issue, in a `mmdc` render, or in a review comment.
 *
 *   Two copies of anything is a drift surface. This repository's answer to a
 *   drift surface is never "remember to update both"; it is a check that fails.
 *   The same reasoning produced the agent-definition validator (one version in
 *   three places) and the change-coupling check.
 *
 * The pairing rule: a section headed `### A1. …` owns the file whose name starts
 * `a1-`. That keeps the filename free to describe the diagram while the id stays
 * the join key.
 *
 * Zero dependencies on purpose: this runs in the same job as check-links.mjs,
 * before anything has been installed.
 *
 * Usage: node scripts/check-diagrams.mjs
 * Exit:  0 = every diagram is paired and identical; 1 = at least one is not.
 */

import { readFileSync, readdirSync, existsSync } from 'node:fs';
import { execFileSync } from 'node:child_process';
import { join } from 'node:path';

const repoRoot = execFileSync('git', ['rev-parse', '--show-toplevel'], { encoding: 'utf8' }).trim();
const docPath = join(repoRoot, 'docs', 'DIAGRAMS.md');
const dirPath = join(repoRoot, 'docs', 'diagrams');

if (!existsSync(docPath)) {
  console.error('check-diagrams: docs/DIAGRAMS.md is missing. It is the rendered half of this pair.');
  process.exit(1);
}

if (!existsSync(dirPath)) {
  console.error('check-diagrams: docs/diagrams/ is missing. It is the reusable half of this pair.');
  process.exit(1);
}

const markdown = readFileSync(docPath, 'utf8');

/**
 * Sections and the Mermaid block each one owns. A section heading looks like
 * `### A1. System context — who talks to what`; the id is `A1`.
 */
const sections = [];
{
  const lines = markdown.split('\n');
  let current = null;
  let fence = null;

  for (const line of lines) {
    const heading = line.match(/^###\s+([A-D]\d+)\.\s+(.+)$/);

    if (heading) {
      current = { id: heading[1].toLowerCase(), title: heading[2].trim(), source: null };
      sections.push(current);
      continue;
    }

    if (fence === null && line.trim() === '```mermaid') {
      fence = [];
      continue;
    }

    if (fence !== null && line.trim() === '```') {
      if (current && current.source === null) {
        current.source = `${fence.join('\n')}\n`;
      }
      fence = null;
      continue;
    }

    if (fence !== null) {
      fence.push(line);
    }
  }
}

const files = readdirSync(dirPath).filter((name) => name.endsWith('.mmd'));
const failures = [];
const claimed = new Set();

for (const section of sections) {
  if (section.source === null) {
    failures.push(`Section ${section.id.toUpperCase()} ("${section.title}") has no \`\`\`mermaid block.`);
    continue;
  }

  const matches = files.filter((name) => name.startsWith(`${section.id}-`));

  if (matches.length === 0) {
    failures.push(
      `Section ${section.id.toUpperCase()} ("${section.title}") has no file in docs/diagrams/. `
      + `Expected one named ${section.id}-<slug>.mmd.`,
    );
    continue;
  }

  if (matches.length > 1) {
    failures.push(`Section ${section.id.toUpperCase()} matches more than one file: ${matches.join(', ')}.`);
    continue;
  }

  const [name] = matches;
  claimed.add(name);

  const onDisk = readFileSync(join(dirPath, name), 'utf8');

  if (onDisk !== section.source) {
    failures.push(
      `docs/diagrams/${name} and section ${section.id.toUpperCase()} of docs/DIAGRAMS.md have drifted. `
      + 'Whichever you edited, copy it to the other — they are the same diagram.',
    );
  }
}

for (const name of files) {
  if (!claimed.has(name)) {
    failures.push(`docs/diagrams/${name} is not referenced by any section of docs/DIAGRAMS.md.`);
  }
}

if (sections.length === 0) {
  console.error('check-diagrams: docs/DIAGRAMS.md declares no diagram sections. That cannot be right — failing loudly.');
  process.exit(1);
}

if (failures.length > 0) {
  console.error('check-diagrams: FAILED\n');
  for (const failure of failures) {
    console.error(`  - ${failure}`);
  }
  process.exit(1);
}

console.log(`check-diagrams: ${sections.length} diagram(s), each paired with docs/diagrams/ and identical.`);
