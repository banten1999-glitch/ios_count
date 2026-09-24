using RemoteDesktop.Core.Protocol;
using SIPSorcery.Net;

namespace RemoteDesktop.Platform.Windows.Rtc;

/// <summary>Builds the ICE server list from issued TURN credentials plus a public STUN server.</summary>
public static class IceServerFactory
{
    public static IReadOnlyList<RTCIceServer> Build(TurnCredentials turn, string? stunUrl = null)
    {
        var servers = new List<RTCIceServer>();

        if (!string.IsNullOrEmpty(stunUrl))
            servers.Add(new RTCIceServer { urls = stunUrl });

        if (turn.Uris.Length > 0 && !string.IsNullOrEmpty(turn.Username))
        {
            servers.Add(new RTCIceServer
            {
                urls = string.Join(',', turn.Uris),
                username = turn.Username,
                credential = turn.Password,
                credentialType = RTCIceCredentialType.password
            });
        }

        return servers;
    }
}
