namespace Hostingaffe.Domain.Identities;

/// <summary>
/// The name an agent gets when its creator gives it none: two words and a
/// number, <c>quiet-otter-42</c>, so that the history, the claim display and the
/// lists never say "Token 7f3a…" (VISION 12). Renamed at will afterwards.
/// </summary>
public static class AgentName
{
    private static readonly string[] Adjectives =
    [
        "quiet", "brisk", "calm", "bright", "steady", "swift", "keen", "patient", "bold", "gentle",
        "sober", "nimble", "plain", "candid", "earnest", "tidy", "lucid", "mellow", "sturdy", "wry",
        "eager", "frank", "humble", "modest", "ready", "sharp", "silent", "sound", "spry", "true",
    ];

    private static readonly string[] Animals =
    [
        "otter", "heron", "badger", "lynx", "marten", "finch", "beaver", "wren", "falcon", "hare",
        "seal", "stoat", "moth", "newt", "crane", "raven", "ibex", "tern", "vole", "pike",
        "owl", "fox", "elk", "kite", "trout", "swan", "mole", "gull", "hawk", "toad",
    ];

    /// <summary>A fresh name; uniqueness is the caller's to check.</summary>
    public static string Assign(Random? random = null)
    {
        random ??= Random.Shared;

        return $"{Adjectives[random.Next(Adjectives.Length)]}-{Animals[random.Next(Animals.Length)]}-{random.Next(1, 100)}";
    }
}
