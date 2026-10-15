using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;
using Tribes.Client.MatchPlay.Logic;
using Tribes.Client.Ui.Art;
using Tribes.Client.Ui.Text;
using Tribes.Core.Commands;
using Tribes.Core.Content.Rules;
using Tribes.Core.World;

namespace Tribes.Client.MatchPlay.Hud;

/// <summary>
/// The panel of a building: what it is, its level and durability, what it is doing (normal work,
/// upgrade, repair, a site being built), who is inside, and its orders: upgrade, repair, stop,
/// demolish, send someone out, the armoury's weapons and traps, the sanctuary's spells and the
/// store's stock.
/// </summary>
public static class BuildingCard
{
    private const int UpgradeEntry = 16, DemolishEntry = 17, EvictEntry = 18, FirstSpellEntry = 19;
    private const int RepairEntry = 26, StopUpgradeEntry = 27, StopRepairEntry = 28;
    private const int DemolishQuestion = 36, NothingToDo = 37, PickAVillager = 34;

    /// <summary>A number that changes when anything the card shows changes.</summary>
    public static int Signature(GameWorld world, Building b)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(b);
        var hash = new HashCode();
        hash.Add(b.Id);
        hash.Add(b.Kind);
        hash.Add(b.Level);
        hash.Add(b.HitPoints);
        hash.Add(b.MaxHitPoints);
        hash.Add(b.WorkMode);
        hash.Add(world.UnlockLevel(Math.Clamp(b.Kind, 0, GameWorld.UnlockableKinds - 1)));
        foreach (int unit in BuildingOffers.Inside(world, b))
        {
            hash.Add(unit);
        }
        foreach (ArmouryLine line in BuildingOffers.Armoury(world, b))
        {
            hash.Add(line.Pending);
        }
        if (b.Kind == RuleNumbers.Store)
        {
            TribeState stock = world.Tribes[b.Tribe];
            for (int c = 0; c <= (int)TribeCounter.Tomahawks; c++)
            {
                hash.Add(stock.Counter(c));
            }
        }
        hash.Add(world.Tribes[b.Tribe][TribeCounter.Chief]);
        return hash.ToHashCode();
    }

    /// <summary>The card of building <paramref name="b"/>; <paramref name="own"/> adds its orders.</summary>
    public static Control Build(IMatchActions actions, Building b, bool own)
    {
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(b);
        VBoxContainer card = HudPanels.Column(2);
        card.Name = "BuildingCard";
        card.AddChild(Header(actions.World, b));
        bool orders = own && !actions.Spectating;
        Control? occupants = Occupants(actions, b, orders);
        if (occupants is not null)
        {
            card.AddChild(occupants);
        }
        if (orders)
        {
            AddKindPart(actions, card, b);
            card.AddChild(WorkOrders(actions, b));
        }
        return card;
    }

    private static Control Header(GameWorld world, Building b)
    {
        HBoxContainer row = HudPanels.Row(4);
        int shownKind = RuleNumbers.ShownKind(b.Kind, b.Aux);
        if (HudArt.HasBuildingPicture(shownKind))
        {
            TextureRect picture = HudArt.Icon(HudArt.BuildingPicture(shownKind));
            picture.CustomMinimumSize = new Vector2(Math.Min(48, picture.CustomMinimumSize.X), Math.Min(40, picture.CustomMinimumSize.Y));
            picture.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
            row.AddChild(HudPanels.Framed(picture));
        }
        VBoxContainer text = HudPanels.Column(1);
        text.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        string name = GameTexts.Building(shownKind);
        if (RuleNumbers.IsSite(b.Kind) && shownKind != b.Kind)
        {
            name = UiText.F("hud.building.site_of", name);
        }
        else if (RuleNumbers.IsPlanned(b.Kind))
        {
            name += "  " + UiText.F("hud.building.level", b.Level);
        }
        Label title = HudWidgets.Value(name, PixelTheme.Variations.Heading);
        title.Name = "BuildingName";
        title.ClipText = true;
        text.AddChild(title);
        HBoxContainer hp = HudPanels.Row(3);
        int max = RuleNumbers.IsSite(b.Kind) ? SiteGoal(world, b) : b.MaxHitPoints;
        var bar = new MeterBar { Fraction = max > 0 ? Math.Max(0, b.HitPoints) / (float)max : 0, CustomMinimumSize = new Vector2(40, 6) };
        bar.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        bar.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        hp.AddChild(bar);
        hp.AddChild(HudWidgets.Value($"{Math.Max(0, b.HitPoints)}/{max}", PixelTheme.Variations.Muted));
        text.AddChild(hp);
        text.AddChild(HudWidgets.Value(UiText.T(StateKey(b)), PixelTheme.Variations.Muted));
        row.AddChild(text);
        return row;
    }

    private static string StateKey(Building b) =>
        RuleNumbers.IsSite(b.Kind) ? "hud.building.state.site"
        : b.WorkMode switch
        {
            BuildingWorkMode.Upgrading => "hud.building.state.upgrading",
            BuildingWorkMode.Repairing => "hud.building.state.repairing",
            _ => b.HitPoints < b.MaxHitPoints ? "hud.building.state.damaged" : "hud.building.state.normal",
        };

    /// <summary>The hit points at which a site is finished: its building's level-1 durability.</summary>
    private static int SiteGoal(GameWorld world, Building site)
    {
        IReadOnlyList<BuildingKind> kinds = world.Content.Rules.Buildings.Kinds;
        int target = Math.Clamp(site.Aux, 0, kinds.Count - 1);
        return kinds[target].MaxHitPoints.Count > 0 ? kinds[target].MaxHitPoints[0] : Math.Max(1, site.MaxHitPoints);
    }

    private static Control? Occupants(IMatchActions actions, Building b, bool orders)
    {
        GameWorld world = actions.World;
        IReadOnlyList<int> inside = BuildingOffers.Inside(world, b);
        IReadOnlyList<int> evictable = orders ? BuildingOffers.Evictable(world, b) : Array.Empty<int>();
        var shown = new List<int>(evictable);
        foreach (int unit in inside)
        {
            if (!shown.Contains(unit))
            {
                shown.Add(unit);
            }
        }
        if (shown.Count == 0)
        {
            return null;
        }
        VBoxContainer part = HudPanels.Column(1);
        part.AddChild(HudWidgets.Value(UiText.F("hud.building.inside", shown.Count), PixelTheme.Variations.Muted));
        HFlowContainer faces = HudPanels.Flow(2);
        faces.Name = "Occupants";
        foreach (int id in shown)
        {
            Unit unit = world.Unit(id);
            Texture2D face = HudArt.Face(unit.Portrait, actions.ColourOf(unit.Tribe));
            if (evictable.Contains(id))
            {
                int unitId = id;
                Button button = HudWidgets.IconButton(actions.Metrics, face, GameTexts.UnitName(unit) + " — " + GameTexts.Entry(EvictEntry),
                    () => actions.Send(new EvictUnit(actions.Tribe, b.Id, unitId)), name: "Evict" + id.ToString(CultureInfo.InvariantCulture));
                button.Icon = face;
                faces.AddChild(button);
            }
            else
            {
                TextureRect picture = HudArt.Icon(face);
                picture.TooltipText = GameTexts.UnitName(unit);
                picture.MouseFilter = Control.MouseFilterEnum.Pass;
                faces.AddChild(picture);
            }
        }
        part.AddChild(faces);
        return part;
    }

    private static void AddKindPart(IMatchActions actions, VBoxContainer card, Building b)
    {
        switch (b.Kind)
        {
            case RuleNumbers.Armoury:
                card.AddChild(ArmouryPart(actions, b));
                break;
            case RuleNumbers.Sanctuary:
                card.AddChild(SanctuaryPart(actions, b));
                break;
            case RuleNumbers.Store:
                card.AddChild(StorePanel.Stock(actions.World, b.Tribe));
                card.AddChild(HudWidgets.Text(GameTexts.T(PickAVillager), PixelTheme.Variations.Muted, translate: false));
                break;
            case RuleNumbers.ChiefsHut:
                int chief = actions.World.Tribes[b.Tribe][TribeCounter.Chief];
                if (!BuildingOffers.Inside(actions.World, b).Contains(chief))
                {
                    card.AddChild(HudWidgets.Text(GameTexts.T(NothingToDo), PixelTheme.Variations.Muted, translate: false));
                }
                break;
        }
    }

    private static Control ArmouryPart(IMatchActions actions, Building armoury)
    {
        HudMetrics m = actions.Metrics;
        VBoxContainer part = HudPanels.Column(1);
        part.Name = "Armoury";
        foreach (ArmouryLine line in BuildingOffers.Armoury(actions.World, armoury))
        {
            HBoxContainer row = HudPanels.Row(3);
            row.AddChild(HudArt.Icon(HudArt.BarItem(line.Product)));
            Label name = HudWidgets.Value(GameTexts.Product(line.Product));
            name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            name.ClipText = true;
            row.AddChild(name);
            int product = line.Product;
            string id = product.ToString(CultureInfo.InvariantCulture);
            if (line.IsTrap)
            {
                if (line.Pending > 0)
                {
                    row.AddChild(HudWidgets.Value("×" + line.Pending.ToString(CultureInfo.InvariantCulture), PixelTheme.Variations.Muted));
                }
                int trap = line.TrapKind;
                row.AddChild(HudWidgets.IconButton(m, HudArt.Painted(PaintedIcon.Plus), "hud.armoury.set_trap",
                    () => actions.BeginTrap(armoury.Id, trap), name: "Trap" + id));
            }
            else
            {
                int count = line.Pending;
                row.AddChild(HudWidgets.IconButton(m, HudArt.Painted(PaintedIcon.Minus), "hud.armoury.less",
                    () => actions.Send(new SetArmouryOrder(actions.Tribe, armoury.Id, product, Math.Max(0, count - 1))), name: "Less" + id));
                Label value = HudWidgets.Value(count.ToString(CultureInfo.InvariantCulture));
                value.CustomMinimumSize = new Vector2(12, 0);
                value.HorizontalAlignment = HorizontalAlignment.Center;
                row.AddChild(value);
                row.AddChild(HudWidgets.IconButton(m, HudArt.Painted(PaintedIcon.Plus), "hud.armoury.more",
                    () => actions.Send(new SetArmouryOrder(actions.Tribe, armoury.Id, product, Math.Min(OrderMenus.MaxArmouryCount, count + 1))),
                    name: "More" + id));
            }
            part.AddChild(row);
        }
        return part;
    }

    private static Control SanctuaryPart(IMatchActions actions, Building sanctuary)
    {
        HFlowContainer part = HudPanels.Flow(actions.Metrics.Gap);
        part.Name = "Spells";
        foreach (int spell in BuildingOffers.Spells(actions.World, sanctuary))
        {
            int chosen = spell;
            part.AddChild(HudWidgets.IconButton(actions.Metrics, HudArt.Effect(FirstSpellEntry + spell), GameTexts.Spell(spell),
                () => actions.BeginSpell(sanctuary.Id, chosen), caption: actions.Metrics.Touch,
                name: "Spell" + spell.ToString(CultureInfo.InvariantCulture)));
        }
        return part;
    }

    private static Control WorkOrders(IMatchActions actions, Building b)
    {
        HudMetrics m = actions.Metrics;
        HFlowContainer row = HudPanels.Flow(m.Gap);
        row.Name = "BuildingOrders";
        int tribe = actions.Tribe;
        BuildingKind? kind = b.Kind >= 0 && b.Kind < actions.World.Content.Rules.Buildings.Kinds.Count
            ? actions.World.Content.Rules.Buildings.Kinds[b.Kind] : null;
        foreach (BuildingWork work in BuildingOffers.Work(actions.World, b))
        {
            (int entry, string name) = work switch
            {
                BuildingWork.Upgrade => (UpgradeEntry, "UpgradeButton"),
                BuildingWork.Repair => (RepairEntry, "RepairButton"),
                BuildingWork.StopUpgrade => (StopUpgradeEntry, "StopUpgradeButton"),
                _ => (StopRepairEntry, "StopRepairButton"),
            };
            BuildingWork chosen = work;
            Button button = HudWidgets.IconButton(m, HudArt.Effect(entry), GameTexts.Entry(entry),
                () => actions.Send(new OrderBuildingWork(tribe, b.Id, chosen)), caption: m.Touch, name: name);
            if (kind is not null && work is BuildingWork.Upgrade or BuildingWork.Repair)
            {
                button.TooltipText += "  " + CostText(work == BuildingWork.Upgrade ? kind.UpgradeCost : RepairCost(kind, b));
            }
            row.AddChild(button);
        }
        if (BuildingOffers.CanDemolish(tribe, b))
        {
            row.AddChild(HudWidgets.IconButton(m, HudArt.Effect(DemolishEntry), GameTexts.Entry(DemolishEntry),
                () => Tribes.Client.Ui.Widgets.ModalDialog.Show(actions.DialogLayer, GameTexts.T(DemolishQuestion),
                    (Tribes.Client.Ui.UiKeys.Yes, () => actions.Send(new DemolishBuilding(tribe, b.Id))), (Tribes.Client.Ui.UiKeys.No, null)),
                caption: m.Touch, name: "DemolishButton"));
        }
        return row;
    }

    private static IReadOnlyList<int> RepairCost(BuildingKind kind, Building b)
    {
        var cost = new int[kind.BuildCost.Count];
        int missing = Math.Max(0, b.MaxHitPoints - b.HitPoints);
        for (int i = 0; i < cost.Length; i++)
        {
            cost[i] = b.MaxHitPoints > 0 ? kind.BuildCost[i] * missing / (2 * b.MaxHitPoints) : 0;
        }
        return cost;
    }

    /// <summary>"wood 4, stones 2" style cost text.</summary>
    public static string CostText(IReadOnlyList<int> cost)
    {
        ArgumentNullException.ThrowIfNull(cost);
        string[] names = { "hud.top.wood", "hud.top.stones", "hud.top.bones" };
        var parts = new List<string>();
        for (int i = 0; i < cost.Count && i < names.Length; i++)
        {
            if (cost[i] > 0)
            {
                parts.Add(UiText.T(names[i]) + " " + cost[i].ToString(CultureInfo.InvariantCulture));
            }
        }
        return string.Join(", ", parts);
    }
}
