using System.Globalization;
using System.Text.Json;

namespace AbsenceConcierge.AgentService.Workforce.Mcp;

/// <summary>
/// A payload arrived that this adapter could not read. Thrown, not returned, because
/// every caller does the same thing with it — and caught in exactly one place.
/// </summary>
public sealed class McpPayloadException : Exception
{
    public McpPayloadException()
    {
    }

    public McpPayloadException(string message)
        : base(message)
    {
    }

    public McpPayloadException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Foreign JSON in, <see cref="WorkforceUser"/> and its neighbours out. This is the anti-corruption
/// layer's actual work (P11) — the interface above it is just where the seam is drawn.
///
/// <para>
/// <b>Tolerant about shape, strict about content.</b> Key lookup ignores case and
/// separators, so <c>employee_id</c>, <c>employeeId</c> and <c>EmployeeID</c> are the
/// same key; a list may arrive bare or wrapped in <c>data</c>/<c>items</c>/<c>results</c>;
/// an id may be a JSON number. None of that changes what the agent then reasons about,
/// so absorbing it here is cheaper than a configuration knob per server. What is
/// <em>not</em> tolerated is a missing required field: a leave with no employee id, or
/// a booking with no dates, is a payload this adapter cannot safely act on, and
/// guessing would produce a plausible request for the wrong dates.
/// </para>
/// <para>
/// <b>Every failure names the keys.</b> The message says which names were looked for
/// and which the object actually had. This repository has never run against a live
/// server, and the first person who does will be reading a log line rather than
/// attaching a debugger — so that log line is written for them.
/// </para>
/// </summary>
public static class McpPayloads
{
    public static WorkforceUser User(string? text)
    {
        var root = Root(text, "get_current_user");

        return new WorkforceUser(
            RequiredString(root, "get_current_user.employee_id", "employee_id", "id", "employee"),
            RequiredString(root, "get_current_user.display_name", "display_name", "name", "full_name"),
            OptionalString(root, "team", "department", "team_name") ?? string.Empty,
            Strings(root, "permissions", "scopes", "grants"));
    }

    public static IReadOnlyList<Employee> Employees(string? text) =>
        Items(Root(text, "find_employee"), "find_employee", "employees")
            .Select(item => new Employee(
                RequiredString(item, "find_employee[].employee_id", "employee_id", "id", "employee"),
                RequiredString(item, "find_employee[].display_name", "display_name", "name", "full_name"),
                OptionalString(item, "team", "department", "team_name") ?? string.Empty))
            .ToList();

    public static IReadOnlyList<LeaveType> LeaveTypes(string? text) =>
        Items(Root(text, "list_leave_types"), "list_leave_types", "leave_types")
            .Select(item => new LeaveType(
                RequiredString(item, "list_leave_types[].id", "id", "leave_type_id", "code"),
                RequiredString(item, "list_leave_types[].name", "name", "display_name", "label"),

                // Defaults chosen so that an absent field never makes the agent more
                // permissive than the server: unknown approval requirement is treated
                // as "needs approval", unknown balance impact as "counts".
                OptionalBool(item, "requires_approval", "approval_required") ?? true,
                OptionalBool(item, "counts_against_balance", "affects_balance") ?? true,

                // Zero means "not limited" throughout this codebase, matching the
                // fixture, so an absent limit reads as no limit rather than as a
                // one-day maximum.
                OptionalInt(item, "max_consecutive_days", "maximum_consecutive_days") ?? 0,
                OptionalBool(item, "allows_half_days", "half_days_allowed") ?? false,
                OptionalInt(item, "requires_attachment_after_days", "attachment_required_after_days")))
            .ToList();

    public static IReadOnlyList<Leave> Leaves(string? text) =>
        Items(Root(text, "list_leaves"), "list_leaves", "leaves")
            .Select(item => new Leave(
                RequiredString(item, "list_leaves[].id", "id", "leave_id"),

                // Required, and deliberately so. only_for_self is enforced by comparing
                // this to the actor, and a payload that omits it is one where that
                // check would silently pass for someone else's booking.
                RequiredString(item, "list_leaves[].employee_id", "employee_id", "employee"),
                RequiredString(item, "list_leaves[].leave_type_id", "leave_type_id", "leave_type", "type_id"),
                RequiredDate(item, "list_leaves[].start_date", "start_date", "start_on", "from", "start"),
                RequiredDate(item, "list_leaves[].end_date", "end_date", "end_on", "to", "finish", "end"),
                OptionalString(item, "status", "state") ?? "unknown",
                OptionalString(item, "comment", "description", "note")))
            .ToList();

