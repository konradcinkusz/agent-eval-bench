namespace AbsenceConcierge.AgentService.Agent.Language;

/// <summary>
/// The languages the rule-based interpreter reads. A closed set, like
/// <see cref="Time.DateExpression"/> and for the same reason: an open string would
/// put "which language is this?" in two places and let them disagree.
/// </summary>
public enum UtteranceLanguage
{
    English,
    Spanish,
}

public static class UtteranceLanguages
{
    /// <summary>
    /// The language a locale tag selects. The tag is the fixture's and the
    /// deployment's vocabulary (<c>en-GB</c>, <c>es-ES</c>); this is the seam where
    /// it starts driving behaviour rather than being carried and ignored (SPEC §9).
    /// Unknown tags read as English — the documented default, not a guess: the
    /// interpreter still tries the other language when the primary one finds
    /// nothing, so a mislabelled fixture degrades to a fallback rather than a wall.
    /// </summary>
    public static UtteranceLanguage FromLocale(string? locale) =>
        locale is not null && locale.StartsWith("es", StringComparison.OrdinalIgnoreCase)
            ? UtteranceLanguage.Spanish
            : UtteranceLanguage.English;
}
