using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;
using Tribes.Client.MatchPlay.Logic;
using Tribes.Client.Ui.Art;
using Tribes.Client.Ui.Text;
using Tribes.Core.Commands;
using Tribes.Core.Content.Rules;
using Tribes.Core.World;

namespace Tribes.Client.MatchPlay.Hud;

/// <summary>
/// The build menu: the building kinds the mission offers now, in the original's order, each with
/// its picture, name, cost and description. Picking one starts placing its ghost on the map.
/// </summary>
public partial class BuildMenu : HudPopup
{
    /// <summary>The war mission, in which the original offers no build menu.</summary>
    public const int WarMission = 3;

    private readonly IMatchActions actions;

    /// <summary>The build menu of the match <paramref name="actions"/> plays.</summary>
    public BuildMenu(IMatchActions actions)
        : base("s.21", actions?.Metrics ?? throw new ArgumentNullException(nameof(actions)))
    {
        this.actions = actions;
        Name = "BuildMenu";
        MaxWidth = 420;
    }

    /// <summary>Whether the mission offers building at all.</summary>
    public static bool Offered(GameWorld world) => world is not null && world.Mission != WarMission;

    /// <inheritdoc/>
    protected override void Fill()
    {
        GameWorld world = actions.World;
        IReadOnlyList<BuildingKind> kinds = world.Content.Rules.Buildings.Kinds;
        TribeState stock = world.Tribes[Math.Max(0, actions.Tribe)];
        foreach (int kind in OrderMenus.BuildableKinds(world))
        {
            int chosen = kind;
            var button = new Button
            {
                Name = "Build" + kind.ToString(CultureInfo.InvariantCulture),
                ThemeTypeVariation = PixelTheme.Variations.Tool,
                FocusMode = FocusModeEnum.None,
                CustomMinimumSize = new Vector2(0, Math.Max(44, Metrics.Button)),
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
            };
            button.Pressed += () =>
            {
                Close();
                actions.BeginPlacement(chosen);
            };
            button.GuiInput += input => DragOut(input, chosen, button);
            HBoxContainer row = HudPanels.Row(6);
            row.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            row.OffsetLeft = 4;
            row.OffsetRight = -4;
            row.OffsetTop = 3;
            row.OffsetBottom = -3;
            TextureRect picture = HudArt.Icon(HudArt.BuildingPicture(kind));
            picture.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
            picture.CustomMinimumSize = new Vector2(44, 40);
            row.AddChild(picture);
            VBoxContainer text = HudPanels.Column(0);
            text.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            text.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            text.AddChild(HudWidgets.Value(GameTexts.Building(kind), PixelTheme.Variations.Heading));
            Label description = HudWidgets.Value(GameTexts.BuildingDescription(kind), PixelTheme.Variations.Muted);
            description.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            description.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            text.AddChild(description);
            row.AddChild(text);
            row.AddChild(Cost(kinds[kind].BuildCost, stock));
            button.AddChild(row);
            Content.AddChild(button);
        }
    }

    private bool draggedOut, lifted;

    /// <summary>
    /// With a finger, a building is dragged out of the menu onto the map: once the finger pressed
    /// on its entry leaves the menu, the menu closes and the ghost comes out under the finger
    /// (a drag inside the menu scrolls the list as usual).
    /// </summary>
    private void DragOut(InputEvent input, int kind, Control entry)
    {
        if (draggedOut && input is InputEventScreenTouch { Pressed: false })
        {
            // Lifted before the menu closed (a quick flick): the ghost stays where the finger left it.
            lifted = true;
            return;
        }
        if (draggedOut || !Metrics.Touch || input is not InputEventScreenDrag drag)
        {
            return;
        }
        Vector2 at = entry.GetGlobalTransformWithCanvas() * drag.Position;
        if (GetGlobalRect().HasPoint(at))
        {
            return;
        }
        draggedOut = true;
        int finger = drag.Index;
        // Not while the entry is handling the event: the menu leaves the screen right after it.
        Callable.From(() =>
        {
            Close();
            actions.DragPlacement(kind, lifted ? -1 : finger, at);
        }).CallDeferred();
    }

    /// <summary>The cost icons and amounts; an amount the tribe lacks now is dimmed.</summary>
    public static Control Cost(IReadOnlyList<int> cost, TribeState stock)
    {
        ArgumentNullException.ThrowIfNull(cost);
        ArgumentNullException.ThrowIfNull(stock);
        VBoxContainer box = HudPanels.Column(0);
        TribeCounter[] counters = { TribeCounter.Wood, TribeCounter.Stones, TribeCounter.Bones };
        for (int i = 0; i < cost.Count && i < counters.Length; i++)
        {
            if (cost[i] <= 0)
            {
                continue;
            }
            HBoxContainer line = HudPanels.Row(2);
            line.AddChild(HudArt.Icon(HudArt.Cost(i)));
            Label amount = HudWidgets.Value(cost[i].ToString(CultureInfo.InvariantCulture));
            if (stock[counters[i]] < cost[i])
            {
                amount.Modulate = new Color(1, 1, 1, 0.5f);
            }
            line.AddChild(amount);
            box.AddChild(line);
        }
        return box;
    }
}

/// <summary>
/// The army calls of the original's game menu: gather the fighters at a point (form up), call
/// everyone to arms at a point, and dismiss either call. A call needs a point picked on the map.
/// </summary>
public partial class ArmyMenu : HudPopup
{
    private const int RallyEntry = 11, AllOutEntry = 12, DismissEntry = 14, BackToWorkEntry = 15;
    private readonly IMatchActions actions;

