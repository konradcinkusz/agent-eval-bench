using System.Net;
using Microsoft.Extensions.Options;

namespace AbsenceConcierge.AgentService.Demo;

/// <summary>
/// One answer to "which client is this?", shared by the rate limiter, the
/// per-client live allowance and the endpoints — because two components that
/// resolve the address differently are two components metering different people.
///
/// <para>
/// On Fly every TCP peer is the platform's edge proxy, so the socket address
/// alone would put every visitor in one bucket — the corporate-NAT collapse of
/// SERVICE-API-PATTERNS.md §1, applied to the whole internet. The platform states
/// the real client in <c>Fly-Client-IP</c>; it is read <b>only</b> when the
/// deployment says to (<see cref="DemoOptions.TrustProxyClientIpHeader"/>),
/// because with no proxy in front that header is client-supplied, and trusting it
/// would hand every visitor a bucket of their choosing.
/// </para>
/// </summary>
public static class DemoClientKey
{
    public const string FlyClientIpHeader = "Fly-Client-IP";

    public static string Resolve(HttpContext http)
    {
        ArgumentNullException.ThrowIfNull(http);

        var options = http.RequestServices.GetRequiredService<IOptions<DemoOptions>>().Value;

        if (options.TrustProxyClientIpHeader
            && http.Request.Headers.TryGetValue(FlyClientIpHeader, out var forwarded)
            && IPAddress.TryParse(forwarded.ToString(), out var client))
        {
            return client.ToString();
        }

        return http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }
}
