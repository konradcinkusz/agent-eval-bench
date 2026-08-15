using System.Globalization;
using System.Text;
using AbsenceConcierge.AgentService.Agent.Time;
using AbsenceConcierge.AgentService.Telemetry;

namespace AbsenceConcierge.AgentService.Agent.Language;

/// <summary>
/// Composes the reply from the turn's typed state, without a model.
///
/// <para>
/// Every sentence below is built from a value the trace already carries: the
/// resolved dates, the retrieved leave-type name, the working-day count, the
/// excluded days, the degradation notes, the tool's own returned status. Nothing is
/// restated from the request — B-10 is explicit that after a write the agent reports
/// what the tool returned, never a restatement of what it asked for, because those
/// two sentences are identical right up until the moment the write fails.
/// </para>
/// <para>
/// English only, as SPEC §9 records. That is a real limitation for a Barcelona
/// reader and it is written down rather than glossed.
/// </para>
/// </summary>
public sealed class DeterministicReplyComposer : IReplyComposer
{
    public const string ComposerName = "deterministic";

    private static readonly CultureInfo Display = CultureInfo.GetCultureInfo("en-GB");

    public ValueTask<string> ComposeAsync(
        AgentTurnContext context,
        string outcome,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(Compose(context, outcome));

    /// <summary>
    /// The synchronous body. Public because this composer genuinely is synchronous
    /// and a caller that knows which one it holds should not have to await nothing.
    /// </summary>
    public string Compose(AgentTurnContext context, string outcome)
    {
        ArgumentNullException.ThrowIfNull(context);

        return outcome switch
        {
            AgentDiagnostics.TurnOutcomes.Refused => Refusal(context),
            AgentDiagnostics.TurnOutcomes.Cancelled => Cancellation(context),
            AgentDiagnostics.TurnOutcomes.ClarificationRequested => Question(context),
            AgentDiagnostics.TurnOutcomes.Degraded => Degraded(context),
            AgentDiagnostics.TurnOutcomes.ConfirmationPending => Confirmation(context),
            AgentDiagnostics.TurnOutcomes.Completed => Completion(context),
            _ => "I could not complete that. Nothing has been submitted.",
        };
    }

    /// <summary>
    /// O-7's refusal names the capability in plain language and never the permission
    /// string. "You lack <c>timeoff:request</c>" satisfies a naive reading of the
    /// refusal requirement while being exactly the leak C-3 exists to prevent.
    /// </summary>
    private static string Refusal(AgentTurnContext context) => context.RefusalRule switch
    {
        AgentDiagnostics.RefusalRules.ApprovalIsAManagerAction =>
            "Approving or rejecting time off is a manager action, and it is not something I can do. "
            + "I can help you create a request for yourself.",

        AgentDiagnostics.RefusalRules.CannotModifyBookings =>
            "I can create new time-off requests, but I cannot change or cancel one that already exists. "
            + "Your HR system is the place to do that.",

        AgentDiagnostics.RefusalRules.OnlyForTheSignedInUser =>
            "I can only request time off for you, not for a colleague. They will need to file it themselves.",

        AgentDiagnostics.RefusalRules.PayrollBelongsToHr =>
            "Pay and policy questions are outside what I can answer — HR will be able to tell you. "
            + "I can help with booking the time off itself.",

        AgentDiagnostics.RefusalRules.NoMedicalJudgement =>
            "I am not able to advise on whether you are unwell enough to take a day off; that is between "
            + "you and a doctor. If you decide to take sick leave, tell me the days and I will draft the request.",

        AgentDiagnostics.RefusalRules.MissingCapability =>
            "Your account is not able to request time off. Your manager or HR can raise it for you.",

        _ => "That is not something I can do.",
    };

    private static string Cancellation(AgentTurnContext context)
    {
        var draft = context.Draft;

        return draft is null
            ? "Understood — nothing has been submitted."
            : $"Understood — I have not submitted anything. The {DisplayText.Name(draft.LeaveType.Name)} request for "
              + $"{Range(draft.StartDate, draft.EndDate)} has been discarded. Tell me what to change and I will redraft it.";
    }

    private static string Question(AgentTurnContext context) => context.ClarificationReason switch
    {
        AgentDiagnostics.ClarificationReasons.AmbiguousDate => AmbiguousDateQuestion(context),

        AgentDiagnostics.ClarificationReasons.NonWorkingDay => NonWorkingDayQuestion(context),

        AgentDiagnostics.ClarificationReasons.DateInThePast =>
            "That date has already passed, so I have not drafted anything. Which upcoming days did you mean?",

        AgentDiagnostics.ClarificationReasons.AmbiguousEmployee => AmbiguousEmployeeQuestion(context),

        AgentDiagnostics.ClarificationReasons.DatesFromAnotherPerson =>
            "I can only see your own time off, so I cannot look up a colleague's dates. "
            + "Which days would you like to book?",

        AgentDiagnostics.ClarificationReasons.NoMatchingLeaveType => NoMatchingLeaveTypeQuestion(context),

        AgentDiagnostics.ClarificationReasons.ConflictingBooking => ConflictQuestion(context),

        AgentDiagnostics.ClarificationReasons.NoDateGiven =>
            "Happy to put that in — which days would you like to take?",

        _ => "I need one more detail before I can draft that. Which days did you have in mind?",
    };

    private static string AmbiguousDateQuestion(AgentTurnContext context)
    {
        var readings = context.Dates?.Readings ?? [];

        if (readings.Count < 2)
        {
            return "I am not certain which dates you meant, so I have not drafted anything. "
                + "Could you give me the dates?";
        }

        // The candidates go in the question. "Which Friday?" is not a useful
        // question; "the 21st or the 28th?" is one the user can answer in a word.
        var options = string.Join(" or ", readings.Select(Long));

        return $"That could mean {options}, and I would rather not guess. Which did you mean? "
            + "I have not drafted anything yet.";
    }

    private static string NonWorkingDayQuestion(AgentTurnContext context)
    {
        var start = context.Dates?.Start;

        if (start is null)
        {
            return "That day is not a working day, so there is nothing to book. Which days did you mean?";
        }

        var holiday = context.Calendar.HolidayName(start.Value);

        return holiday is null
            ? $"{Long(start.Value)} is not a working day, so there is nothing to book. Did you mean a different day?"
            : $"{Long(start.Value)} is {DisplayText.Name(holiday)}, a company holiday, so there is nothing to book. "
              + "Did you mean a different day?";
    }

    private static string AmbiguousEmployeeQuestion(AgentTurnContext context)
    {
        // Distinguished by team, which is what makes the question answerable — the
        // whole reason the fixture contains two colleagues with the same name.
        var teams = context.EmployeeMatches
            .Select(match => $"{DisplayText.Name(match.DisplayName)} in {DisplayText.Name(match.Team)}")
            .ToList();

        return teams.Count == 0
            ? "I could not find that colleague. Which days would you like to book?"
            : $"There is more than one match: {string.Join(", and ", teams)}. Which one did you mean? "
              + "I have not drafted anything.";
    }

    private static string NoMatchingLeaveTypeQuestion(AgentTurnContext context)
    {
        var names = context.LeaveTypes.Select(type => DisplayText.Name(type.Name)).ToList();

        return names.Count == 0
            ? "I could not find a kind of leave that fits. Which would you like to use?"
            : $"None of the leave types available to you covers that. The options are: "
              + $"{string.Join(", ", names)}. Which would you like me to use?";
    }

    private static string ConflictQuestion(AgentTurnContext context)
    {
        var clash = context.ConflictingLeaves[0];
        var requested = context.Dates!;

        return $"You already have time off booked from {Short(clash.StartDate)} to {Short(clash.EndDate)}, "
            + $"which overlaps {Range(requested.Start!.Value, requested.End!.Value)}. "
            + "I have not drafted anything — would you like to change the dates, or is the overlap intended?";
    }

    private static string Degraded(AgentTurnContext context)
    {
        var text = new StringBuilder();

        foreach (var note in context.Degradations)
        {
            text.Append(DescribeDegradation(note)).Append(' ');
        }

        // §7 rule 5: a failed read before the gate does not cancel the draft, it
        // annotates it. The draft still goes out, marked unverified.
        if (context.Draft is { } draft && context.ApprovedDraft is null)
        {
            text.Append(DraftSummary(draft)).Append(' ')
                .Append("Shall I submit it?");
        }
        else if (context.WriteResult is null && context.Draft is null)
        {
            text.Append("Nothing has been submitted.");
        }

        return text.ToString().TrimEnd();
    }

    private static string DescribeDegradation(DegradationNote note) => note switch
    {
        { Phase: AgentDiagnostics.DegradationPhases.LeaveTypeLookup, Kind: AgentDiagnostics.DegradationKinds.Empty } =>
            "The list of leave types came back empty, so I have nothing to book against and I will not guess at one. "
            + "Nothing has been submitted.",

        { Phase: AgentDiagnostics.DegradationPhases.LeaveTypeLookup } =>
            "I could not retrieve the kinds of leave available to you, and I will not guess at one from memory. "
            + "Nothing has been submitted — please try again shortly.",

        { Phase: AgentDiagnostics.DegradationPhases.ConflictCheck } =>
            "I could not check your existing bookings, so I have not been able to confirm this does not clash "
            + "with time off you already have.",

        { Phase: AgentDiagnostics.DegradationPhases.EmployeeLookup } =>
            "I could not reach the directory just now.",

        { Phase: AgentDiagnostics.DegradationPhases.Submission, Kind: AgentDiagnostics.DegradationKinds.Timeout } =>
            // §7.2. The one thing that must not be said is that it definitely did or
            // did not happen. Both are claims this agent cannot support.
            "The submission timed out and I do not know whether it was recorded. Please check your time off "
            + "before requesting these days again, so you do not end up with two requests.",

        { Phase: AgentDiagnostics.DegradationPhases.Submission } =>
            "The request was not submitted — the system rejected it. Nothing has been booked, so please try again.",

        _ => "Something did not work as expected, and nothing has been submitted.",
    };

    private static string Confirmation(AgentTurnContext context)
    {
        var draft = context.Draft!;
        var text = new StringBuilder(DraftSummary(draft));

        if (context.Intent?.ClaimsPriorApproval == true)
        {
            // Answering the argument rather than ignoring it. The user asked to skip
            // this step, and silence would read as not having noticed.
            text.Append(" I still need your confirmation here before anything is submitted — that is the "
                + "one step I cannot skip.");
        }

        text.Append(" Shall I submit it?");
        return text.ToString();
    }

    private static string DraftSummary(LeaveDraft draft)
    {
        var text = new StringBuilder();

        text.Append(CultureInfo.InvariantCulture, $"Here is what I have: {DisplayText.Name(draft.LeaveType.Name)}, ")
            .Append(Range(draft.StartDate, draft.EndDate))
            .Append(CultureInfo.InvariantCulture, $", {Days(draft.WorkingDays)}.");

        if (draft.ExcludedDays.Count > 0)
        {
            text.Append(' ').Append(ExcludedSentence(draft.ExcludedDays));
        }

        if (draft.AttachmentRequired)
        {
            text.Append(" Because this runs past the limit for self-certified sick leave, a medical "
                + "certificate will be needed.");
        }

        if (string.Equals(draft.ConflictCheck, AgentDiagnostics.ConflictCheckStates.NotRun, StringComparison.Ordinal))
        {
            text.Append(" I have not been able to verify it against your existing bookings.");
        }

        return text.ToString();
    }

    /// <summary>
    /// Names the days that were not counted, and why. B-11 asks for exactly this:
    /// the reader is approving a number, and a number they cannot reconstruct is a
    /// number they cannot query.
    /// </summary>
    private static string ExcludedSentence(IReadOnlyList<ExcludedDay> excluded)
    {
        var weekend = excluded.Where(day => day.Reason == WorkingCalendar.WeekendReason).ToList();
        var holidays = excluded.Where(day => day.Reason == WorkingCalendar.HolidayReason).ToList();

        var parts = new List<string>();

        if (weekend.Count > 0)
        {
            parts.Add($"{Join(weekend.Select(day => Short(day.Date)))} (the weekend)");
        }

        foreach (var holiday in holidays)
        {
            parts.Add($"{Short(holiday.Date)} ({DisplayText.Name(holiday.Label)}, a company holiday)");
        }

        return $"I have not counted {Join(parts)}.";
    }

    private static string Join(IEnumerable<string> items)
    {
        var list = items.ToList();

        return list.Count switch
        {
            0 => string.Empty,
            1 => list[0],
            _ => $"{string.Join(", ", list.Take(list.Count - 1))} or {list[^1]}",
        };
    }

    /// <summary>
    /// B-10, in one method: everything here comes from the tool's returned value.
    /// The request identifier is deliberately absent — it matches C-3's pattern and
    /// means nothing to the person reading it.
    /// </summary>
    private static string Completion(AgentTurnContext context)
    {
        if (context.WriteResult?.Value is not { } written)
        {
            return "That is done.";
        }

        var status = written.Status switch
        {
            "pending_approval" => "It is waiting on approval",
            "approved" => "It has been approved",
            _ => $"Its status is {written.Status.Replace('_', ' ')}",
        };

        return $"Submitted: {Range(written.StartDate, written.EndDate)}. {status}.";
    }

    private static string Days(int count) => count == 1 ? "1 working day" : $"{count} working days";

    private static string Range(DateOnly start, DateOnly end) =>
        start == end ? Long(start) : $"{Long(start)} to {Long(end)}";

    private static string Long(DateOnly date) => date.ToString("dddd d MMMM yyyy", Display);

    private static string Short(DateOnly date) => date.ToString("dddd d MMMM", Display);
}
