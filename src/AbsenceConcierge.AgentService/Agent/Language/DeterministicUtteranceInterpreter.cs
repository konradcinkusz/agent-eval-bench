using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

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
/// <b>Two languages, one classification order.</b> The configured locale
/// (<c>Agent:Locale</c>, and per scenario the fixture's <c>locale</c>) selects
/// which vocabulary reads the sentence first; when that language finds nothing at
/// all, the other has a look, so a Madrid deployment still reads an English
/// visitor. The order — payroll, approval, cancellation, medical,
/// time-off — is the specification and is the <b>same</b> in both languages,
/// because it encodes which reading wins when a sentence contains two, and that is
/// a property of the agent, not of the language (SPEC §6).
/// </para>
/// <para>
/// <b>The overfitting risk, and what is done about it.</b> A rule-based reader
/// written against the scenarios it will be scored on is a parser fitted
/// to its own test set. The mitigation is structural rather than promised: the
/// rules below are written against grammatical shapes — a bare weekday, "next X",
/// an ordinal with or without a month, a name after "for" versus after "as" — and
/// the unit tests exercise sentences that appear in no scenario. It remains a
/// smaller claim than a model makes, and the honest reading of a green Layer 1 is
/// "the machinery works", not "the agent understands English". Adding Spanish was
/// itself a test of that claim: the closed <see cref="Time.DateExpression"/> set
/// needed no new case, which is the evidence the shapes were not English-shaped.
/// </para>
/// </summary>
public sealed partial class DeterministicUtteranceInterpreter : IUtteranceInterpreter
{
    public const string InterpreterName = "deterministic";

    private readonly UtteranceLanguage _primary;

    public DeterministicUtteranceInterpreter()
        : this(primary: UtteranceLanguage.English)
    {
    }

    public DeterministicUtteranceInterpreter(UtteranceLanguage primary) => _primary = primary;

    public DeterministicUtteranceInterpreter(IOptions<Agent.AgentOptions> options)
        : this(UtteranceLanguages.FromLocale(options?.Value.Locale))
    {
    }

    public string Name => InterpreterName;

