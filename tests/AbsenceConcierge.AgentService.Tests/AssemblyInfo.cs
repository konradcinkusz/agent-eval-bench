// ─────────────────────────────────────────────────────────────────────────────
//  Tests in this assembly run one at a time, and the reason is not performance.
//
//  Every test that reads the trace builds a TracerProvider with an in-memory
//  exporter. ActivitySource *listeners are process-global*: two providers alive at
//  once both receive every activity from AbsenceConcierge.Agent, so a test running
//  in parallel with another sees the other's tool spans in its own list. It shows
//  up as a count assertion that is right on its own and wrong in company — which is
//  precisely how it was found, when a change to an unrelated file "broke" three
//  tests that had passed a commit earlier.
//
//  AgentHarness additionally scopes its reads to its own trace id, so it is correct
//  whether or not this attribute is here; TraceExportTests has no such scoping and
//  is the reason the attribute is. The alternative — re-running until green — is the
//  false regression net TESTING-STRATEGY.md §6 forbids, and it would have hidden a
//  real property of the instrumentation rather than documenting it.
//
//  The whole suite runs in well under a second, so the cost is nothing.
// ─────────────────────────────────────────────────────────────────────────────

using Xunit.Sdk;
using Xunit.v3;

[assembly: Parallelization(Mode = ParallelMode.None)]
