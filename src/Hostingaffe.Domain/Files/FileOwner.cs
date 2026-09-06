namespace Hostingaffe.Domain.Files;

/// <summary>
/// What a file can belong to (<c>CONTEXT.md</c>, File): a systemd unit belongs
/// to the machine, a Compose file to the installation.
/// </summary>
public enum OwnerKind
{
    Machine,
    Installation,
}

/// <summary>
/// The one owner of a file: which kind of thing, which row, and the key it is
/// named by.
/// </summary>
/// <remarks>
/// The kind is part of the answer and not decoration. A key is unique per
/// entity type and not across them (<c>CONTEXT.md</c>, Key), so a machine
/// <c>caddy</c> and an installation <c>caddy</c> both exist and usually will —
/// an owner that carried only the key would name two things.
/// </remarks>
public sealed record FileOwner(OwnerKind Kind, Guid Id, string Key)
{
    public override string ToString() => $"{Spelling.Of(Kind)} {Key}";
}
