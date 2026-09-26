using FluentAssertions;
using RemoteDesktop.Core.Crypto;
using RemoteDesktop.Core.Media;
using RemoteDesktop.Core.Protocol;
using Xunit;

namespace RemoteDesktop.Tests;

public class SignalingIntegrityTests
{
    private static SignalEnvelope Offer(string from, string to, string sdp)
        => new(SignalKind.Offer, from, to, sdp);

    [Fact]
    public void Signed_signal_verifies_against_the_paired_sender_key()
    {
        using var sender = DeviceIdentity.Create();
        using var receiver = DeviceIdentity.Create();

        var signed = SignedSignal.Create(sender, Offer(sender.DeviceId, receiver.DeviceId, "v=0..."));
        signed.Verify(sender.Public).Should().BeTrue();
    }

    [Fact]
    public void Signed_signal_from_an_unpaired_key_is_rejected()
    {
        using var sender = DeviceIdentity.Create();
        using var attacker = DeviceIdentity.Create();
        using var receiver = DeviceIdentity.Create();

        var signed = SignedSignal.Create(sender, Offer(sender.DeviceId, receiver.DeviceId, "sdp"));
        signed.Verify(attacker.Public).Should().BeFalse();
    }

    [Fact]
    public void Tampered_payload_breaks_the_signature()
    {
        // Simulates a MITM signaling server swapping the SDP (and its DTLS fingerprint).
        using var sender = DeviceIdentity.Create();
        using var receiver = DeviceIdentity.Create();

        var signed = SignedSignal.Create(sender, Offer(sender.DeviceId, receiver.DeviceId, "original-sdp"));
        var tampered = signed with { Envelope = signed.Envelope with { Payload = "attacker-sdp" } };

        tampered.Verify(sender.Public).Should().BeFalse();
    }

    [Fact]
    public void Envelope_sender_id_must_match_the_verifying_key()
    {
        using var sender = DeviceIdentity.Create();
        using var receiver = DeviceIdentity.Create();

        var signed = SignedSignal.Create(sender, Offer("spoofed-id", receiver.DeviceId, "sdp"));
        signed.Verify(sender.Public).Should().BeFalse();
    }

    [Fact]
    public void Sdp_fingerprint_is_extracted_and_matched()
    {
        const string sdp = "v=0\r\n" +
                           "a=setup:actpass\r\n" +
                           "a=fingerprint:sha-256 AB:CD:EF:01:23:45:67:89:AB:CD:EF:01:23:45:67:89\r\n" +
                           "a=ice-ufrag:abcd\r\n";

        var fps = SdpFingerprint.Extract(sdp);
        fps.Should().ContainSingle();
        fps[0].Hash.Should().Be("sha-256");

        SdpFingerprint.Contains(sdp, "sha-256", "ab:cd:ef:01:23:45:67:89:ab:cd:ef:01:23:45:67:89")
            .Should().BeTrue();
        SdpFingerprint.Contains(sdp, "sha-256", "00:00").Should().BeFalse();
    }
}