    public static TimeOffResult TimeOff(string? text)
    {
        var root = Root(text, "request_time_off");

        // A server that wraps its answer gets unwrapped once. Beyond that the payload
        // is what it is; this adapter does not go hunting.
        if (Find(root, "leave", "request", "data") is { ValueKind: JsonValueKind.Object } wrapped)
        {
            root = wrapped;
        }

        return new TimeOffResult(
            RequiredString(root, "request_time_off.request_id", "request_id", "id", "leave_id"),
            OptionalString(root, "status", "state") ?? "unknown",
            RequiredDate(root, "request_time_off.start_date", "start_date", "start_on", "from", "start"),
            RequiredDate(root, "request_time_off.end_date", "end_date", "end_on", "to", "finish", "end"));
    }

    // ── Reading ─────────────────────────────────────────────────────────────────

    private static JsonElement Root(string? text, string what)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new McpPayloadException($"{what}: the reply carried no text content.");
        }

        try
        {
            using var document = JsonDocument.Parse(text);

            // Cloned because the document is disposed on the way out of this method and
            // the element would otherwise be a window onto freed memory.
            return document.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            throw new McpPayloadException($"{what}: the reply's text content is not JSON.", ex);
        }
    }

    private static IReadOnlyList<JsonElement> Items(JsonElement root, string what, string collectionKey)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            return [.. root.EnumerateArray()];
        }

        if (Find(root, collectionKey, "data", "items", "results") is { ValueKind: JsonValueKind.Array } nested)
        {
            return [.. nested.EnumerateArray()];
        }

        throw new McpPayloadException(
            $"{what}: expected an array, or an object holding one under "
            + $"[{collectionKey}, data, items, results]; got {Describe(root)}.");
    }

    /// <summary>
    /// Looks a key up by any of its candidate names, exactly first and then ignoring
    /// case, underscores and hyphens.
    /// </summary>
    private static JsonElement? Find(JsonElement element, params string[] names)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var exact))
            {
                return exact;
            }
        }

        var wanted = names.Select(Normalise).ToArray();

        foreach (var property in element.EnumerateObject())
        {
            if (wanted.Contains(Normalise(property.Name), StringComparer.Ordinal))
            {
                return property.Value;
            }
        }

        return null;
    }

    private static string Normalise(string name) =>
        name.Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();

    private static string RequiredString(JsonElement element, string what, params string[] names)
    {
        if (OptionalString(element, names) is { } value)
        {
            return value;
        }

        throw new McpPayloadException(
            $"{what}: looked for [{string.Join(", ", names)}], found {Describe(element)}.");
    }

    private static string? OptionalString(JsonElement element, params string[] names) =>
        Find(element, names) switch
        {
            { ValueKind: JsonValueKind.String } text when !string.IsNullOrWhiteSpace(text.GetString()) =>
                text.GetString(),

            // Identifiers arrive as numbers often enough that refusing them would make
            // this an adapter that fails on half the servers it meets, for no reason.
            { ValueKind: JsonValueKind.Number } number => number.GetRawText(),

            _ => null,
        };

    private static DateOnly RequiredDate(JsonElement element, string what, params string[] names)
    {
        var raw = RequiredString(element, what, names);

        if (DateOnly.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return date;
        }

        // A timestamp is a date plus noise here. Reading it in the offset the server
        // sent is the only reading that does not silently shift a booking by a day.
        if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var stamp))
        {
            return DateOnly.FromDateTime(stamp.Date);
        }

        throw new McpPayloadException($"{what}: '{raw}' is not a date this adapter can read (expected yyyy-MM-dd).");
    }

    private static bool? OptionalBool(JsonElement element, params string[] names) =>
        Find(element, names) switch
        {
            { ValueKind: JsonValueKind.True } => true,
            { ValueKind: JsonValueKind.False } => false,
            { ValueKind: JsonValueKind.String } text when bool.TryParse(text.GetString(), out var parsed) => parsed,
            _ => null,
        };

    private static int? OptionalInt(JsonElement element, params string[] names) =>
        Find(element, names) switch
        {
            { ValueKind: JsonValueKind.Number } number when number.TryGetInt32(out var parsed) => parsed,
            { ValueKind: JsonValueKind.String } text
                when int.TryParse(text.GetString(), CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null,
        };

    private static IReadOnlyList<string> Strings(JsonElement element, params string[] names)
    {
        if (Find(element, names) is not { ValueKind: JsonValueKind.Array } array)
        {
            return [];
        }

        return [.. array.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString()!)
            .Where(value => !string.IsNullOrWhiteSpace(value))];
    }

    /// <summary>
    /// What the payload actually was, for the failure message. Key names only — a
    /// server's values may be somebody's name, and this string reaches a log.
    /// </summary>
    private static string Describe(JsonElement element) =>
        element.ValueKind == JsonValueKind.Object
            ? $"an object with [{string.Join(", ", element.EnumerateObject().Select(property => property.Name))}]"
            : $"a JSON {element.ValueKind.ToString().ToLowerInvariant()}";
}
