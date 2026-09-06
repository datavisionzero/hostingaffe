namespace Hostingaffe.Domain.Installations;

/// <summary>The transport a port speaks (<c>CONTEXT.md</c>, Installation). Closed.</summary>
public enum Protocol
{
    Tcp,
    Udp,
}

/// <summary>
/// How far a port is reachable (<c>CONTEXT.md</c>, Installation). Closed.
/// </summary>
public enum Scope
{
    /// <summary>From the internet.</summary>
    Public,

    /// <summary>From the operator's own network.</summary>
    Private,

    /// <summary>From a container network, and nowhere else.</summary>
    Internal,
}

/// <summary>
/// One port an installation listens on, and how far it is reachable
/// (VISION 7).
/// </summary>
/// <remarks>
/// <para>
/// It is an object and not the string <c>443/tcp:public</c>, because that
/// spelling is a <em>rendering</em>: as a field it would be the one place in
/// the model where a value carries a grammar of its own, the check would move
/// into a regular expression, "every installation with a public port" would
/// become a text search instead of a query, and the generated clients would see
/// a <c>string</c> they can read nothing out of.
/// </para>
/// <para>
/// The spelling stays what a person reads and types — the CLI and the interface
/// convert, and the history writes it, because a history value is something a
/// person reads.
/// </para>
/// </remarks>
public sealed class Port
{
    public const int Lowest = 1;

    public const int Highest = 65535;

    private Port()
    {
        // EF Core materializes through this; every other route goes through Of.
    }

    private Port(int number, Protocol protocol, Scope scope)
    {
        Number = number;
        Protocol = protocol;
        Scope = scope;
    }

    /// <summary>The number itself; <c>port</c> on the wire.</summary>
    public int Number { get; private init; }

    public Protocol Protocol { get; private init; }

    public Scope Scope { get; private init; }

    /// <exception cref="ArgumentException">The number is not a port, or a word is not one of its set.</exception>
    public static Port Of(int number, Protocol protocol, Scope scope) =>
        number is < Lowest or > Highest
            ? throw new ArgumentException($"A port is between {Lowest} and {Highest}.", nameof(number))
            : !Enum.IsDefined(protocol)
                ? throw new ArgumentException("Not a protocol.", nameof(protocol))
                : !Enum.IsDefined(scope)
                    ? throw new ArgumentException("Not a scope.", nameof(scope))
                    : new Port(number, protocol, scope);

    /// <summary>
    /// The spelling VISION 7 shows — <c>443/tcp:public</c>. It is what a person
    /// reads, and it is what a history row carries for the same reason.
    /// </summary>
    public override string ToString() => $"{Number}/{Spelling.Of(Protocol)}:{Spelling.Of(Scope)}";
}
