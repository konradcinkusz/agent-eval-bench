#!/usr/bin/env bash
#
# ============================================================================
#  0-setup-first-time  →  scripts/setup.sh
#
#  One-command onboarding for agent-eval-bench.
#  REPO-BASELINE.md §3: numbered steps, prerequisites checked with install
#  pointers, local secret store initialised, and every third-party integration
#  offered as a clearly labelled OPTIONAL step so that skipping is informed.
#
#  What makes this repository's version short: there is no mandatory secret.
#  A fresh clone runs the agent, the showcase page and the full Layer-1 eval
#  suite with zero credentials. Steps 4 and 5 are therefore genuinely optional,
#  not optional-in-name.
#
#  Usage:  ./scripts/setup.sh            interactive
#          ./scripts/setup.sh --check    prerequisites only, no changes
# ============================================================================

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "${REPO_ROOT}"

CHECK_ONLY=false
[ "${1:-}" = "--check" ] && CHECK_ONLY=true

# ── output helpers ──────────────────────────────────────────────────────────
bold()  { printf '\033[1m%s\033[0m\n' "$*"; }
green() { printf '\033[0;32m  ✓ %s\033[0m\n' "$*"; }
yellow(){ printf '\033[0;33m  ○ %s\033[0m\n' "$*"; }
red()   { printf '\033[0;31m  ✗ %s\033[0m\n' "$*"; }
dim()   { printf '\033[0;90m    %s\033[0m\n' "$*"; }
step()  { printf '\n\033[1;36m%s\033[0m\n' "$*"; }

MISSING_REQUIRED=0
MISSING_SCANNER=0

bold "agent-eval-bench — first-time setup"
dim  "$(git rev-parse --short HEAD 2>/dev/null || echo 'no commits yet') on $(git rev-parse --abbrev-ref HEAD 2>/dev/null || echo '?')"

# ============================================================================
step "1. Prerequisites"
# ============================================================================

# --- required ---
if command -v git >/dev/null 2>&1; then
  green "git $(git --version | awk '{print $3}')"
else
  red "git is not installed."
  MISSING_REQUIRED=1
fi

if command -v dotnet >/dev/null 2>&1; then
  DOTNET_VERSION="$(dotnet --version 2>/dev/null || echo unknown)"
  DOTNET_MAJOR="${DOTNET_VERSION%%.*}"
  if [ "${DOTNET_MAJOR}" -ge 10 ] 2>/dev/null; then
    green ".NET SDK ${DOTNET_VERSION}"
  else
    red ".NET SDK ${DOTNET_VERSION} found, but this repository targets net10.0."
    dim "global.json pins the SDK band; an older SDK will refuse to build."
    dim "Install: https://dotnet.microsoft.com/download/dotnet/10.0"
    MISSING_REQUIRED=1
  fi
else
  red ".NET SDK is not installed."
  dim "Install .NET 10 SDK: https://dotnet.microsoft.com/download/dotnet/10.0"
  dim "  macOS:   brew install --cask dotnet-sdk"
  dim "  Linux:   https://learn.microsoft.com/dotnet/core/install/linux"
  dim "  Windows: winget install Microsoft.DotNet.SDK.10"
  MISSING_REQUIRED=1
fi

# --- container engine ---
# The daemon is probed, not just the CLI. `docker --version` succeeds on a machine
# where the daemon is not running, and reporting that as available would make the
# gitleaks fallback below claim a scanner that cannot start.
DOCKER_USABLE=false
if command -v docker >/dev/null 2>&1; then
  if docker info >/dev/null 2>&1; then
    DOCKER_USABLE=true
    green "docker $(docker --version 2>/dev/null | awk '{print $3}' | tr -d ,) (daemon running)"
  else
    yellow "docker CLI found, but the daemon is not reachable."
    dim "Container builds and the containerised scanner are unavailable until it starts."
  fi
else
  yellow "docker not found — container builds and the containerised scanner are unavailable."
  dim "Not needed for: building, testing, running the agent, or running the evals."
fi

# --- secret scanner: required, because the pre-commit hook refuses without one ---
if command -v gitleaks >/dev/null 2>&1; then
  green "gitleaks $(gitleaks version 2>/dev/null || echo '')"
elif [ "${DOCKER_USABLE}" = true ]; then
  yellow "gitleaks not installed — the pre-commit hook will use the Docker image."
  dim "Slower per commit. Install the binary to avoid it: https://github.com/gitleaks/gitleaks#installing"
else
  yellow "No secret scanner: neither gitleaks nor a running Docker daemon is available."
  dim "The pre-commit hook REFUSES to commit without one, by design (P5) — so you"
  dim "can build, test, run the agent and run the evals, but not commit, until one"
  dim "exists. Install: https://github.com/gitleaks/gitleaks#installing"
  MISSING_SCANNER=1
