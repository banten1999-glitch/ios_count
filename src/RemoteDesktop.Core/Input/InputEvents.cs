using System.Text.Json.Serialization;

namespace RemoteDesktop.Core.Input;

/// <summary>
/// Input events sent over the WebRTC data channel from Controller to Host. Coordinates are
/// normalized to [0,1] over the target screen so they survive different resolutions and DPI
/// scaling (resolved to pixels on the Host per the active monitor). The Host executes these
/// via SendInput within its granted permissions only.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "t")]
[JsonDerivedType(typeof(MouseMoveEvent), "mm")]
[JsonDerivedType(typeof(MouseButtonEvent), "mb")]
[JsonDerivedType(typeof(MouseWheelEvent), "mw")]
[JsonDerivedType(typeof(KeyEvent), "key")]
public abstract record InputEvent
{
    /// <summary>Monotonic sequence number, used to drop out-of-order/stale events.</summary>
    [JsonPropertyName("seq")] public long Sequence { get; init; }
}

/// <summary>Absolute mouse move, normalized to [0,1] on the target monitor.</summary>
public sealed record MouseMoveEvent : InputEvent
{
    [JsonPropertyName("x")] public double X { get; init; }
    [JsonPropertyName("y")] public double Y { get; init; }
    [JsonPropertyName("mon")] public int MonitorIndex { get; init; }
}

public enum MouseButton { Left, Right, Middle, XButton1, XButton2 }

/// <summary>Mouse button down/up. Down events are tracked so they can be released on drop.</summary>
public sealed record MouseButtonEvent : InputEvent
{
    [JsonPropertyName("btn")] public MouseButton Button { get; init; }
    [JsonPropertyName("down")] public bool IsDown { get; init; }
}

public sealed record MouseWheelEvent : InputEvent
{
    [JsonPropertyName("dx")] public int DeltaX { get; init; }
    [JsonPropertyName("dy")] public int DeltaY { get; init; }
}

/// <summary>Key down/up by Windows virtual-key code. Down keys are tracked for release-on-drop.</summary>
public sealed record KeyEvent : InputEvent
{
    [JsonPropertyName("vk")] public int VirtualKey { get; init; }
    [JsonPropertyName("down")] public bool IsDown { get; init; }
}
