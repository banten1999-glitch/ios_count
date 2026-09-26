using System.Runtime.Versioning;
using System.Text.Json;
using RemoteDesktop.Core.Crypto;
using RemoteDesktop.Core.Pairing;
using RemoteDesktop.Core.Protocol;
using RemoteDesktop.Platform.Windows.Signaling;

namespace RemoteDesktop.Platform.Windows.Pairing;

/// <summary>
/// Host side of pairing over the signaling relay. When a controller submits a signed
/// PairRequest, the proof (key possession + knowledge of the code shown locally) is verified,
/// then the owner is asked to confirm ON THIS MACHINE via <see cref="ConfirmAsync"/>. Only on
/// confirmation is the controller trusted and the Host's public key returned.
///
/// Pre-trust note: the PairRequest is verified by its embedded PairingProof and the SignedSignal
/// signature is checked against the controller key carried IN the request (self-asserted) purely
/// for message integrity — trust comes from the code + local confirmation, not from this key
/// already being known.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class HostPairingCoordinator
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly DeviceIdentity _identity;
    private readonly HostPairingService _pairing;
    private readonly SignalingClient _signaling;
    private readonly Func<string, Task<bool>> _confirmAsync;

    /// <param name="confirmAsync">
    /// Shows the local confirmation UI for the given controller device id and resolves true if
    /// the owner approves. This is the mandatory physical confirmation step.
    /// </param>
    public HostPairingCoordinator(
        DeviceIdentity identity,
        HostPairingService pairing,
        SignalingClient signaling,
        Func<string, Task<bool>> confirmAsync)
    {
        _identity = identity;
        _pairing = pairing;
        _signaling = signaling;
        _confirmAsync = confirmAsync;
        _signaling.SignalReceived += OnSignal;
    }

    /// <summary>Begin pairing and return the code to display locally on the Host.</summary>
    public PairingCode BeginPairing(TimeSpan validity) => _pairing.Begin(validity);

    private async void OnSignal(SignedSignal signal)
    {
        if (signal.Envelope.Kind != SignalKind.PairRequest) return;
        try
        {
            Diagnostics.FileLog.Info($"Host: pair request received from {signal.Envelope.FromDeviceId}");
            var req = JsonSerializer.Deserialize<ControllerPairingRequest>(signal.Envelope.Payload, Json);
            if (req is null) return;

            var controllerKey = DevicePublicKey.FromBase64(req.ControllerPublicKeyBase64);

            // Integrity: the envelope must be signed by the key presented in the request.
            if (!signal.Verify(controllerKey)) { Reject(req.HostDeviceId, controllerKey.DeviceId, "bad_signature"); return; }
            if (req.HostDeviceId != _identity.DeviceId) { Reject(req.HostDeviceId, controllerKey.DeviceId, "wrong_host"); return; }

            var submit = _pairing.Submit(req.Code, controllerKey, req.IssuedAtUnixMs,
                Convert.FromBase64String(req.SignatureBase64));
            Diagnostics.FileLog.Info($"Host: pair submit verdict = {submit.Verdict}");
            if (submit.Verdict != PairingSubmitVerdict.AwaitingConfirmation)
            {
                Reject(req.HostDeviceId, controllerKey.DeviceId, submit.Verdict.ToString());
                return;
            }

            // Mandatory local confirmation on the Host.
            bool approved = await _confirmAsync(controllerKey.DeviceId);
            Diagnostics.FileLog.Info($"Host: pair confirmation approved = {approved}");
            if (!approved) { _pairing.Cancel(); Reject(req.HostDeviceId, controllerKey.DeviceId, "declined"); return; }

            _pairing.Confirm(displayName: controllerKey.DeviceId);
            Diagnostics.FileLog.Info($"Host: device trusted and PairAccepted sent to {controllerKey.DeviceId}");
            SendTo(controllerKey.DeviceId, SignalKind.PairAccepted, _identity.Public.ToBase64());
        }
        catch (Exception ex) { Diagnostics.FileLog.Error("Host: pair request handling failed", ex); }
    }

    private void Reject(string _, string controllerId, string reason)
        => SendTo(controllerId, SignalKind.PairRejected, reason);

    private void SendTo(string toDeviceId, SignalKind kind, string payload)
    {
        var env = new SignalEnvelope(kind, _identity.DeviceId, toDeviceId, payload);
        _ = _signaling.SendAsync(SignedSignal.Create(_identity, env));
    }
}
