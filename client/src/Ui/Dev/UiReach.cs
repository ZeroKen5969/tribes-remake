using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;
using Tribes.Client.App;
using Tribes.Client.Ui.Logic;

namespace Tribes.Client.Ui.Dev;

/// <summary>
/// Development check that every button shown can be pressed: enough of it lies on the screen,
/// inside the room kept from the edges (<see cref="GameApp.SafeMargins"/>), either as it is or
/// once its list is scrolled; and a press in the middle of that part reaches it, not a control
/// over it. Buttons under an open panel, question or story scene are covered on purpose and only
/// counted. Used by "--ui-reach" (menus) and the match script's "expect reachable".
/// </summary>
public static class UiReach
{
    /// <summary>The share of a button's width and height that must be reachable (at most <see cref="EnoughPixels"/>).</summary>
    private const float EnoughShare = 0.8f;

    /// <summary>Reachable pixels along an axis that are always enough (a part of a wide button).</summary>
    private const float EnoughPixels = 32;

    /// <summary>What was found: the buttons checked, those covered by a panel on purpose, and the problems.</summary>
    /// <param name="Checked">Buttons checked.</param>
    /// <param name="UnderPanels">Buttons covered by an open panel, question or story scene.</param>
    /// <param name="Problems">One line per button that cannot be pressed.</param>
    public sealed record Report(int Checked, int UnderPanels, IReadOnlyList<string> Problems);

    /// <summary>The room on the screen buttons must stay in: the visible screen less the safe margins.</summary>
    public static Rect2 SafeScreen(Viewport viewport)
    {
        ArgumentNullException.ThrowIfNull(viewport);
        Vector2 size = viewport.GetVisibleRect().Size;
        EdgeMargins m = GameApp.Instance.SafeMargins;
        return new Rect2(m.Left, m.Top, size.X - m.Left - m.Right, size.Y - m.Top - m.Bottom);
    }

    /// <summary>Checks every button shown under <paramref name="root"/> against <paramref name="screen"/>.</summary>
    public static Report Check(Node root, Rect2 screen)
    {
        ArgumentNullException.ThrowIfNull(root);
        var problems = new List<string>();
        var controls = new List<(Control Control, int Layer)>();
        Collect(root, 0, controls);
        int checkedCount = 0, underPanels = 0;
        foreach ((Control control, _) in controls)
        {
            if (control is not BaseButton button)
            {
                continue;
            }
            checkedCount++;
            Rect2 rect = button.GetGlobalRect();
            Rect2 reach = Reachable(button, rect, scrolled: true).Intersection(screen);
            if (!Enough(reach.Size.X, rect.Size.X) || !Enough(reach.Size.Y, rect.Size.Y))
            {
                problems.Add($"{Name(button)} rect={Text(rect)} reachable={Text(reach)}: off the screen or its edge room");
                continue;
            }
            Rect2 shown = Reachable(button, rect, scrolled: false).Intersection(screen);
            if (!Enough(shown.Size.X, rect.Size.X) || !Enough(shown.Size.Y, rect.Size.Y))
            {
                // Out of its list's view now: scrolling brings it there, where nothing else lies over the list.
                continue;
            }
            // The middle and four points halfway to the corners: the middle and at least three of
            // the others must reach the button (a button half under a bar is hard to hit).
            Control? missedBy = null;
            Vector2 missedAt = Vector2.Zero;
            int missed = 0;
            bool middleMissed = false, underPanel = false;
            Vector2[] probes = Probes(shown);
            for (int i = 0; i < probes.Length; i++)
            {
                Control? top = TopAt(controls, probes[i]);
                Control? taker = top is null ? null : Taker(top, button);
                if (taker == button)
                {
                    continue;
                }
                if (taker is not null && Panel(taker) is { } panel && !IsInside(button, panel))
                {
                    underPanel = true;
                    break;
                }
                if (missed++ == 0)
                {
                    missedBy = taker;
                    missedAt = probes[i];
                }
                middleMissed |= i == 0;
            }
            if (underPanel)
            {
                underPanels++;
                continue;
            }
            if (middleMissed || missed > 1)
            {
                problems.Add($"{Name(button)} rect={Text(rect)}: {missed} of 5 presses miss it, one at {Text(missedAt)} goes to {(missedBy is null ? "nothing" : Name(missedBy))}");
            }
        }
        return new Report(checkedCount, underPanels, problems);
    }

    /// <summary>The middle of <paramref name="r"/> first, then the points halfway from it to the corners.</summary>
    private static Vector2[] Probes(Rect2 r)
    {
        Vector2 c = r.GetCenter(), q = r.Size / 4;
        return [c, c - q, c + q, c + new Vector2(q.X, -q.Y), c + new Vector2(-q.X, q.Y)];
    }

    /// <summary>Prints the report as UI_REACH lines; returns whether every button can be pressed.</summary>
    public static bool Print(Report report, string where)
    {
        ArgumentNullException.ThrowIfNull(report);
        GD.Print($"UI_REACH {where} buttons={report.Checked} underPanels={report.UnderPanels} problems={report.Problems.Count}");
        foreach (string problem in report.Problems)
        {
            GD.Print($"UI_REACH_PROBLEM {where} {problem}");
        }
        return report.Problems.Count == 0;
    }

