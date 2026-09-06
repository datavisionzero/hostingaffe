using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

// Both namespaces carry an `IPNetwork`; the options below take the framework's.
using IPNetwork = System.Net.IPNetwork;

namespace Hostingaffe.Api.Http;

/// <summary>
/// Which peers may speak for the caller: the reverse proxy in front of the
/// instance, named by <c>HOSTINGAFFE_TRUSTED_PROXY</c> (<c>docs/operations.md</c>).
/// </summary>
/// <remarks>
/// <para>
/// Nothing is trusted until an operator says so, because <c>X-Forwarded-For</c>
/// is a header any client can write. Unset, the instance reads the socket, and
/// behind a proxy that is the proxy's own address for every request — which is
/// what made the per-address login limit an instance-wide lockout: twenty
/// failed sign-ins by anybody stopped everybody for the length of the window.
/// </para>
/// <para>
/// The value is a comma-separated list of addresses and CIDR networks, or
/// <c>all</c> where the proxy's address is not known in advance — a container
/// on a Compose network that is renumbered on every start. <c>all</c> trusts
/// whoever connects, so it belongs behind a proxy that is the only thing able
/// to reach the instance, which is what publishing no port but the proxy's does.
/// </para>
/// </remarks>
public sealed record TrustedProxies(bool Any, IReadOnlyList<IPAddress> Addresses, IReadOnlyList<IPNetwork> Networks)
{
    public const string Variable = "HOSTINGAFFE_TRUSTED_PROXY";

    public const string Anything = "all";

    public bool Configured => Any || Addresses.Count > 0 || Networks.Count > 0;

    /// <exception cref="ArgumentException">An entry is neither an address, a network nor <c>all</c>.</exception>
    public static TrustedProxies FromVariable(string? value)
    {
        var entries = (value ?? string.Empty)
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        var addresses = new List<IPAddress>();
        var networks = new List<IPNetwork>();
        var any = false;

        foreach (var entry in entries)
        {
            if (entry.Equals(Anything, StringComparison.OrdinalIgnoreCase))
            {
                any = true;
            }
            else if (entry.Contains('/', StringComparison.Ordinal))
            {
                networks.Add(IPNetwork.TryParse(entry, out var network)
                    ? network
                    : throw new ArgumentException($"{Variable}: {entry} is not a CIDR network.", Variable));
            }
            else
            {
                addresses.Add(IPAddress.TryParse(entry, out var address)
                    ? address
                    : throw new ArgumentException($"{Variable}: {entry} is not an IP address.", Variable));
            }
        }

        return new(any, addresses, networks);
    }

    /// <summary>
    /// The scheme and the caller's address, and nothing else: one hop, because
    /// the instance sits behind one proxy and a longer chain is not this
    /// product's to reason about.
    /// </summary>
    public ForwardedHeadersOptions Options()
    {
        var options = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
            ForwardLimit = 1,
        };

        // The defaults are loopback, and they would silently widen `all`.
        options.KnownProxies.Clear();
        options.KnownIPNetworks.Clear();

        if (!Any)
        {
            foreach (var address in Addresses) options.KnownProxies.Add(address);
            foreach (var network in Networks) options.KnownIPNetworks.Add(network);
        }

        return options;
    }
}
