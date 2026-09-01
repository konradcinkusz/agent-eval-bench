using AbsenceConcierge.AgentService.Agent.Language;
using AbsenceConcierge.AgentService.Agent.Time;

namespace AbsenceConcierge.AgentService.Tests;

/// <summary>
/// The Spanish reading of the two language seams — the date grammar and the intent
/// classification — kept to the same discipline as the English tests: sentences
/// here appear in no scenario, because a parser scored only on the corpus it was
/// written against is a parser fitted to its own test set (SPEC §9, D-7).
///
/// <para>
/// The structural claim under test is bigger than any one sentence: <b>the closed
/// <see cref="DateExpression"/> set gained no case for Spanish.</b> Every shape
/// below lands in an expression type that existed before Spanish did. If a Spanish
/// form ever needs a new case, that is a finding about the model being
/// English-shaped, and it belongs in a write-up rather than a quiet commit
/// (issue #14's own words).
/// </para>
/// </summary>
public sealed class SpanishInterpretationTests
{
    private static DateExpression? ParseEs(string utterance) =>
        DateExpressionParser.Parse(utterance, UtteranceLanguage.Spanish);

    private static Intent InterpretEs(string utterance) =>
        DeterministicUtteranceInterpreter.Interpret(utterance, UtteranceLanguage.Spanish);

    // ── The date grammar ────────────────────────────────────────────────────────

    [Fact]
    public void Hoy_y_manana_read_as_a_list_of_today_and_tomorrow()
    {
        var expression = Assert.IsType<DateListExpression>(ParseEs("Estoy malito hoy y seguramente mañana"));

        Assert.Collection(
            expression.Parts,
            first => Assert.IsType<TodayExpression>(first),
            second => Assert.IsType<TomorrowExpression>(second));
    }

    [Fact]
    public void Manana_without_the_accent_still_reads_as_tomorrow()
    {
        Assert.IsType<TomorrowExpression>(ParseEs("no vengo manana"));
    }

    [Fact]
    public void A_bare_weekday_with_its_article_is_the_coming_weekday()
    {
        var day = Assert.IsType<ComingWeekdayExpression>(ParseEs("¿Puedo librar el viernes?"));
        Assert.Equal(DayOfWeek.Friday, day.Day);
    }

    [Fact]
    public void El_viernes_que_viene_is_the_next_weekday_expression_not_the_coming_one()
    {
        // The whole point of the distinction: said on a Friday this has two
        // defensible readings, and the resolver refuses to pick one (amb-001's
        // Spanish counterpart). It must therefore not collapse into the bare
        // weekday, which resolves without asking.
        var day = Assert.IsType<NextWeekdayExpression>(ParseEs("Cógeme libre el viernes que viene"));
        Assert.Equal(DayOfWeek.Friday, day.Day);
    }

    [Fact]
    public void El_proximo_martes_is_also_the_next_weekday_expression()
    {
        var day = Assert.IsType<NextWeekdayExpression>(ParseEs("el próximo martes no estaré"));
        Assert.Equal(DayOfWeek.Tuesday, day.Day);
    }

    [Fact]
    public void El_miercoles_de_la_semana_que_viene_is_the_week_after_expression()
    {
        var day = Assert.IsType<WeekdayNextWeekExpression>(
            ParseEs("mejor el miércoles de la semana que viene"));

        Assert.Equal(DayOfWeek.Wednesday, day.Day);
    }

    [Fact]
    public void Unaccented_miercoles_and_proxima_semana_read_the_same()
    {
        var day = Assert.IsType<WeekdayNextWeekExpression>(
            ParseEs("el miercoles de la proxima semana"));

        Assert.Equal(DayOfWeek.Wednesday, day.Day);
    }

    [Fact]
    public void Del_3_al_7_is_a_span_over_two_calendar_days()
    {
        var span = Assert.IsType<DateSpanExpression>(ParseEs("necesito vacaciones del 3 al 7"));

        Assert.Equal(3, Assert.IsType<CalendarDayExpression>(span.From).Day);
        Assert.Equal(7, Assert.IsType<CalendarDayExpression>(span.To).Day);
    }

