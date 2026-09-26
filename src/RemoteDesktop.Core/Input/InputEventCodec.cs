using System.Text.Json;

namespace RemoteDesktop.Core.Input;

/// <summary>
/// JSON (de)serialization for <see cref="InputEvent"/> over the WebRTC data channel. Uses the
/// polymorphic type discriminator declared on the base record. Decoding never throws on bad
/// input — it returns null so a malformed/hostile message is dropped rather than crashing the
/// input pump.
/// </summary>
public static class InputEventCodec
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string Encode(InputEvent evt) => JsonSerializer.Serialize(evt, Options);

    public static InputEvent? TryDecode(string json)
    {
        try { return JsonSerializer.Deserialize<InputEvent>(json, Options); }
        catch (JsonException) { return null; }
    }
}