    /// <summary>The army menu of the match <paramref name="actions"/> plays.</summary>
    public ArmyMenu(IMatchActions actions)
        : base("hud.army", actions?.Metrics ?? throw new ArgumentNullException(nameof(actions)))
    {
        this.actions = actions;
        Name = "ArmyMenu";
        MaxWidth = 280;
    }

    /// <inheritdoc/>
    protected override void Fill()
    {
        // Each tribe calls its own army: the menu offers what this tribe's call allows.
        ArmyOrders army = TribeRoles.Army(actions.World, actions.Tribe >= 0 ? actions.Tribe : actions.ViewTribe);
        HudMetrics m = Metrics;
        if (!army.AllOut && !army.Rally)
        {
            Content.AddChild(Entry(m, RallyEntry, "RallyButton", () => actions.BeginArmyPoint(ArmyCall.Rally)));
        }
        if (!army.AllOut)
        {
            Content.AddChild(Entry(m, AllOutEntry, "AllOutButton", () => actions.BeginArmyPoint(ArmyCall.AllOut)));
        }
        if (army.Rally && !army.AllOut)
        {
            Content.AddChild(Entry(m, DismissEntry, "DismissButton", () => actions.Send(new CancelArmyCall(actions.Tribe, ArmyCall.Rally))));
        }
        if (army.AllOut)
        {
            Content.AddChild(Entry(m, BackToWorkEntry, "BackToWorkButton", () => actions.Send(new CancelArmyCall(actions.Tribe, ArmyCall.AllOut))));
        }
        Content.AddChild(HudWidgets.Text("hud.army.help", PixelTheme.Variations.Muted));
    }

    private Button Entry(HudMetrics m, int entry, string name, Action action) =>
        HudWidgets.TextButton(m, HudArt.Effect(entry), GameTexts.Entry(entry), () =>
        {
            Close();
            action();
        }, name);
}

/// <summary>
/// The roster (the original's villager list): every villager of the tribe, adults first, with
/// what they do. Picking one shows it (or calls it out of its building); when the chief died,
/// an adult can be named the new chief here. The game runs on while it is open (as the
/// original's list); the rules are told it is open (a tutorial step waits for that).
/// </summary>
public partial class RosterPanel : HudPopup
{
    private readonly IMatchActions actions;

    /// <summary>The roster of the match <paramref name="actions"/> plays.</summary>
    public RosterPanel(IMatchActions actions)
        : base("s.24", actions?.Metrics ?? throw new ArgumentNullException(nameof(actions)), hold: null)
    {
        this.actions = actions;
        Name = "RosterPanel";
        MaxWidth = 380;
    }

    /// <summary>Whether the roster is open to name a new chief.</summary>
    public bool ChoosingChief => TribeRoles.AwaitsNewChief(actions.World, actions.Tribe);

    /// <inheritdoc/>
    protected override void Fill()
    {
        GameWorld world = actions.World;
        bool chief = ChoosingChief;
        if (chief)
        {
            Content.AddChild(HudWidgets.Value(UiText.S(41), PixelTheme.Variations.Heading));
        }
        var units = new List<int>(BuildingOffers.LivingUnits(world, actions.Tribe));
        units.Sort((a, b) => (world.Unit(b).Age >= RuleNumbers.AdultAge).CompareTo(world.Unit(a).Age >= RuleNumbers.AdultAge));
        foreach (int id in units)
        {
            Unit unit = world.Unit(id);
            if (chief && unit.Age < RuleNumbers.AdultAge)
            {
                continue;
            }
            Content.AddChild(Row(world, unit, chief));
        }
    }

    private Control Row(GameWorld world, Unit unit, bool chief)
    {
        HudMetrics m = Metrics;
        HBoxContainer row = HudPanels.Row(4);
        row.Name = "Unit" + unit.Id.ToString(CultureInfo.InvariantCulture);
        row.AddChild(HudArt.Icon(HudArt.Face(unit.Portrait, actions.ColourOf(unit.Tribe))));
        VBoxContainer text = HudPanels.Column(0);
        text.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        text.AddChild(HudWidgets.Value(GameTexts.UnitName(unit) + "  " + UiText.F("hud.unit.age", unit.Age)));
        int building = Inside(world, unit);
        string where = building > 0 ? UiText.F("hud.roster.inside", GameTexts.Building(world.Building(building).Kind)) : UiText.T("hud.roster.outside");
        text.AddChild(HudWidgets.Value(where, PixelTheme.Variations.Muted));
        row.AddChild(text);
        int id = unit.Id;
        if (chief)
        {
            row.AddChild(HudWidgets.IconButton(m, HudArt.Painted(PaintedIcon.Chief), "hud.roster.appoint", () =>
            {
                actions.Send(new AppointChief(actions.Tribe, id));
                Close();
            }, name: "Appoint" + id.ToString(CultureInfo.InvariantCulture)));
        }
        else if (building > 0)
        {
            row.AddChild(HudWidgets.IconButton(m, HudArt.Painted(PaintedIcon.ComeOut), "hud.roster.call_out", () =>
            {
                actions.Send(new CallOutUnit(actions.Tribe, id));
                Close();
            }, name: "CallOut" + id.ToString(CultureInfo.InvariantCulture)));
        }
        else
        {
            row.AddChild(HudWidgets.IconButton(m, HudArt.Painted(PaintedIcon.Centre), "hud.roster.show", () =>
            {
                Close();
                actions.SelectUnit(id, centre: true);
            }, name: "Show" + id.ToString(CultureInfo.InvariantCulture)));
        }
        return row;
    }

    /// <summary>The building <paramref name="unit"/> is inside, or 0.</summary>
    public static int Inside(GameWorld world, Unit unit) => BuildingOffers.BuildingInside(world, unit);
}