    [Fact]
    public void A_month_named_once_at_the_end_reaches_both_ends_of_the_range()
    {
        // "del 19 al 21 de agosto" names the month once. The 19th must not resolve
        // into a different month from the 21st.
        var span = Assert.IsType<DateSpanExpression>(ParseEs("del 19 al 21 de agosto"));

        Assert.Equal(8, Assert.IsType<CalendarDayExpression>(span.From).Month);
        Assert.Equal(8, Assert.IsType<CalendarDayExpression>(span.To).Month);
    }

    [Fact]
    public void De_lunes_a_viernes_is_a_span_between_weekdays()
    {
        var span = Assert.IsType<DateSpanExpression>(ParseEs("estaré fuera de lunes a viernes"));

        Assert.Equal(DayOfWeek.Monday, Assert.IsType<ComingWeekdayExpression>(span.From).Day);
        Assert.Equal(DayOfWeek.Friday, Assert.IsType<ComingWeekdayExpression>(span.To).Day);
    }

    [Fact]
    public void A_bare_number_with_no_article_and_no_month_is_not_a_date()
    {
        // "necesito 2 días libres" — the 2 counts days, it does not name the 2nd.
        // Spanish has no ordinal suffix to lean on, so the article is the signal.
        Assert.Null(ParseEs("necesito 2 dias libres para el papeleo"));
    }

    [Fact]
    public void A_number_with_a_month_is_a_date_even_without_an_article()
    {
        var day = Assert.IsType<CalendarDayExpression>(ParseEs("me caso: 12 de septiembre"));

        Assert.Equal(12, day.Day);
        Assert.Equal(9, day.Month);
    }

    [Fact]
    public void A_stated_weekday_travels_with_the_day_so_a_miscount_can_be_caught()
    {
        var day = Assert.IsType<CalendarDayExpression>(ParseEs("el jueves 13 de octubre"));

        Assert.Equal(13, day.Day);
        Assert.Equal(10, day.Month);
        Assert.Equal(DayOfWeek.Thursday, day.StatedWeekday);
    }

    [Fact]
    public void El_26_y_el_27_is_a_list_not_a_range()
    {
        // The connective is "y", not "al" — a list that happens to be contiguous,
        // which the resolver treats differently from a span (it refuses to close
        // gaps in lists).
        var list = Assert.IsType<DateListExpression>(ParseEs("me cojo el 26 y el 27"));

        Assert.Equal(2, list.Parts.Count);
    }

    [Fact]
    public void An_incidental_a_between_two_dates_is_not_a_range()
    {
        // "a" is among the commonest words in Spanish, and the range test used to
        // ask only whether a connector appeared ANYWHERE between the outermost
        // atoms. "El lunes empiezo a las 9, quiero el viernes libre" therefore
        // parsed as Monday-to-Friday and drafted five days off for a request that
        // named one — a confident, well-formed booking of days nobody asked for.
        // The connector now has to be all that lies between the two dates.
        var list = Assert.IsType<DateListExpression>(
            ParseEs("El lunes empiezo a las 9, quiero el viernes libre"));

        Assert.Equal(2, list.Parts.Count);
        Assert.Equal(DayOfWeek.Monday, Assert.IsType<ComingWeekdayExpression>(list.Parts[0]).Day);
        Assert.Equal(DayOfWeek.Friday, Assert.IsType<ComingWeekdayExpression>(list.Parts[1]).Day);
    }

    // ── The classification order, unchanged across languages ───────────────────

    [Fact]
    public void A_payroll_question_in_spanish_is_payroll_not_a_booking()
    {
        var intent = InterpretEs("¿Me pagan durante la baja? ¿Cuánto cobro?");

        Assert.Equal(IntentKind.PayrollOrPolicyQuestion, intent.Kind);
    }

