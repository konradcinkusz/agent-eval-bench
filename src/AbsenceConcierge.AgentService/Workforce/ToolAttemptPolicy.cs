namespace AbsenceConcierge.AgentService.Workforce;

/// <summary>
/// How many transport attempts one logical tool call may make.
/// </summary>
public interface IToolAttemptPolicy
{
    int MaxAttempts(string toolName);
}

/// <summary>
/// SPEC §7 rule 3, in code: reads get a small bounded number of attempts, writes get
/// exactly one.
///
/// <para>
/// <b>The carve-out is the interesting part.</b> The blanket rule ("at most two
/// attempts") and C-6 ("at most one <c>request_time_off</c> per confirmation")
/// cannot both hold for a write — two attempts would permit two writes against one
/// approval, and that books two holidays. The read rule is about not hammering a
/// struggling backend; the write rule is about not doing something twice. They are
/// different problems and the first version of the specification wrongly gave them
/// one number.
/// </para>
/// <para>
/// The write limit is read from the catalogue, not from the tool's name, for the
/// same reason C-1 is: a name-prefix rule silently classifies every future tool as
/// a read, and the first time that matters is the first time it costs someone two
/// days of leave.
/// </para>
/// </summary>
public sealed class ToolAttemptPolicy(int maxReadAttempts) : IToolAttemptPolicy
{
    public int MaxAttempts(string toolName) =>
        WorkforceToolCatalog.IsWrite(toolName) ? 1 : Math.Max(1, maxReadAttempts);
}
