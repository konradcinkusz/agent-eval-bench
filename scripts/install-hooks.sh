#!/usr/bin/env bash
#
# install-hooks.sh — point this clone's git hooks at the committed ones.
#
# Hooks live in scripts/hooks/ so they are reviewed like code. .git/hooks/ is not
# under version control, so every clone has to opt in once; scripts/setup.sh calls
# this for you.
#
# Implementation note: this sets core.hooksPath rather than copying files. A copied
# hook goes stale the moment the committed one changes, and nothing tells you.

set -euo pipefail

REPO_ROOT="$(git rev-parse --show-toplevel)"
HOOKS_DIR="scripts/hooks"

green() { printf '\033[0;32m%s\033[0m\n' "$*"; }
dim()   { printf '\033[0;90m%s\033[0m\n' "$*"; }

cd "${REPO_ROOT}"

chmod +x "${HOOKS_DIR}"/* 2>/dev/null || true
git config core.hooksPath "${HOOKS_DIR}"

# Globbing rather than `ls`: a filename with a space would be split by word
# splitting on the output of ls, and hook names are not validated anywhere.
HOOK_NAMES=""
for hook in "${HOOKS_DIR}"/*; do
  [ -e "${hook}" ] || continue
  HOOK_NAMES="${HOOK_NAMES}$(basename "${hook}") "
done

green "Git hooks installed."
dim   "  core.hooksPath = ${HOOKS_DIR}"
dim   "  active hooks:   ${HOOK_NAMES:-none found}"
dim   "  to undo:        git config --unset core.hooksPath"
