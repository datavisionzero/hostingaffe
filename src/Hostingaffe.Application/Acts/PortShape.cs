using Hostingaffe.Domain;

namespace Hostingaffe.Application.Acts;

/// <summary>
/// One port something listens on, as the contract carries it:
/// <c>{ "port": 443, "protocol": "tcp", "scope": "public" }</c>.
/// </summary>
/// <remarks>
/// <para>
/// It is an installation's and a machine's alike, which is why it stands on its
/// own rather than beside either of them: what an installation listens on is
/// the installation's, and what belongs to the machine and to no installation
/// of it — SSH, a Wireguard endpoint — is the machine's, in the same shape.
/// </para>
/// <para>
/// An object rather than the string <c>443/tcp:public</c>, because that
/// spelling is a rendering: as a field it would be the one value in the model
/// with a grammar of its own, and a generated client would see a <c>string</c>
/// it can read nothing out of. The spelling is what the CLI and the interface
/// show and take.
/// </para>
/// </remarks>
public sealed record PortShape(int Port, Protocol Protocol, Scope Scope)
{
    public static PortShape Of(Port port)
    {
        ArgumentNullException.ThrowIfNull(port);
        return new PortShape(port.Number, port.Protocol, port.Scope);
    }
}
