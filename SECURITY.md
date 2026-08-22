# Security policy

## Reporting a vulnerability

Please report vulnerabilities privately, through
[GitHub's private vulnerability reporting](https://github.com/konradcinkusz/agent-eval-bench/security/advisories/new)
— not in a public issue, and not in a pull request whose diff is the disclosure.

You can expect an acknowledgement within a week. If the report is valid, the fix
lands with a regression test — in this repository that usually means a scenario,
because a vulnerability in the agent's behaviour is precisely what the eval
bench exists to catch.

## What counts as a vulnerability here

The interesting surface of this repository is deliberate and small:

- **The confirmation gate.** Any way to reach `request_time_off` without a
  single-use token released by an explicit human approval — through the agent
  loop, the HTTP surface, a replayed token, or a race — is the highest-severity
  report this repository can receive (C-6 in [`docs/SPEC.md`](docs/SPEC.md)).
- **Injection that moves the agent.** A hostile string in user input *or in
  tool results* that causes a tool call the spec forbids. The adversarial
  scenarios under `evals/scenarios/adversarial/` show the shape
  ([`evals/README.md`](evals/README.md) is the tour); a new one that lands is a
  report and its regression test in one file.
- **The demo's spend controls.** A bypass of the access code, the daily budget,
  or the per-client quota on a live deployment.
- **The showcase page.** Anything that makes data execute — the page renders
  hostile fixture content as text, on purpose, and an e2e test pins it.

## What does not need a private report

- Findings that require credentials this repository does not ship. There are
  none: the zero-credential default is a designed property.
- The fictional fixture data. None of it is real; the templates and review
  both hold that line.
- Dependency advisories already visible to Dependabot — those arrive as pull
  requests on their own.

## Supported versions

There are no release branches; `main` is the supported version, and a fix lands
there first.
