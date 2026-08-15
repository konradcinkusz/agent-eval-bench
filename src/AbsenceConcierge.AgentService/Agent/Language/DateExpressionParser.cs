using System.Globalization;
using System.Text.RegularExpressions;
using AbsenceConcierge.AgentService.Agent.Time;

namespace AbsenceConcierge.AgentService.Agent.Language;

/// <summary>
/// Reads date expressions out of a sentence — English or Spanish, chosen by the
/// caller, one closed <see cref="DateExpression"/> set out the other side.
///
/// <para>
/// It produces a <see cref="DateExpression"/> and stops. It does not know what day
/// it is, it never sees a clock, and it cannot tell you whether "next Friday" is
/// ambiguous — that is <see cref="RelativeDateResolver"/>'s job, against a pinned
/// date. Keeping the two apart is what lets the arithmetic be tested exhaustively
/// without a sentence in sight, and the parsing be tested without a calendar.
/// </para>
/// <para>
/// <b>Written against the grammar, not against the corpus.</b> The scenarios in
/// <c>evals/scenarios/</c> are a sample of the language, not the specification of
/// it, and a parser fitted to its scenario strings would pass Layer 1 while being
/// useless on the next sentence. The shapes below — bare weekdays, "next X" /
/// "X que viene", "X next week" / "X de la semana que viene", days of the month
/// with and without a month, ranges and lists — are the grammar; the unit tests
/// deliberately include sentences that appear in no scenario.
/// </para>
/// <para>
/// <b>Spanish was the test of the seam, and the seam held.</b> The closed
/// expression set gained no case for it: "el viernes que viene" is
/// <see cref="NextWeekdayExpression"/>, "del 3 al 7" is a
/// <see cref="DateSpanExpression"/> over two <see cref="CalendarDayExpression"/>s,
/// and the resolver never learns which language produced them (SPEC §9).
/// </para>
/// </summary>
public static partial class DateExpressionParser
{
    /// <summary>An atom found in the sentence, with where it was found.</summary>
    private sealed record Atom(int Index, int Length, DateExpression Expression, int? Month);

    public static DateExpression? Parse(string utterance) => Parse(utterance, UtteranceLanguage.English);

    public static DateExpression? Parse(string utterance, UtteranceLanguage language)
    {
        if (string.IsNullOrWhiteSpace(utterance))
        {
            return null;
        }

        var atoms = language == UtteranceLanguage.Spanish
            ? FindSpanishAtoms(utterance)
            : FindEnglishAtoms(utterance);

        if (atoms.Count == 0)
        {
            return null;
        }

        // "19 to 21 August" and "del 19 al 21 de agosto" name the month once, at
        // the end. Propagating it backwards is what stops the 19th resolving into a
        // different month from the 21st — an off-by-a-month that produces a
        // confident, well-formed request for the wrong week.
        atoms = PropagateMonth(atoms);

        if (atoms.Count == 1)
        {
            return atoms[0].Expression;
        }

        return IsRange(utterance, atoms, language)
            ? new DateSpanExpression(atoms[0].Expression, atoms[^1].Expression)
            : new DateListExpression([.. atoms.Select(a => a.Expression)]);
    }

    // ── English ──────────────────────────────────────────────────────────────────

    private static List<Atom> FindEnglishAtoms(string utterance)
    {
        var atoms = new AtomCollector();

        // Order matters: the most specific shape claims its text first, so
        // "Thursday the 13th" does not also register a bare "Thursday", and
        // "next Friday" is never read as the coming Friday.
        foreach (Match match in WeekdayNextWeekPattern().Matches(utterance))
        {
            atoms.Claim(match, new WeekdayNextWeekExpression(EnglishWeekday(match.Groups["day"].Value)));
        }

        foreach (Match match in NextWeekdayPattern().Matches(utterance))
        {
            atoms.Claim(match, new NextWeekdayExpression(EnglishWeekday(match.Groups["day"].Value)));
        }

        foreach (Match match in WeekdayWithOrdinalPattern().Matches(utterance))
        {
            var month = EnglishMonthNumber(match.Groups["month"].Value);
            atoms.Claim(
                match,
                new CalendarDayExpression(
                    int.Parse(match.Groups["day"].Value, CultureInfo.InvariantCulture),
                    month,
                    EnglishWeekday(match.Groups["weekday"].Value)),
                month);
        }

        // A bare number is only a day of the month when the sentence gives some
        // other reason to read it as one — an ordinal suffix somewhere, or a named
        // month. "I need 2 days off" must not become the 2nd.
        var bareNumbersAllowed = OrdinalPattern().IsMatch(utterance) || ContainsEnglishMonthName(utterance);

        foreach (Match match in DayOfMonthPattern().Matches(utterance))
        {
            if (match.Groups["suffix"].Value.Length == 0 && !bareNumbersAllowed)
            {
                continue;
            }

            var month = EnglishMonthNumber(match.Groups["month"].Value);
            atoms.Claim(
                match,
                new CalendarDayExpression(
                    int.Parse(match.Groups["day"].Value, CultureInfo.InvariantCulture),
                    month,
                    null),
                month);
        }

        foreach (Match match in WeekdayPattern().Matches(utterance))
        {
            atoms.Claim(match, new ComingWeekdayExpression(EnglishWeekday(match.Groups["day"].Value)));
        }

        foreach (Match match in TodayPattern().Matches(utterance))
        {
            atoms.Claim(match, new TodayExpression());
        }

        foreach (Match match in TomorrowPattern().Matches(utterance))
        {
            atoms.Claim(match, new TomorrowExpression());
        }

        return atoms.Sorted();
    }