    private static bool Enough(float reachable, float size) => reachable >= Math.Min(size * EnoughShare, EnoughPixels) - 0.01f;

    /// <summary>
    /// The part of <paramref name="rect"/> in view: now, or (<paramref name="scrolled"/>) once its
    /// lists are scrolled, which along an axis a list scrolls moves the button into the list's
    /// view; every list and clipping control cuts off what lies outside it.
    /// </summary>
    private static Rect2 Reachable(Control control, Rect2 rect, bool scrolled)
    {
        Rect2 reach = rect;
        for (Node? parent = control.GetParent(); parent is not null; parent = parent.GetParent())
        {
            if (parent is ScrollContainer scroll)
            {
                Rect2 view = scroll.GetGlobalRect();
                float x = !scrolled || scroll.HorizontalScrollMode == ScrollContainer.ScrollMode.Disabled ? reach.Position.X : Into(reach.Position.X, reach.Size.X, view.Position.X, view.Size.X);
                float y = !scrolled || scroll.VerticalScrollMode == ScrollContainer.ScrollMode.Disabled ? reach.Position.Y : Into(reach.Position.Y, reach.Size.Y, view.Position.Y, view.Size.Y);
                reach = new Rect2(x, y, reach.Size).Intersection(view);
            }
            else if (parent is Control { ClipContents: true } clip)
            {
                reach = reach.Intersection(clip.GetGlobalRect());
            }
        }
        return reach;
    }

    private static float Into(float start, float size, float viewStart, float viewSize) =>
        size >= viewSize ? viewStart : Math.Clamp(start, viewStart, viewStart + viewSize - size);

    /// <summary>The controls shown, in drawing order, with the canvas layer each is drawn on.</summary>
    private static void Collect(Node node, int layer, List<(Control, int)> controls)
    {
        foreach (Node child in node.GetChildren())
        {
            int childLayer = layer;
            switch (child)
            {
                case Viewport:
                    // Other windows and off-screen views (thumbnails) take no presses on this screen.
                    continue;
                case CanvasLayer canvas:
                    if (!canvas.Visible)
                    {
                        continue;
                    }
                    childLayer = canvas.Layer;
                    break;
                case CanvasItem { Visible: false }:
                    continue;
            }
            if (child is Control control)
            {
                controls.Add((control, childLayer));
            }
            Collect(child, childLayer, controls);
        }
    }

    /// <summary>
    /// The control a press at <paramref name="point"/> goes to, the way Godot picks it: the highest
    /// canvas layer first, then the control drawn last; controls that ignore the mouse are skipped,
    /// and a clipping control hides the part of its children outside it.
    /// </summary>
    private static Control? TopAt(List<(Control Control, int Layer)> controls, Vector2 point)
    {
        Control? best = null;
        int bestLayer = int.MinValue;
        foreach ((Control control, int layer) in controls)
        {
            if (control.MouseFilter == Control.MouseFilterEnum.Ignore || layer < bestLayer || !control.GetGlobalRect().HasPoint(point) || Clipped(control, point))
            {
                continue;
            }
            best = control;
            bestLayer = layer;
        }
        return best;
    }

    private static bool Clipped(Control control, Vector2 point)
    {
        for (Node? parent = control.GetParent(); parent is Control c; parent = c.GetParent())
        {
            if (c.ClipContents && !c.GetGlobalRect().HasPoint(point))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Who takes a press that lands on <paramref name="top"/>: it goes up through the controls that
    /// pass presses on until <paramref name="button"/> or one that keeps them.
    /// </summary>
    private static Control Taker(Control top, BaseButton button)
    {
        for (Node? node = top; node is Control control; node = control.GetParent())
        {
            if (control == button || control.MouseFilter == Control.MouseFilterEnum.Stop)
            {
                return control;
            }
        }
        return top;
    }

    /// <summary>The open panel, question or story scene <paramref name="control"/> belongs to, if any.</summary>
    private static Control? Panel(Control control)
    {
        for (Node? node = control; node is not null; node = node.GetParent())
        {
            if (node is MatchPlay.Hud.HudPopup or Widgets.ModalDialog or MatchPlay.Hud.SceneDialog)
            {
                return (Control)node;
            }
        }
        return null;
    }

    private static bool IsInside(Node node, Node ancestor)
    {
        for (Node? n = node; n is not null; n = n.GetParent())
        {
            if (n == ancestor)
            {
                return true;
            }
        }
        return false;
    }

    private static string Name(Control control)
    {
        string text = control is Button { Text.Length: > 0 } b ? $" '{b.Text}'" : "";
        return $"{control.GetParent()?.Name}/{control.Name}{text}";
    }

    private static string Text(Rect2 r) => string.Create(CultureInfo.InvariantCulture, $"{r.Position.X:0},{r.Position.Y:0} {r.Size.X:0}x{r.Size.Y:0}");

    private static string Text(Vector2 p) => string.Create(CultureInfo.InvariantCulture, $"{p.X:0},{p.Y:0}");
}
