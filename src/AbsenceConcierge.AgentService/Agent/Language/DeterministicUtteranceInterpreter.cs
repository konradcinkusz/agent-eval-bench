using System.Text.RegularExpressions;

namespace AbsenceConcierge.AgentService.Agent.Language;

/// <summary>
/// Reads an utterance with rules rather than a model.
///
/// <para>
/// <b>What this is for, stated plainly.</b> It is the interpreter on the gated path
/// — the one Layer 1 runs against on every pull request, with no credentials and no
/// network (SPEC §8.2). That means Layer 1 grades the orchestration and the
/// constraint layer: tool ordering, the confirmation gate, grounding, termination,
/// the absence of internal identifiers. It does <em>not</em> grade language
/// understanding, and this repository does not claim it does. AI-EVALS.md §4 says
/// the same thing from the other direction — "Layer 1 is cheap, fast, and
/// model-independent". Model understanding is graded by Layer 2 and by the keyed
/// nightly matrix, against <see cref="IUtteranceInterpreter"/>'s other
/// implementation, and the two baselines are never merged (ADR-0004).
/// </para>
/// <para>
/// <b>The overfitting risk, and what is done about it.</b> A rule-based reader
/// written against the thirty-two scenarios it will be scored on is a parser fitted
/// to its own test set. The mitigation is structural rather than promised: the
/// rules below are written against grammatical shapes — a bare weekday, "next X",
/// an ordinal with or without a month, a name after "for" versus after "as" — and
/// the unit tests exercise sentences that appear in no scenario. It remains a
/// smaller claim than a model makes, and the honest reading of a green Layer 1 is
/// "the machinery works", not "the agent understands English".
/// </para>
/// </summary>
public sealed partial class DeterministicUtteranceInterpreter : IUtteranceInterpreter
{
    public const string InterpreterName = "deterministic";

    public string Name => InterpreterName;

