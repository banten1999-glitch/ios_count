using System.Runtime.Versioning;
using System.Text.Json;
using RemoteDesktop.Core.Crypto;
using RemoteDesktop.Core.Pairing;
using RemoteDesktop.Core.Protocol;
using RemoteDesktop.Core.Trust;
using RemoteDesktop.Platform.Windows.Signaling;

namespace RemoteDesktop.Platform.Windows.Pairing;

/// <summary>
/// Controller side of pairing. The user enters the Host's device id and the code shown on the
/// Host; this sends a signed PairRequest and waits for PairAccepted (carrying the Host public
/// key, which is stored so the Host can be verified on future connections) or PairRejected.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ControllerPairingCoordinator
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly DeviceIdentity _identity;
    private readonly SignalingClient _signaling;
    private readonly ITrustedDeviceStore _trustStore;

    private TaskCompletionSource<PairingOutcome>? _pending;
    private string? _expectedHostId;

    public ControllerPairingCoordinator(
        DeviceIdentity identity,
        SignalingClient signaling,
        ITrustedDeviceStore trustStore)
    {
        _identity = identity;
        _signaling = signaling;
        _trustStore = trustStore;
        _signaling.SignalReceived += OnSignal;
    }

    /// <summary>Attempt to pair with the given Host id + code. Resolves with the outcome.</summary>
    public async Task<PairingOutcome> PairAsync(string hostDeviceId, string code, TimeSpan timeout)
    {
        _expectedHostId = hostDeviceId;
        _pending = new TaskCompletionSource<PairingOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);

        var req = ControllerPairing.CreateRequest(_identity, code, hostDeviceId);
        var env = new SignalEnvelope(SignalKind.PairRequest, _identity.DeviceId, hostDeviceId,
            JsonSerializer.Serialize(req, Json));
        await _signaling.SendAsync(SignedSignal.Create(_identity, env));

        using var cts = new CancellationTokenSource(timeout);
        await using var _ = cts.Token.Register(() => _pending?.TrySetResult(new PairingOutcome(false, "timeout")));
        return await _pending.Task;
    }

    private void OnSignal(SignedSignal signal)
    {
        var env = signal.Envelope;
        if (env.FromDeviceId != _expectedHostId || _pending is null) return;

        try
        {
            switch (env.Kind)
            {
                case SignalKind.PairAccepted:
                    var hostKey = DevicePublicKey.FromBase64(env.Payload);
                    // Integrity: the acceptance must be signed by the very key we are about to trust.
                    if (hostKey.DeviceId != env.FromDeviceId || !signal.Verify(hostKey))
                    {
                        _pending.TrySetResult(new PairingOutcome(false, "bad_host_signature"));
                        return;
                    }
                    _trustStore.Upsert(new TrustedDevice(hostKey.DeviceId, hostKey.ToBase64(),
                        "Host " + hostKey.DeviceId, DateTimeOffset.UtcNow));
                    _pending.TrySetResult(new PairingOutcome(true, null, hostKey));
                    break;

                case SignalKind.PairRejected:
                    _pending.TrySetResult(new PairingOutcome(false, env.Payload));
                    break;
            }
        }
        catch
        {
            _pending.TrySetResult(new PairingOutcome(false, "error"));
        }
    }
}

/// <summary>Result of a controller pairing attempt.</summary>
public sealed record PairingOutcome(bool Success, string? Reason, DevicePublicKey? HostKey = null);
