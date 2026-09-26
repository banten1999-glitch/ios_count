using System.Text.Json;
using SIPSorcery.Net;

namespace RemoteDesktop.Platform.Windows.Rtc;

/// <summary>
/// Serializes SDP and ICE to the string payload carried inside a signed signal envelope, and
/// back. Kept as small explicit DTOs so the wire form is stable across SIPSorcery versions.
/// </summary>
internal static class SignalPayloads
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private sealed record SdpDto(int Type, string Sdp);
    private sealed record IceDto(string Candidate, string? SdpMid, ushort SdpMLineIndex, string? UsernameFragment);

    public static string FromSdp(RTCSessionDescriptionInit sdp)
        => JsonSerializer.Serialize(new SdpDto((int)sdp.type, sdp.sdp), Json);

    public static RTCSessionDescriptionInit ToSdp(string payload)
    {
        var dto = JsonSerializer.Deserialize<SdpDto>(payload, Json)!;
        return new RTCSessionDescriptionInit { type = (RTCSdpType)dto.Type, sdp = dto.Sdp };
    }

    public static string FromIce(RTCIceCandidate candidate)
        => JsonSerializer.Serialize(
            new IceDto(candidate.candidate, candidate.sdpMid, candidate.sdpMLineIndex, candidate.usernameFragment), Json);

    public static RTCIceCandidateInit ToIce(string payload)
    {
        var dto = JsonSerializer.Deserialize<IceDto>(payload, Json)!;
        return new RTCIceCandidateInit
        {
            candidate = dto.Candidate,
            sdpMid = dto.SdpMid,
            sdpMLineIndex = dto.SdpMLineIndex,
            usernameFragment = dto.UsernameFragment
        };
    }
}
