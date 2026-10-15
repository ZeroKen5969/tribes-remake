using System.Collections.Generic;
using Godot;

namespace Tribes.Client.Input;

/// <summary>
/// Turns touch and mouse input into the gestures the map understands, so the rest of the client
/// does not care whether it runs on a phone or a PC:
/// tap (select / order), long press (box selection on touch), drag (pan), pinch and wheel (zoom),
/// mouse drag with the left button (box selection) and right click (order).
/// </summary>
public partial class PointerGestures : Node
{
    /// <summary>How far a finger may move and still count as a tap, in screen pixels.</summary>
    [Export] public float TapSlop { get; set; } = 12f;

    /// <summary>How long a finger must rest to count as a long press, in seconds.</summary>
    [Export] public double LongPressSeconds { get; set; } = 0.45;

    /// <summary>Zoom factor of one mouse wheel notch.</summary>
    [Export] public float WheelZoomStep { get; set; } = 1.15f;

    /// <summary>A one-finger drag draws a selection box instead of panning (touch multi-select).</summary>
    public bool DragSelects { get; set; }

    /// <summary>A right click counts on release and only when the mouse did not drag (right drag pans).</summary>
    public bool SecondaryOnRelease { get; set; }

    /// <summary>A short touch or left click at a screen position.</summary>
    [Signal] public delegate void TappedEventHandler(Vector2 position);

    /// <summary>Two taps in quick succession at about the same place.</summary>
    [Signal] public delegate void DoubleTappedEventHandler(Vector2 position);

    /// <summary>A finger rested without moving.</summary>
    [Signal] public delegate void LongPressedEventHandler(Vector2 position);

    /// <summary>Right click (PC): an order at the position.</summary>
    [Signal] public delegate void SecondaryTappedEventHandler(Vector2 position);

    /// <summary>The view should move by <paramref name="delta"/> screen pixels.</summary>
    [Signal] public delegate void PannedEventHandler(Vector2 delta);

    /// <summary>The view should zoom by <paramref name="factor"/> around <paramref name="focus"/>.</summary>
    [Signal] public delegate void ZoomedEventHandler(float factor, Vector2 focus);

    /// <summary>A selection rectangle in screen pixels, while it is being drawn.</summary>
    [Signal] public delegate void BoxChangedEventHandler(Rect2 box);

    /// <summary>A selection rectangle was finished.</summary>
    [Signal] public delegate void BoxSelectedEventHandler(Rect2 box);

    private const double DoubleTapSeconds = 0.3;
    private const float TrackpadPanScale = 8f;

    private readonly Dictionary<int, Vector2> touches = new();
    private Vector2 pressStart;
    private double pressTime = -1;
    private bool moved;
    private bool boxing;
    private bool longPressFired;
    private float pinchDistance;
    private double lastTapTime = -10;
    private Vector2 lastTapPosition;
    private bool mouseLeftDown;
    private Vector2? rightPressAt;

    private static double Now => Time.GetTicksMsec() / 1000.0;

    /// <inheritdoc/>
    public override void _Process(double delta)
    {
        if (pressTime >= 0 && !moved && !longPressFired && touches.Count == 1 && Now - pressTime >= LongPressSeconds)
        {
            longPressFired = true;
            boxing = true;
            EmitSignal(SignalName.LongPressed, pressStart);
        }
    }

