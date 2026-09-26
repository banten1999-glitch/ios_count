using System.Text.Json;
using RemoteDesktop.Core.Protocol;

namespace RemoteDesktop.Core.Trust;

/// <summary>
/// File-backed trusted-device store (JSON). Public keys are not secret, so no encryption is
/// needed here; integrity of this file matters, not confidentiality. Writes are atomic
/// (temp file + move) so a crash mid-write can't corrupt the list.
/// </summary>
public sealed class JsonFileTrustedDeviceStore : ITrustedDeviceStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _path;
    private readonly object _gate = new();
    private readonly Dictionary<string, TrustedDevice> _devices;

    public JsonFileTrustedDeviceStore(string path)
    {
        _path = path;
        _devices = Load(path);
    }

    public IReadOnlyList<TrustedDevice> List()
    {
        lock (_gate)
            return _devices.Values.OrderBy(d => d.PairedAt).ToList();
    }

    public TrustedDevice? Find(string deviceId)
    {
        lock (_gate)
            return _devices.TryGetValue(deviceId, out var d) ? d : null;
    }

    public void Upsert(TrustedDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        lock (_gate)
        {
            _devices[device.DeviceId] = device;
            Persist();
        }
    }

    public bool Revoke(string deviceId, DateTimeOffset when)
    {
        lock (_gate)
        {
            if (!_devices.TryGetValue(deviceId, out var existing))
                return false;
            _devices[deviceId] = existing with { RevokedAt = when };
            Persist();
            return true;
        }
    }

    private static Dictionary<string, TrustedDevice> Load(string path)
    {
        if (!File.Exists(path))
            return new Dictionary<string, TrustedDevice>(StringComparer.Ordinal);

        var list = JsonSerializer.Deserialize<List<TrustedDevice>>(File.ReadAllText(path))
                   ?? new List<TrustedDevice>();
        return list.ToDictionary(d => d.DeviceId, StringComparer.Ordinal);
    }

    private void Persist()
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(_path));
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var tmp = _path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(_devices.Values.ToList(), JsonOptions));
        File.Move(tmp, _path, overwrite: true);
    }
}
