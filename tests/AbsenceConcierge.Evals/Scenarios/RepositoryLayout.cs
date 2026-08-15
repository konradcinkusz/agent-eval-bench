namespace AbsenceConcierge.Evals.Scenarios;

/// <summary>
/// Finds the corpus in the source tree, rather than in a copy beside the binary.
///
/// <para>
/// A copied scenario is a scenario that can be stale, and a suite grading yesterday's
/// corpus while the repository holds today's would report green for a scenario nobody
/// can find. Walking up to the solution file costs a few directory probes once per
/// run and removes the whole class of problem.
/// </para>
/// </summary>
public static class RepositoryLayout
{
    private const string Marker = "AbsenceConcierge.slnx";

    private static readonly Lazy<string> RootDirectory = new(FindRoot);

    public static string Root => RootDirectory.Value;

    public static string ScenariosDirectory => Path.Combine(Root, "evals", "scenarios");

    public static string FixturesDirectory => Path.Combine(Root, "evals", "fixtures");

    public static string BaselinesDirectory => Path.Combine(Root, "evals", "baselines");

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, Marker)))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        // Loudly, with the reason. The alternative is an empty corpus and a run that
        // reports "0 failures" — the worst possible green.
        throw new DirectoryNotFoundException(
            $"Could not find '{Marker}' above '{AppContext.BaseDirectory}'. The eval harness reads "
            + "evals/ from the source tree; it cannot run from a published output that does not "
            + "carry the repository.");
    }
}