    // ── Spanish ──────────────────────────────────────────────────────────────────

    private static List<Atom> FindSpanishAtoms(string utterance)
    {
        var atoms = new AtomCollector();

        // Same discipline as English: most specific first. "el viernes de la
        // semana que viene" must not also register "el viernes que viene" or a
        // bare "viernes".
        foreach (Match match in SpanishWeekdayNextWeekPattern().Matches(utterance))
        {
            atoms.Claim(match, new WeekdayNextWeekExpression(SpanishWeekday(match.Groups["day"].Value)));
        }

        foreach (Match match in SpanishNextWeekdayPattern().Matches(utterance))
        {
            atoms.Claim(match, new NextWeekdayExpression(SpanishWeekday(match.Groups["day"].Value)));
        }

        foreach (Match match in SpanishProximoWeekdayPattern().Matches(utterance))
        {
            atoms.Claim(match, new NextWeekdayExpression(SpanishWeekday(match.Groups["day"].Value)));
        }

        foreach (Match match in SpanishWeekdayWithDayPattern().Matches(utterance))
        {
            var month = SpanishMonthNumber(match.Groups["month"].Value);
            atoms.Claim(
                match,
                new CalendarDayExpression(
                    int.Parse(match.Groups["day"].Value, CultureInfo.InvariantCulture),
                    month,
                    SpanishWeekday(match.Groups["weekday"].Value)),
                month);
        }

        // Spanish has no ordinal suffix to lean on — "el 26" is the ordinary form.
        // A bare number therefore needs an article before it or a month after it:
        // "necesito 2 días libres" must not become the 2nd. The article is a
        // lookbehind so the atom starts at the digit and the words between two
        // atoms — "del 3 al 7" — keep their connector for the range check below.
        foreach (Match match in SpanishDayOfMonthPattern().Matches(utterance))
        {
            var month = SpanishMonthNumber(match.Groups["month"].Value);
            atoms.Claim(
                match,
                new CalendarDayExpression(
                    int.Parse(match.Groups["day"].Value, CultureInfo.InvariantCulture),
                    month,
                    null),
                month);
        }

        foreach (Match match in SpanishWeekdayPattern().Matches(utterance))
        {
            atoms.Claim(match, new ComingWeekdayExpression(SpanishWeekday(match.Groups["day"].Value)));
        }

        foreach (Match match in SpanishTodayPattern().Matches(utterance))
        {
            atoms.Claim(match, new TodayExpression());
        }

        foreach (Match match in SpanishTomorrowPattern().Matches(utterance))
        {
            atoms.Claim(match, new TomorrowExpression());
        }

        return atoms.Sorted();
    }

    // ── Shared machinery ─────────────────────────────────────────────────────────

    /// <summary>
    /// Collects atoms, first claim wins. Two patterns matching overlapping text is
    /// the ordinary case — "next Friday" contains "Friday" — and the ordering of
    /// the claims above is what resolves it.
    /// </summary>
    private sealed class AtomCollector
    {
        private readonly List<Atom> _atoms = [];
        private readonly List<(int Start, int End)> _claimed = [];

        public void Claim(Match match, DateExpression expression, int? month = null)
        {
            foreach (var (start, end) in _claimed)
            {
                if (match.Index < end && start < match.Index + match.Length)
                {
                    return;
                }
            }

            _claimed.Add((match.Index, match.Index + match.Length));
            _atoms.Add(new Atom(match.Index, match.Length, expression, month));
        }

        public List<Atom> Sorted()
        {
            _atoms.Sort((left, right) => left.Index.CompareTo(right.Index));
            return _atoms;
        }
    }

    private static List<Atom> PropagateMonth(List<Atom> atoms)
    {
        var month = atoms.LastOrDefault(a => a.Month is not null)?.Month;

        if (month is null)
        {
            return atoms;
        }

        return
        [
            .. atoms.Select(atom => atom switch
            {
                { Month: null, Expression: CalendarDayExpression day } =>
                    atom with { Expression = day with { Month = month }, Month = month },
                _ => atom,
            }),
        ];
    }

