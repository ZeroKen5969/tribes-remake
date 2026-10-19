using Godot;

namespace Tribes.Client.Ui;

/// <summary>
/// On touch screens a finger dragged over a list scrolls it, also when the drag starts on a
/// button or a framed row of the list. Godot's lists scroll only from presses that reach them,
/// and buttons and panels keep presses to themselves, so inside a list they let presses through
/// (they still take them); once the list starts scrolling, its buttons let go, so the drag does
/// not press the one it started on. Sliders, text fields and the scroll bars keep their presses.
/// Installed once for the whole game; nothing changes on screens without touch.
/// </summary>
public static class TouchScrolling
{
    private static readonly StringName Watched = "touch_scrolling";
    private static bool installed;

    /// <summary>
    /// How far, in logical pixels, a finger moves before a list scrolls: less is a tap that
    /// shook a little (Godot's default of 0 would turn every such tap into a scroll).
    /// </summary>
    public const int ScrollSlop = 10;

    /// <summary>Whether lists scroll by touch here (Godot drags lists only on touch screens).</summary>
    public static bool Enabled => DisplayServer.IsTouchscreenAvailable();

    /// <summary>Watches every control the game adds to <paramref name="tree"/> (once, on touch screens).</summary>
    public static void Install(SceneTree tree)
    {
        if (installed || !Enabled)
        {
            return;
        }
        installed = true;
        tree.NodeAdded += OnNodeAdded;
    }

    private static void OnNodeAdded(Node node)
    {
        switch (node)
        {
            case ScrollContainer scroll:
                if (scroll.ScrollDeadzone < ScrollSlop)
                {
                    scroll.ScrollDeadzone = ScrollSlop;
                }
                // Once per list (a list moved elsewhere enters the tree again).
                if (!scroll.HasMeta(Watched))
                {
                    scroll.SetMeta(Watched, true);
                    scroll.ScrollStarted += () => LetGo(scroll);
                }
                break;
            case BaseButton or PanelContainer or Panel when ((Control)node).MouseFilter == Control.MouseFilterEnum.Stop && InList(node):
                ((Control)node).MouseFilter = Control.MouseFilterEnum.Pass;
                break;
        }
    }

    /// <summary>Whether <paramref name="node"/> is in a list's content (not one of its scroll bars).</summary>
    private static bool InList(Node node)
    {
        for (Node? parent = node.GetParent(); parent is not null; parent = parent.GetParent())
        {
            if (parent is ScrollContainer)
            {
                return true;
            }
            if (parent is ScrollBar or Window)
            {
                return false;
            }
        }
        return false;
    }

    /// <summary>Ends the press of every button in <paramref name="scroll"/> (turning a button off and on drops its press).</summary>
    private static void LetGo(Node scroll)
    {
        foreach (Node child in scroll.GetChildren())
        {
            if (child is BaseButton { Disabled: false } button)
            {
                button.Disabled = true;
                button.Disabled = false;
            }
            LetGo(child);
        }
    }
}
