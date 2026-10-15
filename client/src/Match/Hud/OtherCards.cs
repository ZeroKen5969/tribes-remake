using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;
using Tribes.Client.MatchPlay.Logic;
using Tribes.Client.Ui.Art;
using Tribes.Client.Ui.Text;
using Tribes.Core.Commands;
using Tribes.Core.Content;
using Tribes.Core.Maps;
using Tribes.Core.World;

namespace Tribes.Client.MatchPlay.Hud;

/// <summary>
/// The panels of a group of the player's units, and of what is not the player's: another
/// tribe's villager or building, a wild animal, a resource node.
/// </summary>
public static class OtherCards
{
    /// <summary>What each resource node kind (1..7) yields, as the carried-item icon.</summary>
    private static readonly int[] NodeItems = { 0, 5, 2, 1, 7, 3, 6, 4 };

    private const int MaxGroupFaces = 24;

    /// <summary>The panel of several selected units.</summary>
    public static Control Group(IMatchActions actions, IReadOnlyList<int> units)
    {
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(units);
        VBoxContainer card = HudPanels.Column(2);
        card.Name = "GroupCard";
        card.AddChild(HudWidgets.Value(UiText.F("hud.group.count", units.Count), PixelTheme.Variations.Heading));
        HFlowContainer faces = HudPanels.Flow(2);
        faces.Name = "GroupFaces";
        for (int i = 0; i < units.Count && i < MaxGroupFaces; i++)
        {
            Unit unit = actions.World.Unit(units[i]);
            int id = unit.Id;
            Button button = HudWidgets.IconButton(actions.Metrics, HudArt.Face(unit.Portrait, actions.ColourOf(unit.Tribe)),
                GameTexts.UnitName(unit), () => actions.SelectUnit(id, centre: false), name: "Member" + id.ToString(CultureInfo.InvariantCulture));
            button.Icon = HudArt.Face(unit.Portrait, actions.ColourOf(unit.Tribe));
            faces.AddChild(button);
        }
        card.AddChild(faces);
        if (!actions.Spectating)
        {
            card.AddChild(GroupOrders(actions, units));
        }
        return card;
    }

    /// <summary>The orders a group (or one unit) takes from its panel: the store's menu and showing where it is.</summary>
    public static Control GroupOrders(IMatchActions actions, IReadOnlyList<int> units)
    {
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(units);
        HudMetrics m = actions.Metrics;
        HFlowContainer row = HudPanels.Flow(m.Gap);
        row.Name = "GroupOrders";
        if (NearestStore(actions.World, actions.Tribe, units) is { } store)
        {
            row.AddChild(HudWidgets.IconButton(m, HudArt.BuildingPicture(RuleNumbers.Store), "hud.order.store",
                () => actions.OpenStore(store.Id, new GridPoint(store.X, store.Y)), name: "StoreButton"));
        }
        row.AddChild(HudWidgets.IconButton(m, HudArt.Painted(PaintedIcon.Centre), "hud.order.centre",
            () => actions.CentreOnSelection(), name: "CentreButton"));
        return row;
    }

    /// <summary>The panel of something that is not the player's, by its cell code.</summary>
    public static Control Other(IMatchActions actions, int code)
    {
        ArgumentNullException.ThrowIfNull(actions);
        GameWorld world = actions.World;
        VBoxContainer card = HudPanels.Column(2);
        card.Name = "OtherCard";
        if (CellTargets.UnitOf(world, code) is int unit)
        {
            card.AddChild(UnitCard.Build(actions, world.Unit(unit), own: false));
            card.AddChild(TribeLine(actions, world.Unit(unit).Tribe));
        }
        else if (CellTargets.AnimalOf(world, code) is int slot)
        {
            Animal animal = world.Animal(slot);
            card.AddChild(HudWidgets.Value(UiText.T("hud.other.animal"), PixelTheme.Variations.Heading));
            card.AddChild(Health(animal.HitPoints, Math.Max(animal.HitPoints, 1)));
        }
        else if (CellTargets.BuildingOf(world, code) is int building)
        {
            Building b = world.Building(building);
            card.AddChild(BuildingCard.Build(actions, b, own: false));
            card.AddChild(TribeLine(actions, b.Tribe));
        }
        else if (CellTargets.NodeOf(world, code) is int nodeSlot)
        {
            ResourceNode node = world.Map.ResourceNodes[nodeSlot];
            HBoxContainer row = HudPanels.Row(4);
            int item = node.Kind >= 0 && node.Kind < NodeItems.Length ? NodeItems[node.Kind] : 0;
            if (item > 0)
            {
                row.AddChild(HudArt.Icon(HudArt.CarriedItem(item)));
            }
            row.AddChild(HudWidgets.Value(UiText.T("hud.other.node"), PixelTheme.Variations.Heading));
            row.AddChild(HudWidgets.Value("×" + Math.Max(0, node.Amount).ToString(CultureInfo.InvariantCulture)));
            card.AddChild(row);
        }
        return card;
    }

    /// <summary>Whether the thing with cell code <paramref name="code"/> is still there to be shown.</summary>
    public static bool StillThere(GameWorld world, int code)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (CellTargets.UnitOf(world, code) is int unit)
        {
            return world.Unit(unit).HitPoints > 0;
        }
        if (CellTargets.AnimalOf(world, code) is int animal)
        {
            return world.Animal(animal).HitPoints > 0;
        }
        if (CellTargets.BuildingOf(world, code) is int building)
        {
            return world.Building(building).Stands;
        }
        if (CellTargets.NodeOf(world, code) is int node)
        {
            return world.Map.ResourceNodes[node].InUse;
        }
        return false;
    }

    /// <summary>The tribe's standing store nearest to the first of <paramref name="units"/>.</summary>
    public static Building? NearestStore(GameWorld world, int tribe, IReadOnlyList<int> units)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(units);
        if (tribe < 0 || units.Count == 0)
        {
            return null;
        }
        Unit first = world.Unit(units[0]);
        Building? best = null;
        int bestDistance = int.MaxValue;
        foreach (int id in BuildingOffers.StandingBuildings(world, tribe))
        {
            Building b = world.Building(id);
            if (b.Kind != RuleNumbers.Store)
            {
                continue;
            }
            int distance = Math.Abs(b.X - first.X) + Math.Abs(b.Y - first.Y);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = b;
            }
        }
        return best;
    }

    private static Control Health(int hp, int max)
    {
        HBoxContainer row = HudPanels.Row(3);
        var bar = new MeterBar { Fraction = max > 0 ? Math.Max(0, hp) / (float)max : 0, CustomMinimumSize = new Vector2(60, 6) };
        bar.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        row.AddChild(bar);
        row.AddChild(HudWidgets.Value(Math.Max(0, hp).ToString(CultureInfo.InvariantCulture), PixelTheme.Variations.Muted));
        return row;
    }

    private static Control TribeLine(IMatchActions actions, int tribe)
    {
        HBoxContainer row = HudPanels.Row(3);
        int colour = Math.Clamp(actions.ColourOf(tribe), 0, 7);
        row.AddChild(new ColorRect
        {
            Color = new Color(Tribes.Client.World.WorldView.Colours.Colours[colour].Swatch),
            CustomMinimumSize = new Vector2(8, 8),
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        });
        row.AddChild(HudWidgets.Value(PlayersStrip.TribeName(actions, tribe), PixelTheme.Variations.Muted));
        return row;
    }
}
