# scripts/

Operational scripts, following the conventions in
[`REPO-BASELINE.md`](https://github.com/konradcinkusz/architecture-standards/blob/main/docs/guides/REPO-BASELINE.md)
§4: self-sufficient, each runnable alone, each mirroring a CI job where one exists.

| Script | What it does | Mirrors |
|---|---|---|
| `setup.sh` | First-time onboarding: prerequisites, hooks, `.env`, labelled optional integrations | — |
| `install-hooks.sh` | Points `core.hooksPath` at the committed `hooks/` directory | — |
| `scan-secrets.sh` | gitleaks over full history, staged changes, or the working tree | `.github/workflows/secret-scan.yml` |
| `hooks/pre-commit` | Refuses a commit whose staged changes contain a secret | same job, one commit earlier |
| `check-links.mjs` | Resolves every relative link in every Markdown file, tracked or not | `ci.yml` → lint-docs |
| `validate-scenarios.mjs` | Schema, corpus invariants and assertion discipline over `evals/scenarios/` | `ci.yml` → lint-docs |
| `validate-agent-definitions.mjs` | Schema, one version in three places, and the tool catalogue against the service's source | `ci.yml` → lint-docs |
| `check-change-coupling.mjs` | A behaviour change is written down; a changed measuring stick is versioned | `ci.yml` → coupling |
| `eval-comment.mjs` | Renders the pull request's eval comment — the diff against the baseline | `ci.yml` → build-test |
| `provision-azure.sh` | Deploys `infra/azure/` (OpenAI `composer` + `judge` deployments, App Insights) and prints the wiring commands | `azure.yml` → provision |

```bash
./scripts/setup.sh              # run once per clone
./scripts/setup.sh --check      # prerequisites only, changes nothing
./scripts/scan-secrets.sh       # exactly what CI runs
./scripts/scan-secrets.sh --staged

node scripts/validate-scenarios.mjs
node scripts/validate-agent-definitions.mjs
node scripts/check-change-coupling.mjs origin/main
node scripts/eval-comment.mjs    # needs TestResults/ from a `dotnet test` run
```

## Why bash and not PowerShell

The rest of the estate writes runbook scripts in PowerShell, matching a
Windows-first development machine. This repository is public, its CI runs on
Ubuntu, and its intended readers are asked to judge it without installing
anything — so the scripts are POSIX shell, which every reviewer already has. The
guide mandates *one* setup script per repo, not a language, so this is a choice
rather than a deviation; it is recorded here so the inconsistency across repos is
deliberate and visible.

## Environment variables

Every variable this repository reads is documented once, with its tier and what
degrades without it, in [`../secrets.env.example`](../secrets.env.example). That
file is the single source of truth; nothing here re-lists it, because two lists
of environment variables means one of them is wrong.

The short version: **all of them are optional.** No script in this directory
takes a secret, and none will run without one.

## Conventions these scripts follow

- **Self-sufficient.** Each resolves the repository root itself and runs alone.
  No script sources another for anything but an explicit sub-step.
- **Secrets from the environment or not at all.** No script accepts a credential
  as an argument (it would land in shell history) or carries one inline. The
  estate's recorded incident is exactly that: live credentials pasted into a
  tracked helper script because inline literals were the path of least resistance.
- **Fail fast, naming what is missing.** `set -euo pipefail` everywhere, and a
  missing prerequisite is reported by name with an install pointer, never as a
  stack trace.
- **A skipped optional step says what degrades.** "Optional" without a
  consequence is not a decision the reader can make.

## Troubleshooting

Keyed on the literal text you will see, because that is what gets pasted into a
search box.

| What you see | What it means | Fix |
|---|---|---|
| `pre-commit: no secret scanner available — refusing to commit.` | Neither `gitleaks` nor a running Docker daemon was found. The hook fails rather than waving the commit through. | Install gitleaks, or start Docker. `git commit --no-verify` bypasses it, and CI will still block. |
| `.NET SDK 9.x found, but this repository targets net10.0.` | `global.json` pins the SDK band. | Install the .NET 10 SDK. |
| `scan-secrets: .gitleaks.toml missing from the repository root.` | The scanner configuration was deleted or you are running from outside the repo. | Restore `.gitleaks.toml`; it is not optional (P5). |
| `bash: ./scripts/setup.sh: /bin/bash^M: bad interpreter` | The file was checked out with CRLF line endings. | `.gitattributes` forces LF on `*.sh`; re-clone or run `git add --renormalize .`. |
| `unknown flag: --staged` from gitleaks | A gitleaks old enough to predate the flag. | Upgrade gitleaks; the scripts already handle the v8.19 `protect` → `git` rename. |
