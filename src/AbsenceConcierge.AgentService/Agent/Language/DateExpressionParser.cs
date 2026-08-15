using System.Globalization;
using System.Text.RegularExpressions;
using AbsenceConcierge.AgentService.Agent.Time;

namespace AbsenceConcierge.AgentService.Agent.Language;

/// <summary>
/// Reads date expressions out of an English sentence.
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
/// <c>evals/scenarios/</c> are a sample of English, not the specification of it,
/// and a parser fitted to thirty-two strings would pass Layer 1 while being useless
/// on the thirty-third. The shapes below — bare weekdays, "next X", "X next week",
/// ordinals with and without a month, ranges and lists — are the grammar; the unit
/// tests deliberately include sentences that appear in no scenario.
/// </para>
/// </summary>
public static partial class DateExpressionParser
{
    private static readonly string[] WeekdayNames =
    [
        "sunday", "monday", "tuesday", "wednesday", "thursday", "friday", "saturday",
    ];

    private static readonly string[] MonthNames =
    [
        "january", "february", "march", "april", "may", "june",
        "july", "august", "september", "october", "november", "december",
    ];

    /// <summary>An atom found in the sentence, with where it was found.</summary>
    private sealed record Atom(int Index, int Length, DateExpression Expression, int? Month);

    public static DateExpression? Parse(string utterance)
    {
        if (string.IsNullOrWhiteSpace(utterance))
        {
            return null;
        }

        var atoms = FindAtoms(utterance);

        if (atoms.Count == 0)
        {
            return null;
        }

        // "19 to 21 August" names the month once, at the end. Propagating it
        // backwards is what stops the 19th resolving into a different month from
        // the 21st — an off-by-a-month that produces a confident, well-formed
        // request for the wrong week.
        atoms = PropagateMonth(atoms);

        if (atoms.Count == 1)
        {
            return atoms[0].Expression;
        }

        return IsRange(utterance, atoms)
            ? new DateSpanExpression(atoms[0].Expression, atoms[^1].Expression)
            : new DateListExpression([.. atoms.Select(a => a.Expression)]);
    }

    private static List<Atom> FindAtoms(string utterance)
    {
        var atoms = new List<Atom>();
        var claimed = new List<(int Start, int End)>();

        void Claim(Match match, DateExpression expression, int? month = null)
        {
            foreach (var (start, end) in claimed)
            {
                if (match.Index < end && start < match.Index + match.Length)
                {
                    return;
                }
            }

            claimed.Add((match.Index, match.Index + match.Length));
            atoms.Add(new Atom(match.Index, match.Length, expression, month));
        }

        // Order matters: the most specific shape claims its text first, so
        // "Thursday the 13th" does not also register a bare "Thursday", and
        // "next Friday" is never read as the coming Friday.
        foreach (Match match in WeekdayNextWeekPattern().Matches(utterance))
        {
            Claim(match, new WeekdayNextWeekExpression(Weekday(match.Groups["day"].Value)));
        }

        foreach (Match match in NextWeekdayPattern().Matches(utterance))
        {
            Claim(match, new NextWeekdayExpression(Weekday(match.Groups["day"].Value)));
        }

        foreach (Match match in WeekdayWithOrdinalPattern().Matches(utterance))
        {
            var month = MonthNumber(match.Groups["month"].Value);
            Claim(
                match,
                new CalendarDayExpression(
                    int.Parse(match.Groups["day"].Value, CultureInfo.InvariantCulture),
                    month,
                    Weekday(match.Groups["weekday"].Value)),
                month);
        }

        // A bare number is only a day of the month when the sentence gives some
        // other reason to read it as one — an ordinal suffix somewhere, or a named
        // month. "I need 2 days off" must not become the 2nd.
        var bareNumbersAllowed = OrdinalPattern().IsMatch(utterance) || ContainsMonthName(utterance);

        foreach (Match match in DayOfMonthPattern().Matches(utterance))
        {
            if (match.Groups["suffix"].Value.Length == 0 && !bareNumbersAllowed)
            {
                continue;
            }

            var month = MonthNumber(match.Groups["month"].Value);
            Claim(
                match,
                new CalendarDayExpression(
                    int.Parse(match.Groups["day"].Value, CultureInfo.InvariantCulture),
                    month,
                    null),
                month);
        }

        foreach (Match match in WeekdayPattern().Matches(utterance))
        {
            Claim(match, new ComingWeekdayExpression(Weekday(match.Groups["day"].Value)));
        }

        foreach (Match match in TodayPattern().Matches(utterance))
        {
            Claim(match, new TodayExpression());
        }

        foreach (Match match in TomorrowPattern().Matches(utterance))
        {
            Claim(match, new TomorrowExpression());
        }

        atoms.Sort((left, right) => left.Index.CompareTo(right.Index));
        return atoms;
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
    /// than a list. "the 20th to the 24th" is a span; "the 26th, 27th and 28th" is
    /// a list that happens to be contiguous, and the difference matters because the
    /// resolver refuses to close a gap in a list.
    /// </summary>
    private static bool IsRange(string utterance, List<Atom> atoms)
    {
        var start = atoms[0].Index + atoms[0].Length;
        var end = atoms[^1].Index;

        if (end <= start)
        {
            return false;
        }

        var between = utterance[start..end];
        return RangeConnectorPattern().IsMatch(between);
    }

    private static bool ContainsMonthName(string utterance) =>
        MonthNames.Any(month => utterance.Contains(month, StringComparison.OrdinalIgnoreCase));

    private static DayOfWeek Weekday(string name) =>
        (DayOfWeek)Array.FindIndex(
            WeekdayNames,
            candidate => string.Equals(candidate, name, StringComparison.OrdinalIgnoreCase));

    private static int? MonthNumber(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        var index = Array.FindIndex(
            MonthNames,
            candidate => string.Equals(candidate, name, StringComparison.OrdinalIgnoreCase));

        return index < 0 ? null : index + 1;
    }

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
}
