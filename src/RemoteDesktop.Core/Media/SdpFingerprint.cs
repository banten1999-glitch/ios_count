using System.Text.RegularExpressions;

namespace RemoteDesktop.Core.Media;

/// <summary>
/// Helpers for the DTLS fingerprint carried in an SDP (a=fingerprint:&lt;hash&gt; &lt;value&gt;).
/// The fingerprint is what DTLS uses to bind the encrypted transport to the offer/answer.
/// Since our signaling envelopes are signed end-to-end (see SignedSignal), this fingerprint
/// is protected against substitution by the signaling server; these helpers let the peers
/// log and cross-check it defensively.
/// </summary>
public static partial class SdpFingerprint
{
    [GeneratedRegex(@"^a=fingerprint:(?<hash>[\w-]+)\s+(?<value>[0-9A-Fa-f:]+)\s*$",
        RegexOptions.Multiline)]
    private static partial Regex FingerprintLine();

    /// <summary>All (hashAlgorithm, value) fingerprints declared in an SDP blob.</summary>
    public static IReadOnlyList<(string Hash, string Value)> Extract(string sdp)
    {
        if (string.IsNullOrEmpty(sdp)) return Array.Empty<(string, string)>();
        var result = new List<(string, string)>();
        foreach (Match m in FingerprintLine().Matches(sdp))
            result.Add((m.Groups["hash"].Value.ToLowerInvariant(),
                        m.Groups["value"].Value.ToUpperInvariant()));
        return result;
    }

    /// <summary>True if the SDP contains the given fingerprint (case-insensitive).</summary>
    public static bool Contains(string sdp, string hash, string value)
    {
        foreach (var (h, v) in Extract(sdp))
            if (string.Equals(h, hash, StringComparison.OrdinalIgnoreCase)
                && string.Equals(v, value, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }
}
