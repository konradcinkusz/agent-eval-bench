namespace AbsenceConcierge.AgentService.Agent.Language;

/// <summary>
/// Reads a versioned prompt from <c>prompts/</c>.
///
/// <para>
/// Prompts are files, never string literals. A prompt is behaviour with no code
/// diff, and the whole change-coupling rule in <c>docs/SPEC.md</c> §10 — a prompt
/// edit must land with a specification change — is enforced by
/// <c>scripts/check-change-coupling.mjs</c> watching a path. A literal in a
/// <c>.cs</c> file is invisible to that check, and gets edited the way a constant
/// gets edited.
/// </para>
/// </summary>
public interface IPromptLibrary
{
    string Read(string name);
}

public sealed class PromptLibrary(string directory) : IPromptLibrary
{
    public const string ReplyComposer = "reply-composer";

    private readonly Dictionary<string, string> _cache = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();

    public string Read(string name)
    {
        lock (_gate)
        {
            if (_cache.TryGetValue(name, out var cached))
            {
                return cached;
            }

            var path = Path.Combine(directory, $"{name}.md");

            if (!File.Exists(path))
            {
                // Loudly. A missing prompt file must not become an empty system
                // prompt: an unprompted model answers, plausibly, with something
                // nobody specified — which is the failure mode hardest to notice and
                // hardest to attribute afterwards.
                throw new FileNotFoundException(
                    $"Prompt '{name}' was not found at '{path}'. Prompts are files under prompts/ so that "
                    + "editing one is a reviewable diff that CI couples to a specification change.",
                    path);
            }

            var text = File.ReadAllText(path);
            _cache[name] = text;
            return text;
        }
    }
}