    /// <inheritdoc/>
    public override void _UnhandledInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventScreenTouch touch:
                OnTouch(touch);
                break;
            case InputEventScreenDrag drag:
                OnDrag(drag);
                break;
            case InputEventMagnifyGesture magnify:
                EmitSignal(SignalName.Zoomed, magnify.Factor, magnify.Position);
                break;
            case InputEventPanGesture pan:
                EmitSignal(SignalName.Panned, -pan.Delta * TrackpadPanScale);
                break;
            case InputEventMouseButton button:
                OnMouseButton(button);
                break;
            case InputEventMouseMotion motion:
                OnMouseMotion(motion);
                break;
        }
    }

    private void OnTouch(InputEventScreenTouch touch)
    {
        if (touch.Pressed)
        {
            touches[touch.Index] = touch.Position;
            if (touches.Count == 1)
            {
                BeginPress(touch.Position);
                boxing = DragSelects;
            }
            else if (touches.Count == 2)
            {
                pinchDistance = PinchDistance();
                moved = true;
                boxing = false;
            }
            return;
        }
        touches.Remove(touch.Index);
        if (touches.Count == 0)
        {
            EndPress(touch.Position);
        }
    }

    private void OnDrag(InputEventScreenDrag drag)
    {
        touches[drag.Index] = drag.Position;
        if (touches.Count >= 2)
        {
            float distance = PinchDistance();
            if (pinchDistance > 0 && distance > 0)
            {
                EmitSignal(SignalName.Zoomed, distance / pinchDistance, PinchCentre());
            }
            pinchDistance = distance;
            EmitSignal(SignalName.Panned, drag.Relative / touches.Count);
            return;
        }
        Track(drag.Position, drag.Relative);
    }

    private void OnMouseButton(InputEventMouseButton button)
    {
        // Touch screens also send emulated mouse events; the touch handlers already took them.
        if (button.Device == (int)InputEvent.DeviceIdEmulation)
        {
            return;
        }
        switch (button.ButtonIndex)
        {
            case MouseButton.WheelUp when button.Pressed:
                EmitSignal(SignalName.Zoomed, WheelZoomStep, button.Position);
                break;
            case MouseButton.WheelDown when button.Pressed:
                EmitSignal(SignalName.Zoomed, 1f / WheelZoomStep, button.Position);
                break;
            case MouseButton.Right when button.Pressed:
                if (SecondaryOnRelease)
                {
                    rightPressAt = button.Position;
                }
                else
                {
                    EmitSignal(SignalName.SecondaryTapped, button.Position);
                }
                break;
            case MouseButton.Right:
                if (rightPressAt is { } start && button.Position.DistanceTo(start) <= TapSlop)
                {
                    EmitSignal(SignalName.SecondaryTapped, button.Position);
                }
                rightPressAt = null;
                break;
            case MouseButton.Left:
                mouseLeftDown = button.Pressed;
                if (button.Pressed)
                {
                    BeginPress(button.Position);
                    boxing = true;
                }
                else
                {
                    EndPress(button.Position);
                }
                break;
        }
    }

    private void OnMouseMotion(InputEventMouseMotion motion)
    {
        if (motion.Device == (int)InputEvent.DeviceIdEmulation)
        {
            return;
        }
        if ((motion.ButtonMask & (MouseButtonMask.Middle | MouseButtonMask.Right)) != 0)
        {
            if (rightPressAt is { } start && motion.Position.DistanceTo(start) > TapSlop)
            {
                // A drag, not a click.
                rightPressAt = null;
            }
            EmitSignal(SignalName.Panned, motion.Relative);
        }
        else if (mouseLeftDown)
        {
            Track(motion.Position, motion.Relative);
        }
    }

    private void BeginPress(Vector2 position)
    {
        pressStart = position;
        pressTime = Now;
        moved = false;
        boxing = false;
        longPressFired = false;
    }

    private void Track(Vector2 position, Vector2 relative)
    {
        if (!moved && position.DistanceTo(pressStart) > TapSlop)
        {
            moved = true;
        }
        if (!moved)
        {
            return;
        }
        if (boxing)
        {
            EmitSignal(SignalName.BoxChanged, Box(position));
        }
        else
        {
            EmitSignal(SignalName.Panned, relative);
        }
    }

    private void EndPress(Vector2 position)
    {
        if (pressTime < 0)
        {
            return;
        }
        if (boxing && moved)
        {
            EmitSignal(SignalName.BoxSelected, Box(position));
        }
        else if (!moved && !longPressFired)
        {
            if (Now - lastTapTime <= DoubleTapSeconds && position.DistanceTo(lastTapPosition) <= TapSlop * 2)
            {
                EmitSignal(SignalName.DoubleTapped, position);
                lastTapTime = -10;
            }
            else
            {
                EmitSignal(SignalName.Tapped, position);
                lastTapTime = Now;
                lastTapPosition = position;
            }
        }
        pressTime = -1;
        boxing = false;
    }

    private Rect2 Box(Vector2 position) => new Rect2(pressStart, Vector2.Zero).Expand(position).Abs();

    private float PinchDistance()
    {
        using var e = touches.Values.GetEnumerator();
        e.MoveNext();
        Vector2 a = e.Current;
        e.MoveNext();
        return a.DistanceTo(e.Current);
    }

    private Vector2 PinchCentre()
    {
        Vector2 sum = Vector2.Zero;
        foreach (Vector2 p in touches.Values)
        {
            sum += p;
        }
        return sum / touches.Count;
    }
}