fi

if command -v node >/dev/null 2>&1; then
  green "node $(node --version)"
else
  yellow "node not found — the markdown lint job cannot be run locally."
  dim "CI still runs it. Only affects: npm run lint:md"
fi

# A missing scanner blocks COMMITTING, not building — and the front door promises
# the .NET SDK is the only hard prerequisite. This used to be fatal, so on an
# ordinary fresh laptop with dotnet and node the quickstart's very first command
# exited 1 before installing hooks or creating the local secret store: the setup
# script refuting the README two paragraphs above it. `--check` is the committer's
# gate and stays strict; an interactive run degrades and says what will be refused.
if [ "${CHECK_ONLY}" = true ] && [ "${MISSING_SCANNER}" -ne 0 ]; then
  MISSING_REQUIRED=1
fi

if [ "${MISSING_REQUIRED}" -ne 0 ]; then
  printf '\n'
  red "Required prerequisites are missing (listed above). Nothing was changed."
  exit 1
fi

if [ "${CHECK_ONLY}" = true ]; then
  printf '\n'
  green "Prerequisite check passed. No changes made (--check)."
  exit 0
fi

if [ "${MISSING_SCANNER}" -ne 0 ]; then
  printf '\n'
  yellow "Continuing without a secret scanner. Commits WILL be refused until one exists."
fi

# ============================================================================
step "2. Git hooks"
# ============================================================================
./scripts/install-hooks.sh

# ============================================================================
step "3. Local secret store"
# ============================================================================
# Secrets never live in the working tree as files that a build could copy. Two
# stores are used, and they are NOT interchangeable:
#   • dotnet user-secrets — per-project, outside the repository. This is the store
#     .NET reads by itself, and the one every how-to in docs/how-to/ uses.
#   • .env (gitignored)   — for shell tooling and container runs. .NET does not
#     read it natively and no script here sources it, so a value set only in .env
#     reaches nothing. Saying so is the point: secrets.env.example describes "the
#     journey of any value, once: .env → environment variable → IConfiguration
#     key", and the first arrow is the shell's job, not this script's.
if [ -f .env ]; then
  green ".env already exists — leaving it alone."
else
  cp secrets.env.example .env
  green "Created .env from secrets.env.example (gitignored)."
fi
dim "Every variable in it is OPTIONAL. An empty .env is a valid, working setup."
dim "Nothing reads .env on your behalf. To load it into a shell first:"
dim "  set -a; . ./.env; set +a"

# ============================================================================
step "4. Optional — live LLM provider  (needed for: live-model mode)"
# ============================================================================
yellow "Skipped unless you set it up yourself."
dim "Without it: the deterministic composer writes the reply (Llm:Provider=None)."
dim "            Free, and what the Layer-1 evals run on anyway."
dim "With it, in the store .NET actually reads:"
dim "  dotnet user-secrets --project src/AbsenceConcierge.AgentService set Llm:Provider AzureOpenAI"
dim "  ...and likewise Llm:Endpoint, Llm:Model and Llm:ApiKey."
dim "The key is Llm:Model — the Azure DEPLOYMENT NAME. There is no Llm:Deployment."
dim "Setting these in .env alone is a silent no-op: nothing loads that file."
dim "What degrades if you skip it: nothing in the demo or in Layer 1."

# ============================================================================
step "5. Optional — LLM judge  (needed for: eval Layer 2)"
# ============================================================================
yellow "Skipped unless you set Llm:ApiKey, Llm:Endpoint and Llm:JudgeModel."
dim "Without it: the Layer-2 job reports SKIPPED with a reason — never green."
dim "With it:    rubric-anchored scoring against evals/rubrics/, judge model pinned."

# ============================================================================
step "6. Optional — live workforce MCP server  (needed for: Mcp mode)"
# ============================================================================
yellow "Skipped unless you set WorkforceTools__Mode=Mcp and its credentials."
dim "Without it: in-memory fixtures. This is the default and the demonstrated path."
dim "Never enabled on the public deployment — it acts as a real identity."

# ============================================================================
step "Done"
# ============================================================================
SLN=""
for candidate in ./*.sln ./*.slnx; do
  if [ -e "${candidate}" ]; then
    SLN="${candidate}"
    break
  fi
done

if [ -n "${SLN}" ]; then
  green "Ready. Next:"
  dim "  dotnet build ${SLN}"
  dim "  dotnet run --project src/AbsenceConcierge.AppHost"
else
  green "Ready — repository baseline only; there is no solution to build yet."
  dim "This is Phase 0 of the plan in README.md. What you CAN run today:"
  dim "  ./scripts/scan-secrets.sh      the CI secret scan, locally"
  dim "  npm install && npm run lint:md the CI docs lint, locally"
fi
