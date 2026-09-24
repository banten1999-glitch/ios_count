using RemoteDesktop.Core.Crypto;
using RemoteDesktop.Core.Protocol;
using RemoteDesktop.Core.Trust;

namespace RemoteDesktop.Core.Pairing;

public enum PairingSubmitVerdict
{
    /// <summary>Proof accepted; awaiting explicit local confirmation on the Host.</summary>
    AwaitingConfirmation,
    NoPendingCode,
    CodeExpired,
    WrongCode,
    TooManyAttempts,
    BadProof
}

/// <summary>Result of a controller submitting its pairing proof.</summary>
public sealed record PairingSubmitResult(PairingSubmitVerdict Verdict, string? ControllerDeviceId = null);

/// <summary>
/// Host-side pairing state machine. The flow enforces the security properties from the
/// threat model:
///   1. <see cref="Begin"/> creates a short-lived, single-active pairing code shown locally.
///   2. <see cref="Submit"/> checks the code (constant-time), enforces expiry and an attempt
///      cap (T14), and verifies the controller's proof of key possession (T1). It does NOT
///      grant trust yet.
///   3. <see cref="Confirm"/> represents the explicit physical confirmation on the Host and
///      is the only step that adds the controller to the trusted store (T12).
///
/// A single pairing is in flight at a time; starting a new one supersedes any pending code.
/// </summary>
public sealed class HostPairingService
{
    private readonly DeviceIdentity _hostIdentity;
    private readonly ITrustedDeviceStore _trustStore;
    private readonly TimeProvider _clock;
    private readonly int _maxAttempts;
    private readonly TimeSpan _proofFreshness;
    private readonly object _gate = new();

    private PendingPairing? _pending;

    public HostPairingService(
        DeviceIdentity hostIdentity,
        ITrustedDeviceStore trustStore,
        TimeProvider? clock = null,
        int maxAttempts = 5,
        TimeSpan? proofFreshness = null)
    {
        _hostIdentity = hostIdentity;
        _trustStore = trustStore;
        _clock = clock ?? TimeProvider.System;
        _maxAttempts = maxAttempts;
        _proofFreshness = proofFreshness ?? TimeSpan.FromMinutes(5);
    }

    public string HostDeviceId => _hostIdentity.DeviceId;

    /// <summary>Start pairing: generate and return a code to display locally on the Host.</summary>
    public PairingCode Begin(TimeSpan validity)
    {
        var code = PairingCode.Generate(validity, _clock);
        lock (_gate)
            _pending = new PendingPairing(code);
        return code;
    }

    /// <summary>Cancel any in-flight pairing (e.g. owner dismissed the dialog).</summary>
    public void Cancel()
    {
        lock (_gate)
            _pending = null;
    }

    /// <summary>
    /// A controller submits its public key, the code it was given, and a signed proof.
    /// </summary>
    public PairingSubmitResult Submit(
        string candidateCode,
        DevicePublicKey controllerKey,
        long issuedAtUnixMs,
        ReadOnlySpan<byte> signature)
    {
        lock (_gate)
        {
            if (_pending is null)
                return new PairingSubmitResult(PairingSubmitVerdict.NoPendingCode);

            if (_pending.Code.IsExpired(_clock))
            {
                _pending = null;
                return new PairingSubmitResult(PairingSubmitVerdict.CodeExpired);
            }

            if (_pending.Attempts >= _maxAttempts)
            {
                _pending = null;
                return new PairingSubmitResult(PairingSubmitVerdict.TooManyAttempts);
            }

            // Reject stale proofs regardless of code validity window.
            long nowMs = _clock.GetUtcNow().ToUnixTimeMilliseconds();
            if (Math.Abs(nowMs - issuedAtUnixMs) > _proofFreshness.TotalMilliseconds)
            {
                _pending.Attempts++;
                return new PairingSubmitResult(PairingSubmitVerdict.BadProof);
            }

            if (!_pending.Code.Matches(candidateCode))
            {
                _pending.Attempts++;
                return new PairingSubmitResult(PairingSubmitVerdict.WrongCode);
            }

            bool ok = PairingProof.Verify(
                controllerKey, _pending.Code.Value, _hostIdentity.DeviceId, issuedAtUnixMs, signature);
            if (!ok)
            {
                _pending.Attempts++;
                return new PairingSubmitResult(PairingSubmitVerdict.BadProof);
            }

            _pending.VerifiedController = controllerKey;
            return new PairingSubmitResult(PairingSubmitVerdict.AwaitingConfirmation, controllerKey.DeviceId);
        }
    }

    /// <summary>
    /// The owner confirmed on the Host. Adds the verified controller to the trusted store and
    /// clears the pending pairing. Returns the created entry, or null if there was nothing to
    /// confirm (no verified controller pending).
    /// </summary>
    public TrustedDevice? Confirm(string displayName)
    {
        lock (_gate)
        {
            var controller = _pending?.VerifiedController;
            if (controller is null)
                return null;

            var device = new TrustedDevice(
                controller.DeviceId,
                controller.ToBase64(),
                displayName,
                _clock.GetUtcNow());
            _trustStore.Upsert(device);
            _pending = null;
            return device;
        }
    }

    private sealed class PendingPairing(PairingCode code)
    {
        public PairingCode Code { get; } = code;
        public int Attempts { get; set; }
        public DevicePublicKey? VerifiedController { get; set; }
    }
}