    /// <summary>
    /// Whether the words between the first and last atom make this a range rather
    /// than a list. "the 20th to the 24th" and "del 20 al 24" are spans; "the 26th,
    /// 27th and 28th" is a list that happens to be contiguous, and the difference
    /// matters because the resolver refuses to close a gap in a list.
    /// </summary>
    private static bool IsRange(string utterance, List<Atom> atoms, UtteranceLanguage language)
    {
        var start = atoms[0].Index + atoms[0].Length;
        var end = atoms[^1].Index;

        if (end <= start)
        {
            return false;
        }

        var between = utterance[start..end];

        return language == UtteranceLanguage.Spanish
            ? SpanishRangeConnectorPattern().IsMatch(between)
            : RangeConnectorPattern().IsMatch(between);
    }

    // ── English vocabulary ───────────────────────────────────────────────────────

    private static readonly string[] EnglishWeekdayNames =
    [
        "sunday", "monday", "tuesday", "wednesday", "thursday", "friday", "saturday",
    ];

    private static readonly string[] EnglishMonthNames =
    [
        "january", "february", "march", "april", "may", "june",
        "july", "august", "september", "october", "november", "december",
    ];

    private static bool ContainsEnglishMonthName(string utterance) =>
        EnglishMonthNames.Any(month => utterance.Contains(month, StringComparison.OrdinalIgnoreCase));

    private static DayOfWeek EnglishWeekday(string name) =>
        (DayOfWeek)Array.FindIndex(
            EnglishWeekdayNames,
            candidate => string.Equals(candidate, name, StringComparison.OrdinalIgnoreCase));

    private static int? EnglishMonthNumber(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        var index = Array.FindIndex(
            EnglishMonthNames,
            candidate => string.Equals(candidate, name, StringComparison.OrdinalIgnoreCase));

        return index < 0 ? null : index + 1;
    }

    // ── Spanish vocabulary ───────────────────────────────────────────────────────

    private static readonly string[] SpanishWeekdayNames =
    [
        "domingo", "lunes", "martes", "miercoles", "jueves", "viernes", "sabado",
    ];

    private static readonly string[] SpanishMonthNames =
    [
        "enero", "febrero", "marzo", "abril", "mayo", "junio",
        "julio", "agosto", "septiembre", "octubre", "noviembre", "diciembre",
    ];

    private static DayOfWeek SpanishWeekday(string name)
    {
        var plain = WithoutAccents(name);

        return (DayOfWeek)Array.FindIndex(
            SpanishWeekdayNames,
            candidate => string.Equals(candidate, plain, StringComparison.OrdinalIgnoreCase));
    }

    private static int? SpanishMonthNumber(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        var plain = WithoutAccents(name);

        if (string.Equals(plain, "setiembre", StringComparison.OrdinalIgnoreCase))
        {
            return 9;
        }

        var index = Array.FindIndex(
            SpanishMonthNames,
            candidate => string.Equals(candidate, plain, StringComparison.OrdinalIgnoreCase));

        return index < 0 ? null : index + 1;
    }

    /// <summary>
    /// The accented vowels the vocabulary above can carry, folded so "miércoles"
    /// and "miercoles" are one word. A five-character map rather than a Unicode
    /// normalisation pass, because these are the only five that occur.
    /// </summary>
    private static string WithoutAccents(string word) =>
        word.ToLowerInvariant()
            .Replace('á', 'a')
            .Replace('é', 'e')
            .Replace('í', 'i')
            .Replace('ó', 'o')
            .Replace('ú', 'u');

    // ── English patterns ─────────────────────────────────────────────────────────

    private const string Weekdays = "sunday|monday|tuesday|wednesday|thursday|friday|saturday";
    private const string Months =
        "january|february|march|april|may|june|july|august|september|october|november|december";

    [GeneratedRegex($@"\b(?<day>{Weekdays})\s+(?:of\s+)?next\s+week\b", RegexOptions.IgnoreCase)]
    private static partial Regex WeekdayNextWeekPattern();

    [GeneratedRegex($@"\bnext\s+(?<day>{Weekdays})\b", RegexOptions.IgnoreCase)]
    private static partial Regex NextWeekdayPattern();

    [GeneratedRegex(
        $@"\b(?<weekday>{Weekdays})\s+(?:the\s+)?(?<day>\d{{1,2}})(?:st|nd|rd|th)\b(?:\s+(?:of\s+)?(?<month>{Months}))?",
        RegexOptions.IgnoreCase)]
    private static partial Regex WeekdayWithOrdinalPattern();

