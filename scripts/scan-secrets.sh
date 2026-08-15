#!/usr/bin/env bash
#
# scan-secrets.sh — the local mirror of the CI secret-scan job.
#
# REPO-BASELINE.md §4: "a script that reproduces a CI job 1:1 so the job can be
# debugged without pushing a tag". This one scans the full history exactly as
# .github/workflows/secret-scan.yml does, so "it passed locally" means something.
#
# Usage:
#   scripts/scan-secrets.sh            # full history (what CI runs)
#   scripts/scan-secrets.sh --staged   # staged changes only (what the hook runs)
#   scripts/scan-secrets.sh --dir      # working tree as files, ignoring git history

set -euo pipefail

REPO_ROOT="$(git rev-parse --show-toplevel)"
CONFIG="${REPO_ROOT}/.gitleaks.toml"
MODE="${1:-history}"

red()   { printf '\033[0;31m%s\033[0m\n' "$*"; }
green() { printf '\033[0;32m%s\033[0m\n' "$*"; }
dim()   { printf '\033[0;90m%s\033[0m\n' "$*"; }

if [ ! -f "${CONFIG}" ]; then
  red "scan-secrets: .gitleaks.toml missing from the repository root."
  exit 1
fi

# gitleaks renamed its subcommands in v8.19 (detect → git, and a new `dir`).
# Probe instead of assuming, so this works on old and new installs alike.
have_new_cli() { gitleaks git --help >/dev/null 2>&1; }

if ! command -v gitleaks >/dev/null 2>&1; then
  if command -v docker >/dev/null 2>&1 && docker info >/dev/null 2>&1; then
    dim "gitleaks not on PATH — running the container image (same one CI uses)."
    IMAGE="ghcr.io/gitleaks/gitleaks:latest"
    if docker run --rm "${IMAGE}" git --help >/dev/null 2>&1; then
      SUB=(git /repo)
    else
      SUB=(detect --source=/repo)
    fi
    exec docker run --rm -v "${REPO_ROOT}:/repo" -w /repo "${IMAGE}" \
      "${SUB[@]}" --config=/repo/.gitleaks.toml --redact --no-banner --verbose
  fi
  red "scan-secrets: neither gitleaks nor a running Docker daemon is available."
  echo "Install gitleaks: https://github.com/gitleaks/gitleaks#installing"
  exit 1
fi

# NOTE on the two command forms: v8.19 renamed `detect`/`protect` to `git`/`dir`
# and moved the target from --source to a positional argument. Getting only half
# of that right fails with "unknown flag", so both halves are switched together.
case "${MODE}" in
  --staged)
    dim "Scanning staged changes only."
    if have_new_cli; then
      gitleaks git --staged "${REPO_ROOT}" --config="${CONFIG}" --redact --no-banner --verbose
    else
      gitleaks protect --staged --source="${REPO_ROOT}" --config="${CONFIG}" --redact --no-banner --verbose
    fi
    ;;
  --dir)
    dim "Scanning the working tree as files (history ignored)."
    if have_new_cli; then
      gitleaks dir "${REPO_ROOT}" --config="${CONFIG}" --redact --no-banner --verbose
    else
      gitleaks detect --no-git --source="${REPO_ROOT}" --config="${CONFIG}" --redact --no-banner --verbose
    fi
    ;;
  history|"")
    dim "Scanning full git history — this is what CI runs."
    if have_new_cli; then
      gitleaks git "${REPO_ROOT}" --config="${CONFIG}" --redact --no-banner --verbose
    else
      gitleaks detect --source="${REPO_ROOT}" --config="${CONFIG}" --redact --no-banner --verbose
    fi
    ;;
  *)
    red "Unknown mode: ${MODE}"
    echo "Usage: scripts/scan-secrets.sh [--staged|--dir]"
    exit 2
    ;;
esac

green "Secret scan clean."