    public ValueTask<Intent> InterpretAsync(string utterance, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(Interpret(utterance));

    public static Intent Interpret(string utterance)
    {
        if (string.IsNullOrWhiteSpace(utterance))
        {
            return new Intent(IntentKind.Unclear, null, null, null, ClaimsPriorApproval: false);
        }

        var kind = Classify(utterance);
        var claimsApproval = ClaimsPriorApprovalPattern().IsMatch(utterance);

        // Out-of-scope kinds still carry what was extracted. A refusal that knows
        // the user also asked about Thursday can say so, and O-6 requires the agent
        // to carry on with a booking task that is in flight rather than dropping it.
        return new Intent(
            kind,
            DateExpressionParser.Parse(utterance),
            LeaveTypeHint(utterance),
            FindPerson(utterance),
            claimsApproval);
    }

    /// <summary>
    /// Order is the specification here. Approval is checked before cancellation
    /// because "approve the cancellation" is an approval; medical advice is checked
    /// before a booking request because "should I take the day off sick?" contains
    /// a perfectly good booking sentence inside a question the agent must not answer.
    /// </summary>
    private static IntentKind Classify(string utterance)
    {
        if (PayrollPattern().IsMatch(utterance))
        {
            return IntentKind.PayrollOrPolicyQuestion;
        }

        if (ApprovalPattern().IsMatch(utterance))
        {
            return IntentKind.ApproveOrRejectLeave;
        }

        if (CancelOrEditPattern().IsMatch(utterance))
        {
            return IntentKind.CancelOrEditBooking;
        }

        if (MedicalAdvicePattern().IsMatch(utterance))
        {
            return IntentKind.MedicalAdvice;
        }

        return TimeOffRequestPattern().IsMatch(utterance)
            ? IntentKind.RequestTimeOff
            : IntentKind.Unclear;
    }

    /// <summary>
    /// The user's own word for the leave, or null when they gave none.
    ///
    /// The null is load-bearing and is not the same as "no match". A user who said
    /// nothing about the reason gets the default type; a user who said "funeral"
    /// gets a question, because the retrieved list has no such type and choosing the
    /// closest one for them is B-3's named failure.
    /// </summary>
    private static string? LeaveTypeHint(string utterance)
    {
        var match = LeaveReasonPattern().Match(utterance);

        if (!match.Success)
        {
            return null;
        }

        // Normalised to the word the user is understood to have said, so that
        // "signed off", "not feeling well" and "ill" all reach the leave-type
        // matcher as one hint rather than three.
        var word = match.Value.Trim();

        if (IllnessPattern().IsMatch(word))
        {
            return "sick";
        }

        if (VacationPattern().IsMatch(word))
        {
            return "vacation";
        }

        return word;
    }

    /// <summary>
    /// Finds a person in the sentence and, more importantly, decides what they are
    /// doing in it. The role is the whole point: "for Sam" is a request the agent
    /// must refuse, "as Sam" is a date the agent must resolve, and "Sam is covering
    /// for me" is an ordinary sentence that must not be treated as either (O-3).
    /// </summary>
    private static PersonReference? FindPerson(string utterance)
    {
        var subject = SubjectMarkerPattern().Match(utterance);

        if (subject.Success)
        {
            var name = subject.Groups["name"].Value.Trim();

            if (name.Length > 0 && !IsNotAName(name))
            {
                return new PersonReference(name, PersonRole.Subject);
            }
        }

        var reference = DateReferenceMarkerPattern().Match(utterance);

        if (reference.Success)
        {
            var name = reference.Groups["name"].Value.Trim();

            if (name.Length > 0 && !IsNotAName(name))
            {
                return new PersonReference(name, PersonRole.DateReference);
            }
        }

        foreach (Match match in NameLikePattern().Matches(utterance))
        {
            var name = match.Groups["name"].Value.Trim();

            if (IsNotAName(name) || IsSentenceInitial(utterance, match.Index))
            {
                continue;
            }

            return new PersonReference(name, PersonRole.Mention);
        }

        return null;
    }

    /// <summary>
    /// Weekdays, months and pronouns get capitalised in ordinary English and are
    /// not people. Everything here would otherwise become a spurious directory
    /// lookup on "August" or "Friday".
    /// </summary>
    private static bool IsNotAName(string candidate) =>
        NotANamePattern().IsMatch(candidate);

    /// <summary>
    /// A capitalised word at the start of a sentence is capitalised because it is at
    /// the start of a sentence. Without this, "My manager already approved it" makes
    /// "My" a colleague.
    /// </summary>
    private static bool IsSentenceInitial(string utterance, int index)
    {
        for (var i = index - 1; i >= 0; i--)
        {
            var character = utterance[i];

            if (char.IsWhiteSpace(character))
            {
                continue;
            }

            return character is '.' or '!' or '?' or ';' or ':';
        }

        return true;
    }

    [GeneratedRegex(
        @"\b(salary|payroll|pay\s*slip|paid\b.*\b(while|during)|how\s+much\b.*\b(pay|paid|salary)|contract|accrual|accrue)",
        RegexOptions.IgnoreCase)]
    private static partial Regex PayrollPattern();

    /// <remarks>
    /// Base forms only — <c>approve</c>, never <c>approved</c>. The past tense is
    /// almost always someone reporting that an approval happened elsewhere ("my
    /// manager already approved it"), which is a claim to weigh, not a request to
    /// act on. Reading it as a request is how <c>adv-002</c> would turn a
    /// social-engineering sentence into a refusal and hide the behaviour it exists
    /// to test — the agent must draft and stop at the gate, not decline.
    /// </remarks>
    [GeneratedRegex(
        @"\b(approve|reject|decline|sign\s+off\s+on)\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex ApprovalPattern();

    [GeneratedRegex(
        @"\b(cancel|delete|remove|undo|amend|reschedule|move)\b.*\b(leave|booking|holiday|vacation|time\s+off|request|day)",
        RegexOptions.IgnoreCase)]
    private static partial Regex CancelOrEditPattern();

    [GeneratedRegex(
        @"\b(do\s+you\s+think\s+i\s+should|should\s+i\b|am\s+i\s+(too\s+)?(ill|sick)|is\s+it\s+(bad|serious)|what\s+should\s+i\s+do)",
        RegexOptions.IgnoreCase)]
    private static partial Regex MedicalAdvicePattern();

    [GeneratedRegex(
        @"\b(book|take|request|put\s+me\s+down|put\s+.*\s+in|off\b|leave\b|sick\b|holiday|vacation|absent)",
        RegexOptions.IgnoreCase)]
    private static partial Regex TimeOffRequestPattern();

    [GeneratedRegex(
        @"\b(already\s+approved|pre-?approved|skip\s+the\s+confirmation|no\s+need\s+to\s+(ask|confirm)|do\s+not\s+need\s+to\s+ask|without\s+asking)",
        RegexOptions.IgnoreCase)]
    private static partial Regex ClaimsPriorApprovalPattern();

    [GeneratedRegex(
        @"\b(sick|ill|unwell|flu|signed\s+off|not\s+feeling\s+\w+|vacation|holiday|annual\s+leave|parental|maternity|paternity|unpaid|funeral|bereavement|wedding|moving\s+house)\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex LeaveReasonPattern();

    [GeneratedRegex(@"^(sick|ill|unwell|flu|signed\s+off|not\s+feeling)", RegexOptions.IgnoreCase)]
    private static partial Regex IllnessPattern();

    [GeneratedRegex(@"^(vacation|holiday|annual\s+leave)", RegexOptions.IgnoreCase)]
    private static partial Regex VacationPattern();

    [GeneratedRegex(
        @"\b(?:for|on\s+behalf\s+of)\s+(?<name>[A-Z][a-z]+(?:\s+[A-Z][a-z]+)?)\b",
        RegexOptions.None)]
    private static partial Regex SubjectMarkerPattern();

    [GeneratedRegex(@"\bas\s+(?<name>[A-Z][a-z]+(?:\s+[A-Z][a-z]+)?)\b", RegexOptions.None)]
    private static partial Regex DateReferenceMarkerPattern();

    [GeneratedRegex(@"\b(?<name>[A-Z][a-z]+(?:\s+[A-Z][a-z]+)?)\b", RegexOptions.None)]
    private static partial Regex NameLikePattern();

    /// <remarks>
    /// Weekdays and months only, plus the two words an injection payload uses to
    /// address the agent. Deliberately not a list of the fixture's team names: a
    /// stop list grown from the corpus is a parser fitted to its own test set, and
    /// the role markers above already resolve the case that would need it.
    /// </remarks>
    [GeneratedRegex(
        @"^(Sunday|Monday|Tuesday|Wednesday|Thursday|Friday|Saturday|January|February|March|April|May|June|July|August|September|October|November|December|Assistant|System)\b",
        RegexOptions.None)]
    private static partial Regex NotANamePattern();
}
