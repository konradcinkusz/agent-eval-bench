#!/usr/bin/env node
/**
 * check-links.mjs — verify that every RELATIVE link and image in the repository's
 * Markdown resolves to a file that exists.
 *
 * Why relative links only, and no network:
 *
 *   P14's corollary is that a stale README is a review finding. The staleness this
 *   catches is the one that is both most common and least visible in a diff — a
 *   document pointing at a file that was renamed or never written. Checking
 *   external URLs instead would add a network dependency, rate limits, and a lint
 *   job that fails for reasons unrelated to the commit; a flaky gate is a gate
 *   people learn to re-run rather than read.
 *
 * Zero dependencies on purpose: this runs before `npm ci` has anything to install.
 *
 * Usage: node scripts/check-links.mjs
 * Exit:  0 = every relative link resolves; 1 = at least one does not.
 */

import { readFileSync, existsSync, statSync } from 'node:fs';
import { execFileSync } from 'node:child_process';
import { dirname, resolve, join } from 'node:path';

const repoRoot = execFileSync('git', ['rev-parse', '--show-toplevel'], { encoding: 'utf8' }).trim();

// --cached AND --others: tracked files plus new, not-yet-committed ones. Without
// --others this passes locally on a document that was never added, and fails in CI
// after checkout — the worst order to discover a broken link in.
const markdownFiles = execFileSync(
  'git',
  ['ls-files', '--cached', '--others', '--exclude-standard', '*.md'],
  { encoding: 'utf8', cwd: repoRoot },
)
  .split('\n')
  .filter(Boolean)
  // ls-files lists a path twice when it is both cached and modified.
  .filter((f, i, all) => all.indexOf(f) === i);

if (markdownFiles.length === 0) {
  console.error('check-links: no Markdown files found. That cannot be right — failing loudly.');
  process.exit(1);
}

/** Inline links and images: [text](target) and ![alt](target). */
const INLINE_LINK = /!?\[[^\]]*\]\(([^)\s]+)(?:\s+"[^"]*")?\)/g;
/** Reference definitions: [label]: target */
const REFERENCE_DEF = /^\s{0,3}\[[^\]]+\]:\s*(\S+)/gm;

/** Targets that are not files on disk and are therefore out of scope. */
function isExternal(target) {
  return (
    /^[a-z][a-z0-9+.-]*:/i.test(target) || // http:, https:, mailto:, tel:, data:
    target.startsWith('//') ||
    target.startsWith('#') // same-document anchor
  );
}

/**
 * Fenced code blocks are stripped before scanning. A README that documents a
 * command containing brackets and parentheses would otherwise be reported as a
 * broken link, which trains the reader to ignore this tool.
 */
function stripFencedCode(markdown) {
  return markdown.replace(/^([ \t]*)(```|~~~).*$[\s\S]*?^\1\2[ \t]*$/gm, '');
}

let checked = 0;
const failures = [];

for (const file of markdownFiles) {
  const absolute = join(repoRoot, file);
  const source = stripFencedCode(readFileSync(absolute, 'utf8'));
  const fileDir = dirname(absolute);

  const targets = [
    ...[...source.matchAll(INLINE_LINK)].map((m) => m[1]),
    ...[...source.matchAll(REFERENCE_DEF)].map((m) => m[1]),
  ];

  for (const rawTarget of targets) {
    if (isExternal(rawTarget)) {
      continue;
    }

    // Drop any anchor fragment: the file must exist; the heading is not checked,
    // because heading slugs differ between renderers and a wrong answer here
    // would be worse than no answer.
    const pathPart = decodeURIComponent(rawTarget.split('#')[0]);
    if (pathPart === '') {
      continue;
    }

    const resolved = pathPart.startsWith('/')
      ? resolve(repoRoot, `.${pathPart}`)
      : resolve(fileDir, pathPart);

    checked += 1;

    if (!existsSync(resolved)) {
      failures.push({ file, target: rawTarget, reason: 'does not exist' });
      continue;
    }

    // A link to a directory is valid only if the directory can render something.
    if (statSync(resolved).isDirectory()) {
      const hasIndex = ['README.md', 'index.md'].some((n) => existsSync(join(resolved, n)));
      if (!hasIndex) {
        failures.push({ file, target: rawTarget, reason: 'directory has no README.md' });
      }
    }
  }
}

if (failures.length > 0) {
  console.error(`\ncheck-links: ${failures.length} broken relative link(s) in ${markdownFiles.length} file(s).\n`);
  for (const { file, target, reason } of failures) {
    console.error(`  ${file}`);
    console.error(`    → ${target}  (${reason})`);
  }
  console.error('\nA document pointing at a file that does not exist teaches the reader');
  console.error('something false about this repository. Fix the link or write the file.\n');
  process.exit(1);
}

console.log(
  `check-links: ${checked} relative link(s) across ${markdownFiles.length} Markdown file(s) — all resolve.`,
);