    [GeneratedRegex(
        $@"\b(?:the\s+)?(?<day>\d{{1,2}})(?<suffix>st|nd|rd|th)?\b(?:\s+(?:of\s+)?(?<month>{Months}))?",
        RegexOptions.IgnoreCase)]
    private static partial Regex DayOfMonthPattern();

    [GeneratedRegex(@"\b\d{1,2}(st|nd|rd|th)\b", RegexOptions.IgnoreCase)]
    private static partial Regex OrdinalPattern();

    [GeneratedRegex($@"\b(?<day>{Weekdays})\b", RegexOptions.IgnoreCase)]
    private static partial Regex WeekdayPattern();

    [GeneratedRegex(@"\btoday\b", RegexOptions.IgnoreCase)]
    private static partial Regex TodayPattern();

    [GeneratedRegex(@"\btomorrow\b", RegexOptions.IgnoreCase)]
    private static partial Regex TomorrowPattern();

    [GeneratedRegex(@"(\bto\b|\bthrough\b|\buntil\b|\btill\b|–|—|-->|\s-\s)", RegexOptions.IgnoreCase)]
    private static partial Regex RangeConnectorPattern();

    // ── Spanish patterns ─────────────────────────────────────────────────────────
    //
    // Accented and unaccented forms are both accepted throughout — a visitor
    // without a Spanish keyboard types "miercoles" and means Wednesday.

    private const string SpanishWeekdays =
        "domingo|lunes|martes|mi[eé]rcoles|jueves|viernes|s[aá]bado";

    private const string SpanishMonths =
        "enero|febrero|marzo|abril|mayo|junio|julio|agosto|septiembre|setiembre|octubre|noviembre|diciembre";

    // "el viernes de la semana que viene", "viernes de la próxima semana"
    [GeneratedRegex(
        $@"\b(?<day>{SpanishWeekdays})\s+de\s+la\s+(?:semana\s+que\s+viene|pr[oó]xima\s+semana)\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex SpanishWeekdayNextWeekPattern();

    // "el viernes que viene". A different expression from the bare weekday for the
    // same reason "next Friday" is in English: said on a Friday it has two readings
    // a competent Spanish speaker will defend, and the resolver refuses to pick.
    [GeneratedRegex($@"\b(?<day>{SpanishWeekdays})\s+que\s+viene\b", RegexOptions.IgnoreCase)]
    private static partial Regex SpanishNextWeekdayPattern();

    // "el próximo viernes" — the other surface form of the same expression.
    [GeneratedRegex($@"\bpr[oó]ximo\s+(?<day>{SpanishWeekdays})\b", RegexOptions.IgnoreCase)]
    private static partial Regex SpanishProximoWeekdayPattern();

    // "el viernes 13", "viernes 13 de octubre" — weekday and day both stated, kept
    // so the resolver can catch a user who has miscounted.
    [GeneratedRegex(
        $@"\b(?<weekday>{SpanishWeekdays})\s+(?:d[ií]a\s+)?(?<day>\d{{1,2}})\b(?:\s+de\s+(?<month>{SpanishMonths}))?",
        RegexOptions.IgnoreCase)]
    private static partial Regex SpanishWeekdayWithDayPattern();

    // "el 26", "el día 26", "del 3", "al 7", "26 de octubre". The article is a
    // lookbehind: the atom starts at the digit, so "del 3 al 7" leaves " al "
    // between its two atoms for the range check to read. A number with neither an
    // article before it nor a month after it is not a date.
    [GeneratedRegex(
        $@"(?:(?<=\b(?:el\s+d[ií]a|del|al|el)\s+)(?<day>\d{{1,2}})\b(?:\s+de\s+(?<month>{SpanishMonths}))?"
        + $@"|\b(?<day>\d{{1,2}})\s+de\s+(?<month>{SpanishMonths})\b)",
        RegexOptions.IgnoreCase)]
    private static partial Regex SpanishDayOfMonthPattern();

    [GeneratedRegex($@"\b(?<day>{SpanishWeekdays})\b", RegexOptions.IgnoreCase)]
    private static partial Regex SpanishWeekdayPattern();

    [GeneratedRegex(@"\bhoy\b", RegexOptions.IgnoreCase)]
    private static partial Regex SpanishTodayPattern();

    [GeneratedRegex(@"\bma[ñn]ana\b", RegexOptions.IgnoreCase)]
    private static partial Regex SpanishTomorrowPattern();

    // "del 3 al 7", "de lunes a viernes", "hasta el jueves". "a" and "al" only
    // count between two atoms, which is the only place this pattern is applied.
    [GeneratedRegex(@"(\bal\b|\ba\b|\bhasta\b|–|—|-->|\s-\s)", RegexOptions.IgnoreCase)]
    private static partial Regex SpanishRangeConnectorPattern();
}
