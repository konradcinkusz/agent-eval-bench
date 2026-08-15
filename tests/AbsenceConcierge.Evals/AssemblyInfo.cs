// ─────────────────────────────────────────────────────────────────────────────
//  One scenario at a time.
//
//  Every scenario builds a TracerProvider with an in-memory exporter, and
//  ActivitySource listeners are process-global: two providers alive at once both
//  receive every activity, so a scenario running in parallel with another would see
//  the other's tool spans in its own trace. ScenarioRunner scopes its reads to its
//  own trace id, so this is belt as well as braces — but a suite whose results
//  depend on which scenarios happened to overlap is not a suite, and the whole
//  corpus runs in seconds.
// ─────────────────────────────────────────────────────────────────────────────

using Xunit.Sdk;
using Xunit.v3;

[assembly: Parallelization(Mode = ParallelMode.None)]