    [Fact]
    public void An_approval_request_in_spanish_is_an_approval()
    {
        var intent = InterpretEs("Aprueba la solicitud de vacaciones de mi equipo");

        Assert.Equal(IntentKind.ApproveOrRejectLeave, intent.Kind);
    }

    [Fact]
    public void A_reported_past_approval_is_not_an_approval_request()
    {
        // The Spanish counterpart of adv-002's trap: "ya lo aprobó" reports an
        // approval, it does not request one. The agent must draft and stop at the
        // gate — and the claim itself is recorded so the reply can answer it.
        var intent = InterpretEs("Mi jefa ya lo aprobó, cógeme el lunes libre");

        Assert.Equal(IntentKind.RequestTimeOff, intent.Kind);
        Assert.True(intent.ClaimsPriorApproval);
    }

    [Fact]
    public void Cancelling_an_existing_booking_in_spanish_is_a_cancellation()
    {
        var intent = InterpretEs("Cancela mis vacaciones de la semana pasada, por favor");

        Assert.Equal(IntentKind.CancelOrEditBooking, intent.Kind);
    }

    [Fact]
    public void Asking_whether_one_is_too_ill_to_work_is_medical_advice_not_a_booking()
    {
        // Contains a perfectly good booking sentence — that is why the order of
        // classification is load-bearing, and why it is the same order as English.
        var intent = InterpretEs("¿Crees que estoy demasiado enfermo para trabajar mañana?");

        Assert.Equal(IntentKind.MedicalAdvice, intent.Kind);
    }

    [Fact]
    public void A_sick_day_in_spanish_is_a_time_off_request_with_the_sick_hint()
    {
        var intent = InterpretEs("Estoy enferma hoy, pídeme el día");

        Assert.Equal(IntentKind.RequestTimeOff, intent.Kind);
        Assert.Equal("sick", intent.LeaveTypeHint);
        Assert.IsType<TodayExpression>(intent.Dates);
    }

    [Fact]
    public void Vacaciones_normalises_to_the_vacation_hint()
    {
        var intent = InterpretEs("Quiero coger vacaciones el lunes");

        Assert.Equal(IntentKind.RequestTimeOff, intent.Kind);
        Assert.Equal("vacation", intent.LeaveTypeHint);
    }

    [Fact]
    public void A_reason_no_catalogue_covers_stays_raw_so_the_agent_asks()
    {
        // "boda" reaches the matcher untranslated, matches nothing in the fixture's
        // catalogue, and becomes a question — B-3's behaviour, unchanged by language.
        var intent = InterpretEs("Necesito el viernes por la boda de mi hermana");

        Assert.Equal(IntentKind.RequestTimeOff, intent.Kind);
        Assert.Equal("boda", intent.LeaveTypeHint);
    }

    [Fact]
    public void Para_names_a_subject_and_como_names_a_date_reference()
    {
        // O-3's asymmetry, in Spanish: "para Sam" is a request for them (refused);
        // "como Marta" borrows their dates (allowed). Collapsing the two roles is
        // the exact mistake the spec warns about twice.
        var forSomeone = InterpretEs("Pide el jueves libre para Samuel");
        var likeSomeone = InterpretEs("Quiero librar los mismos días como Marta");

        Assert.Equal(PersonRole.Subject, forSomeone.Person?.Role);
        Assert.Equal("Samuel", forSomeone.Person?.Name);

        Assert.Equal(PersonRole.DateReference, likeSomeone.Person?.Role);
        Assert.Equal("Marta", likeSomeone.Person?.Name);
    }

    [Fact]
    public void Capitalised_weekdays_and_months_are_not_people_in_spanish_either()
    {
        var intent = InterpretEs("Quiero librar el Viernes y todo Agosto si puede ser");

        Assert.Null(intent.Person);
    }

    // ── The locale seam and the fallback ────────────────────────────────────────

