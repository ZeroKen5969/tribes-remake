using Godot;

namespace Tribes.Client.MatchPlay.Dev;

/// <summary>
/// Sends made-up input through the engine as if a person did it: touches and mouse clicks at
/// logical screen positions (converted to window pixels), drags, and keys. Used by
/// <see cref="MatchScript"/>.
/// </summary>
public static class ScriptInput
{
    private const int DragSteps = 8;

    /// <summary>A tap (touch) or left click at logical point <paramref name="at"/>.</summary>
    public static void Tap(Node context, System.Numerics.Vector2 at, bool touch) => Tap(context, new Vector2(at.X, at.Y), touch);

    /// <summary>A tap (touch) or left click at logical point <paramref name="at"/>.</summary>
    public static void Tap(Node context, Vector2 at, bool touch)
    {
        Vector2 p = ToWindow(context, at);
        if (touch)
        {
            Godot.Input.ParseInputEvent(new InputEventScreenTouch { Index = 0, Position = p, Pressed = true });
            Godot.Input.ParseInputEvent(new InputEventScreenTouch { Index = 0, Position = p, Pressed = false });
            return;
        }
        Godot.Input.ParseInputEvent(new InputEventMouseMotion { Position = p, GlobalPosition = p });
        Godot.Input.ParseInputEvent(new InputEventMouseButton { Position = p, GlobalPosition = p, ButtonIndex = MouseButton.Left, Pressed = true, ButtonMask = MouseButtonMask.Left });
        Godot.Input.ParseInputEvent(new InputEventMouseButton { Position = p, GlobalPosition = p, ButtonIndex = MouseButton.Left, Pressed = false });
    }

    /// <summary>A right click at logical point <paramref name="at"/>.</summary>
    public static void RightClick(Node context, System.Numerics.Vector2 at) => RightClick(context, new Vector2(at.X, at.Y));

    /// <summary>A right click at logical point <paramref name="at"/>.</summary>
    public static void RightClick(Node context, Vector2 at)
    {
        Vector2 p = ToWindow(context, at);
        Godot.Input.ParseInputEvent(new InputEventMouseMotion { Position = p, GlobalPosition = p });
        Godot.Input.ParseInputEvent(new InputEventMouseButton { Position = p, GlobalPosition = p, ButtonIndex = MouseButton.Right, Pressed = true, ButtonMask = MouseButtonMask.Right });
        Godot.Input.ParseInputEvent(new InputEventMouseButton { Position = p, GlobalPosition = p, ButtonIndex = MouseButton.Right, Pressed = false });
    }

    /// <summary>A drag from <paramref name="from"/> to <paramref name="to"/> (one finger, or the left button).</summary>
    public static void Drag(Node context, System.Numerics.Vector2 from, System.Numerics.Vector2 to, bool touch) =>
        Drag(context, new Vector2(from.X, from.Y), new Vector2(to.X, to.Y), touch);

    /// <summary>A drag from <paramref name="from"/> to <paramref name="to"/> (one finger, or the left button).</summary>
    public static void Drag(Node context, Vector2 from, Vector2 to, bool touch)
    {
        Vector2 a = ToWindow(context, from), b = ToWindow(context, to);
        if (touch)
        {
            Godot.Input.ParseInputEvent(new InputEventScreenTouch { Index = 0, Position = a, Pressed = true });
        }
        else
        {
            Godot.Input.ParseInputEvent(new InputEventMouseButton { Position = a, GlobalPosition = a, ButtonIndex = MouseButton.Left, Pressed = true, ButtonMask = MouseButtonMask.Left });
        }
        Vector2 last = a;
        for (int i = 1; i <= DragSteps; i++)
        {
            Vector2 p = a.Lerp(b, i / (float)DragSteps);
            if (touch)
            {
                Godot.Input.ParseInputEvent(new InputEventScreenDrag { Index = 0, Position = p, Relative = p - last });
            }
            else
            {
                Godot.Input.ParseInputEvent(new InputEventMouseMotion { Position = p, GlobalPosition = p, Relative = p - last, ButtonMask = MouseButtonMask.Left });
            }
            last = p;
        }
        if (touch)
        {
            Godot.Input.ParseInputEvent(new InputEventScreenTouch { Index = 0, Position = b, Pressed = false });
        }
        else
        {
            Godot.Input.ParseInputEvent(new InputEventMouseButton { Position = b, GlobalPosition = b, ButtonIndex = MouseButton.Left, Pressed = false });
        }
    }

    /// <summary>Presses and lets go of the key named <paramref name="name"/> ("B", "Escape", "Space", "Ctrl+1", "Alt+Enter").</summary>
    public static void Key(string name)
    {
        bool ctrl = name.StartsWith("Ctrl+", System.StringComparison.Ordinal);
        bool shift = name.StartsWith("Shift+", System.StringComparison.Ordinal);
        bool alt = name.StartsWith("Alt+", System.StringComparison.Ordinal);
        string keyName = ctrl ? name[5..] : shift ? name[6..] : alt ? name[4..] : name;
        Key key = OS.FindKeycodeFromString(keyName);
        if (key == Godot.Key.None)
        {
            throw new System.FormatException($"unknown key {name}");
        }
        Godot.Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = true, CtrlPressed = ctrl, ShiftPressed = shift, AltPressed = alt });
        Godot.Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = false, CtrlPressed = ctrl, ShiftPressed = shift, AltPressed = alt });
    }

    /// <summary>A logical screen point in window pixels.</summary>
    private static Vector2 ToWindow(Node context, Vector2 logical) => context.GetViewport().GetFinalTransform() * logical;
}