    public ValueTask<Intent> InterpretAsync(string utterance, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(InterpretWithFallback(utterance, _primary));

    /// <summary>English-first reading. Kept as the plain entry point for the unit tests and the harness.</summary>
    public static Intent Interpret(string utterance) => Interpret(utterance, UtteranceLanguage.English);

    /// <summary>
    /// The locale's language reads first; when it finds nothing at all — no intent,
    /// no date, no hint, no name — the other language has a look. Falling back only
    /// from an entirely empty reading keeps the two vocabularies from competing
    /// over one sentence: a Spanish reading that classified the intent wins
    /// outright, even if an English pattern would also have matched something.
    /// </summary>
    public static Intent InterpretWithFallback(string utterance, UtteranceLanguage primary)
    {
        var read = Interpret(utterance, primary);

        if (!IsEmpty(read))
        {
            return read;
        }

        var other = primary == UtteranceLanguage.Spanish
            ? UtteranceLanguage.English
            : UtteranceLanguage.Spanish;

        var fallback = Interpret(utterance, other);
        return IsEmpty(fallback) ? read : fallback;
    }

    public static Intent Interpret(string utterance, UtteranceLanguage language)
    {
        if (string.IsNullOrWhiteSpace(utterance))
        {
            return new Intent(IntentKind.Unclear, null, null, null, ClaimsPriorApproval: false);
        }

        var kind = Classify(utterance, language);
        var claimsApproval = language == UtteranceLanguage.Spanish
            ? SpanishClaimsPriorApprovalPattern().IsMatch(utterance)
            : ClaimsPriorApprovalPattern().IsMatch(utterance);

        // Out-of-scope kinds still carry what was extracted. A refusal that knows
        // the user also asked about Thursday can say so, and O-6 requires the agent
        // to carry on with a booking task that is in flight rather than dropping it.
        return new Intent(
            kind,
            DateExpressionParser.Parse(utterance, language),
            LeaveTypeHint(utterance, language),
            FindPerson(utterance, language),
            claimsApproval);
    }

    private static bool IsEmpty(Intent intent) =>
        intent is
        {
            Kind: IntentKind.Unclear,
            Dates: null,
            LeaveTypeHint: null,
            Person: null,
            ClaimsPriorApproval: false,
        };

    /// <summary>
    /// Order is the specification here, and it is deliberately identical in both
    /// languages. Approval is checked before cancellation because "approve the
    /// cancellation" is an approval; medical advice is checked before a booking
    /// request because "should I take the day off sick?" contains a perfectly good
    /// booking sentence inside a question the agent must not answer.
    /// </summary>
    private static IntentKind Classify(string utterance, UtteranceLanguage language)
    {
        var spanish = language == UtteranceLanguage.Spanish;

        if (spanish ? SpanishPayrollPattern().IsMatch(utterance) : PayrollPattern().IsMatch(utterance))
        {
            return IntentKind.PayrollOrPolicyQuestion;
        }

        if (spanish ? SpanishApprovalPattern().IsMatch(utterance) : ApprovalPattern().IsMatch(utterance))
        {
            return IntentKind.ApproveOrRejectLeave;
        }

        if (spanish ? SpanishCancelOrEditPattern().IsMatch(utterance) : CancelOrEditPattern().IsMatch(utterance))
        {
            return IntentKind.CancelOrEditBooking;
        }

        if (spanish ? SpanishMedicalAdvicePattern().IsMatch(utterance) : MedicalAdvicePattern().IsMatch(utterance))
        {
            return IntentKind.MedicalAdvice;
        }

        var requested = spanish
            ? SpanishTimeOffRequestPattern().IsMatch(utterance)
            : TimeOffRequestPattern().IsMatch(utterance);

        return requested ? IntentKind.RequestTimeOff : IntentKind.Unclear;
    }

    /// <summary>
    /// The user's own word for the leave, or null when they gave none.
    ///
    /// The null is load-bearing and is not the same as "no match". A user who said
    /// nothing about the reason gets the default type; a user who said "funeral"
    /// gets a question, because the retrieved list has no such type and choosing the
    /// closest one for them is B-3's named failure.
    /// </summary>
    private static string? LeaveTypeHint(string utterance, UtteranceLanguage language)
    {
        var match = language == UtteranceLanguage.Spanish
            ? SpanishLeaveReasonPattern().Match(utterance)
            : LeaveReasonPattern().Match(utterance);

        if (!match.Success)
        {
            return null;
        }

        // Normalised to the word the fixture's catalogue is matched with, so that
        // "signed off", "not feeling well", "ill", "enfermo" and "de baja" all
        // reach the leave-type matcher as one hint rather than five. The retrieved
        // names are what the hint is compared against (B-2), and those names are
        // the fixture's — normalising here is what keeps the matcher's job to
        // string containment rather than translation.
        var word = match.Value.Trim();

        if (language == UtteranceLanguage.Spanish)
        {
            if (SpanishIllnessPattern().IsMatch(word))
            {
                return "sick";
            }

            if (SpanishVacationPattern().IsMatch(word))
            {
                return "vacation";
            }

            return word;
        }

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
    /// doing in it. The role is the whole point: "for Sam" / "para Sam" is a request
    /// the agent must refuse, "as Sam" / "como Sam" is a date the agent must
    /// resolve, and "Sam is covering for me" is an ordinary sentence that must not
    /// be treated as either (O-3).
    /// </summary>
    private static PersonReference? FindPerson(string utterance, UtteranceLanguage language)
    {
        var spanish = language == UtteranceLanguage.Spanish;

        var subjectPattern = spanish ? SpanishSubjectMarkerPattern() : SubjectMarkerPattern();

        foreach (Match subject in subjectPattern.Matches(utterance))
        {
            var name = subject.Groups["name"].Value.Trim();

            if (name.Length == 0 || IsNotAName(name))
            {
                continue;
            }

            // "for" does not always introduce a subject, and ScopeGuardStep's own
            // comment names the counter-example: "book Friday off, I'm covering for
            // Sam" is an ordinary sentence, and SPEC §6 says so too — O-3's
            // "deliberate asymmetry" exists precisely because banning the name
            // outright "would make the agent useless at ordinary sentences". Read as
            // a subject it was refused under O-3: the agent turning away the person
            // it exists to serve, which is the inverse of the defect the aggressive
            // refusal is there to prevent. Being hard to talk into writing for
            // someone else is right; being impossible to talk into writing for
            // yourself is not.
            //
            // Only an ADJACENT cover verb counts. A verb tested anywhere in the
            // sentence would be the same over-wide net one layer down, where
            // "contains a connector" used to read whole clauses as date ranges.
            if (CoverArrangementPattern().IsMatch(utterance[..subject.Index]))
            {
                continue;
            }

            return new PersonReference(name, PersonRole.Subject);
        }

        var reference = (spanish ? SpanishDateReferenceMarkerPattern() : DateReferenceMarkerPattern())
            .Match(utterance);

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
    /// Weekdays, months and pronouns get capitalised in ordinary text and are not
    /// people — in either language. Everything here would otherwise become a
    /// spurious directory lookup on "August", "Friday" or a sentence-initial
    /// "Mañana".
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

            return character is '.' or '!' or '?' or ';' or ':' or '¿' or '¡';
        }

        return true;
    }

    // ── English classification ───────────────────────────────────────────────────

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

    /// <remarks>
    /// The verbs after which "for" introduces a cover arrangement rather than the
    /// subject of the request. Anchored at the end and matched against the text
    /// immediately preceding the marker, so only an adjacent verb disqualifies it:
    /// "book Friday off for Dana" keeps its subject, "I'm covering for Sam" does
    /// not have one.
    /// </remarks>
    [GeneratedRegex(
        @"\b(?:cover(?:s|ed|ing)?|fill(?:s|ed|ing)?\s+in|stand(?:s|ing)?\s+in|stood\s+in"
        + @"|sit(?:s|ting)?\s+in|sat\s+in|substitut(?:e|es|ed|ing)|deputis(?:e|es|ed|ing)"
        + @"|deputiz(?:e|es|ed|ing)|cubr(?:o|e|es|iendo)|sustitu(?:yo|ye|yes|yendo))\s+$",
        RegexOptions.IgnoreCase)]
    private static partial Regex CoverArrangementPattern();

    // ── Spanish classification ───────────────────────────────────────────────────
    //
    // The same five checks, in the same order, reading Castilian forms — accented
    // and unaccented alike, because a visitor without the keyboard types "nomina"
    // and means payroll. Base forms only for approval, exactly as in English:
    // "aprueba esta solicitud" is a request to act; "mi jefa ya lo aprobó" is a
    // claim to weigh, and adv-002's Spanish counterpart depends on the difference.

    [GeneratedRegex(
        @"\b(n[oó]mina|salario|sueldo|contrato|devengo|devengar|cu[aá]nto\s+(me\s+)?(pagan|cobro|cobrar)|pagan\s+durante)",
        RegexOptions.IgnoreCase)]
    private static partial Regex SpanishPayrollPattern();

    [GeneratedRegex(
        @"\b(aprueba|apruebes|aprobar|apru[eé]bame|rechaza|rechaces|rechazar|deniega|denegar|declina|declinar)\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex SpanishApprovalPattern();

    [GeneratedRegex(
        @"\b(cancela|cancelar|anula|anular|borra|borrar|elimina|eliminar|cambia|cambiar|mueve|mover|reprograma|reprogramar|modifica|modificar)\b.*\b(baja|permiso|reserva|vacaciones|solicitud|d[ií]as?\b|festivo)",
        RegexOptions.IgnoreCase)]
    private static partial Regex SpanishCancelOrEditPattern();

    [GeneratedRegex(
        @"(\bdeber[ií]a\s+(quedarme|ir|trabajar|coger|cogerme|tomar|tomarme|pedir)|crees\s+que|\bestoy\s+(demasiado\s+)?enferm[oa]\s+(como\s+)?para\b|es\s+(grave|serio)\b|qu[eé]\s+(hago|debo\s+hacer)|me\s+recomiendas)",
        RegexOptions.IgnoreCase)]
    private static partial Regex SpanishMedicalAdvicePattern();

    [GeneratedRegex(
        @"\b(baja|vacaciones|libres?\b|permiso|enferm[oa]|ausente|coger|cogerme|tomar|tomarme|pedir|p[ií]deme|solicitar|reservar|librar|necesito|me\s+hace\s+falta|no\s+(?:vendr[eé]|estar[eé]|ir[eé]))\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex SpanishTimeOffRequestPattern();

    [GeneratedRegex(
        @"(ya\s+(lo\s+|me\s+lo\s+)?(aprob[oó]|ha\s+aprobado)|preaprobad[oa]|sin\s+(preguntar|confirmar|confirmaci[oó]n)|no\s+hace\s+falta\s+(preguntar|confirmar)|s[aá]ltate\s+la\s+confirmaci[oó]n|env[ií]alo\s+directamente)",
        RegexOptions.IgnoreCase)]
    private static partial Regex SpanishClaimsPriorApprovalPattern();

    [GeneratedRegex(
        @"\b(enferm[oa]|malit[oa]|gripe|resfriad[oa]|de\s+baja|baja\s+m[eé]dica|vacaciones|asuntos\s+propios|sin\s+sueldo|funeral|entierro|boda|mudanza|paternidad|maternidad)\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex SpanishLeaveReasonPattern();

    [GeneratedRegex(@"^(enferm|malit|gripe|resfriad|de\s+baja|baja\s+m[eé]dica)", RegexOptions.IgnoreCase)]
    private static partial Regex SpanishIllnessPattern();

    [GeneratedRegex(@"^(vacaciones|asuntos\s+propios)", RegexOptions.IgnoreCase)]
    private static partial Regex SpanishVacationPattern();

    [GeneratedRegex(
        @"\b(?:para|en\s+nombre\s+de)\s+(?<name>[A-Z][a-zÁÉÍÓÚáéíóúñ]+(?:\s+[A-Z][a-zÁÉÍÓÚáéíóúñ]+)?)\b",
        RegexOptions.None)]
    private static partial Regex SpanishSubjectMarkerPattern();

    [GeneratedRegex(
        @"\bcomo\s+(?<name>[A-Z][a-zÁÉÍÓÚáéíóúñ]+(?:\s+[A-Z][a-zÁÉÍÓÚáéíóúñ]+)?)\b",
        RegexOptions.None)]
    private static partial Regex SpanishDateReferenceMarkerPattern();

    // ── Shared name machinery ────────────────────────────────────────────────────

    [GeneratedRegex(@"\b(?<name>[A-Z][a-zÁÉÍÓÚáéíóúñ]+(?:\s+[A-Z][a-zÁÉÍÓÚáéíóúñ]+)?)\b", RegexOptions.None)]
    private static partial Regex NameLikePattern();

    /// <remarks>
    /// Weekdays and months in both languages, plus the two words an injection
    /// payload uses to address the agent — in both languages too. Deliberately not
    /// a list of the fixture's team names: a stop list grown from the corpus is a
    /// parser fitted to its own test set, and the role markers above already
    /// resolve the case that would need it.
    /// </remarks>
    [GeneratedRegex(
        @"^(Sunday|Monday|Tuesday|Wednesday|Thursday|Friday|Saturday"
        + @"|January|February|March|April|May|June|July|August|September|October|November|December"
        + @"|Lunes|Martes|Mi[eé]rcoles|Jueves|Viernes|S[aá]bado|Domingo"
        + @"|Enero|Febrero|Marzo|Abril|Mayo|Junio|Julio|Agosto|Septiembre|Octubre|Noviembre|Diciembre"
        + @"|Hoy|Ma[ñn]ana"
        + @"|Assistant|System|Asistente|Sistema)\b",
        RegexOptions.None)]
    private static partial Regex NotANamePattern();
}