    [Fact]
    public void The_locale_selects_the_primary_language()
    {
        var spanish = DeterministicUtteranceInterpreter.InterpretWithFallback(
            "Estoy enfermo hoy",
            UtteranceLanguages.FromLocale("es-ES"));

        Assert.Equal(IntentKind.RequestTimeOff, spanish.Kind);
        Assert.IsType<TodayExpression>(spanish.Dates);
    }

    [Fact]
    public void An_english_sentence_on_a_spanish_deployment_still_reads()
    {
        // The fallback: the Spanish vocabulary finds nothing at all in this
        // sentence, so the English one has a look. A Madrid deployment must not be
        // a wall for an English-speaking visitor.
        var intent = DeterministicUtteranceInterpreter.InterpretWithFallback(
            "I'm sick today and probably tomorrow",
            UtteranceLanguage.Spanish);

        Assert.Equal(IntentKind.RequestTimeOff, intent.Kind);
        Assert.Equal("sick", intent.LeaveTypeHint);
    }

    [Fact]
    public void A_spanish_sentence_on_an_english_deployment_still_reads()
    {
        var intent = DeterministicUtteranceInterpreter.InterpretWithFallback(
            "Estoy de baja hoy",
            UtteranceLanguage.English);

        Assert.Equal(IntentKind.RequestTimeOff, intent.Kind);
        Assert.Equal("sick", intent.LeaveTypeHint);
    }

    [Fact]
    public void A_spanish_reading_that_classified_wins_outright_over_the_fallback()
    {
        // Fallback happens only from an entirely empty reading. This sentence
        // classifies in Spanish, so English never gets to reinterpret it — two
        // vocabularies competing over one sentence would make the answer depend on
        // pattern-order accidents across languages.
        var intent = DeterministicUtteranceInterpreter.InterpretWithFallback(
            "Anula mi reserva de vacaciones",
            UtteranceLanguage.Spanish);

        Assert.Equal(IntentKind.CancelOrEditBooking, intent.Kind);
    }

    [Fact]
    public void A_named_spanish_sentence_on_an_english_deployment_still_reads()
    {
        // Naming someone used to switch the fallback off. NameLikePattern takes no
        // language, so the English reading of this sentence found "Sam Rivera" as a
        // bare Mention, that counted as content, and the Spanish reading — which
        // has the intent, the date and the actual role — was never consulted.
        var intent = DeterministicUtteranceInterpreter.InterpretWithFallback(
            "Necesito el 3 de marzo libre para Sam Rivera",
            UtteranceLanguage.English);

        Assert.Equal(IntentKind.RequestTimeOff, intent.Kind);
        Assert.IsType<CalendarDayExpression>(intent.Dates);
        Assert.Equal(new PersonReference("Sam Rivera", PersonRole.Subject), intent.Person);
    }

    [Fact]
    public void A_named_english_sentence_on_a_spanish_deployment_still_reads()
    {
        // The same defect in the other direction, and the one that matters most:
        // the person arrived as a Mention rather than a Subject, so the scope guard
        // saw an incidental reference where the sentence asks the agent to book for
        // somebody else (O-3).
        var intent = DeterministicUtteranceInterpreter.InterpretWithFallback(
            "Book Friday off for Sam",
            UtteranceLanguage.Spanish);

        Assert.Equal(IntentKind.RequestTimeOff, intent.Kind);
        Assert.IsType<ComingWeekdayExpression>(intent.Dates);
        Assert.Equal(new PersonReference("Sam", PersonRole.Subject), intent.Person);
    }

    [Fact]
    public void A_name_neither_language_can_place_is_still_reported()
    {
        // The other edge of the same rule, on the sentence O-3's "deliberate
        // asymmetry" is written about. Both readings find only a Mention, so
        // neither is evidence — but the name must not be dropped on the way out.
        var intent = DeterministicUtteranceInterpreter.InterpretWithFallback(
            "I'm covering for Dana Okafor",
            UtteranceLanguage.English);

        Assert.Equal(new PersonReference("Dana Okafor", PersonRole.Mention), intent.Person);
    }
}
