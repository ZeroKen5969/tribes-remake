using System;
using System.Globalization;
using Godot;
using Tribes.Client.MatchPlay.Logic;
using Tribes.Client.Ui.Art;
using Tribes.Core.Commands;
using Tribes.Core.World;

namespace Tribes.Client.MatchPlay.Hud;

/// <summary>
/// The panel of one villager: face, name, sex and age, hit points, hunger, tiredness and mood,
/// what it thinks, its task, weapon and load, its nine skills, and what it can be told: eat,
/// rest, fetch a weapon, take its load to a store, show where it is.
/// </summary>
public static class UnitCard
{
    /// <summary>Skill icons: the inline icon of the task class that uses each skill.</summary>
    private static readonly (UnitSkill Skill, int Icon, string Key)[] Skills =
    {
        (UnitSkill.Melee, 14, "hud.skill.melee"),
        (UnitSkill.Ranged, 15, "hud.skill.ranged"),
        (UnitSkill.Scouting, 13, "hud.skill.scouting"),
        (UnitSkill.Harvesting, 1, "hud.skill.harvesting"),
        (UnitSkill.Weaponsmithing, 8, "hud.skill.weaponsmithing"),
        (UnitSkill.Cooking, 9, "hud.skill.cooking"),
        (UnitSkill.Djing, 10, "hud.skill.djing"),
        (UnitSkill.Witchcraft, 11, "hud.skill.witchcraft"),
        (UnitSkill.Building, 12, "hud.skill.building"),
    };

    private const int MaxNeed = 99, FirstThought = 77, NoThought = -1;

    /// <summary>A number that changes when anything the card shows changes.</summary>
    public static int Signature(Unit unit)
    {
        ArgumentNullException.ThrowIfNull(unit);
        var hash = new HashCode();
        hash.Add(unit.Id);
        hash.Add(unit.HitPoints);
        hash.Add(unit.MaxHitPoints);
        hash.Add(unit.Satiety / 5);
        hash.Add(unit.Energy / 5);
        hash.Add(unit.Happiness / 5);
        hash.Add(unit.Thought);
        hash.Add(unit.AssignedTask);
        hash.Add(unit.Weapon);
        hash.Add(unit.CarriedItem);
        hash.Add(unit.CarriedAmount);
        hash.Add(unit.Age);
        hash.Add(unit.Status);
        for (int i = 0; i < Skills.Length; i++)
        {
            hash.Add(unit.Skill(Skills[i].Skill));
        }
        return hash.ToHashCode();
    }

    /// <summary>
    /// The card of <paramref name="unit"/>; <paramref name="own"/> adds the orders it takes,
    /// <paramref name="detailed"/> the nine skills (kept out by default so the card covers less of the map).
    /// </summary>
    public static Control Build(IMatchActions actions, Unit unit, bool own, bool detailed = false)
    {
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(unit);
        HudMetrics m = actions.Metrics;
        VBoxContainer card = HudPanels.Column(2);
        card.Name = "UnitCard";
        card.AddChild(Header(actions, unit));
        card.AddChild(Needs(unit));
        card.AddChild(Status(unit));
        if (detailed)
        {
            card.AddChild(SkillGrid(unit));
        }
        if (own && !actions.Spectating)
        {
            card.AddChild(Orders(actions, unit, m));
        }
        return card;
    }

    /// <summary>The folded panel's tab of <paramref name="unit"/>: its face and name.</summary>
    public static Control Tab(IMatchActions actions, Unit unit)
    {
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(unit);
        HBoxContainer row = HudPanels.Row(4);
        row.Name = "UnitTab";
        row.AddChild(HudPanels.Framed(HudArt.Icon(HudArt.Face(unit.Portrait, actions.ColourOf(unit.Tribe)), 1)));
        Label name = HudWidgets.Value(GameTexts.UnitName(unit), PixelTheme.Variations.Heading);
        name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        name.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        name.ClipText = true;
        row.AddChild(name);
        return row;
    }

    private static Control Header(IMatchActions actions, Unit unit)
    {
        HBoxContainer row = HudPanels.Row(4);
        TextureRect face = HudArt.Icon(HudArt.Face(unit.Portrait, actions.ColourOf(unit.Tribe)), 1);
        face.Name = "Face";
        row.AddChild(HudPanels.Framed(face));
        VBoxContainer text = HudPanels.Column(1);
        text.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        HBoxContainer nameRow = HudPanels.Row(3);
        Label name = HudWidgets.Value(GameTexts.UnitName(unit), PixelTheme.Variations.Heading);
        name.Name = "UnitName";
        name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        name.ClipText = true;
        nameRow.AddChild(name);
        if (actions.World.Tribes[unit.Tribe][TribeCounter.Chief] == unit.Id)
        {
            nameRow.AddChild(HudArt.Icon(HudArt.Painted(PaintedIcon.Chief)));
        }
        nameRow.AddChild(HudArt.Icon(HudArt.Sprite(HudArt.MaleIcon + (unit.Gender == 0 ? 0 : 1))));
        nameRow.AddChild(HudWidgets.Value(Tribes.Client.Ui.Text.UiText.F("hud.unit.age", unit.Age)));
        text.AddChild(nameRow);
        HBoxContainer hp = HudPanels.Row(3);
        var bar = new MeterBar { Fraction = unit.MaxHitPoints > 0 ? Math.Max(0, unit.HitPoints) / (float)unit.MaxHitPoints : 0, CustomMinimumSize = new Vector2(40, 6) };
        bar.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        bar.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        hp.AddChild(bar);
        hp.AddChild(HudWidgets.Value($"{Math.Max(0, unit.HitPoints)}/{unit.MaxHitPoints}", PixelTheme.Variations.Muted));
        text.AddChild(hp);
        row.AddChild(text);
        return row;
    }

    private static Control Needs(Unit unit)
    {
        HBoxContainer row = HudPanels.Row(3);
        row.AddChild(Meter(HudArt.Painted(PaintedIcon.Eat), unit.Satiety, "hud.unit.satiety"));
        row.AddChild(Meter(HudArt.Painted(PaintedIcon.Rest), unit.Energy, "hud.unit.energy"));
        TextureRect mood = HudArt.Icon(HudArt.Mood(unit.Happiness));
        mood.TooltipText = "hud.unit.mood";
        mood.MouseFilter = Control.MouseFilterEnum.Pass;
        row.AddChild(mood);
        row.AddChild(HudWidgets.Value(unit.Happiness.ToString(CultureInfo.InvariantCulture), PixelTheme.Variations.Muted));
        return row;
    }

    private static Control Meter(Texture2D icon, int value, string tip)
    {
        HBoxContainer box = HudPanels.Row(2);
        box.TooltipText = tip;
        box.MouseFilter = Control.MouseFilterEnum.Pass;
        box.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        box.AddChild(HudArt.Icon(icon));
        var bar = new MeterBar { Fraction = Math.Clamp(value, 0, MaxNeed) / (float)MaxNeed, CustomMinimumSize = new Vector2(24, 6) };
        bar.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        bar.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        box.AddChild(bar);
        return box;
    }

    private static Control Status(Unit unit)
    {
        HFlowContainer row = HudPanels.Flow(4);
        if (unit.Thought >= 0 && unit.Thought != NoThought)
        {
            row.AddChild(HudArt.Icon(HudArt.Effect(FirstThought + unit.Thought)));
            row.AddChild(HudWidgets.Value(Tribes.Client.Ui.Text.UiText.T("hud.thought." + unit.Thought.ToString(CultureInfo.InvariantCulture)), PixelTheme.Variations.Muted));
        }
        if (unit.Weapon != RuleNumbers.BareHands && unit.Weapon >= 0 && unit.Weapon < RuleNumbers.StoreWeapons)
        {
            TextureRect weapon = HudArt.Icon(HudArt.BarItem(unit.Weapon));
            weapon.TooltipText = GameTexts.Product(unit.Weapon);
            weapon.MouseFilter = Control.MouseFilterEnum.Pass;
            row.AddChild(weapon);
        }
        if (unit.CarriedItem > 0 && unit.CarriedAmount > 0 && unit.CarriedItem != 15)
        {
            row.AddChild(HudArt.Icon(HudArt.CarriedItem(unit.CarriedItem)));
            row.AddChild(HudWidgets.Value("×" + unit.CarriedAmount.ToString(CultureInfo.InvariantCulture)));
        }
        if ((unit.Status & UnitStatus.Diseased) != 0)
        {
            row.AddChild(HudWidgets.Value(Tribes.Client.Ui.Text.UiText.T("hud.unit.sick"), PixelTheme.Variations.Muted));
        }
        if ((unit.Status & UnitStatus.Protected) != 0)
        {
            row.AddChild(HudWidgets.Value(Tribes.Client.Ui.Text.UiText.T("hud.unit.protected"), PixelTheme.Variations.Muted));
        }
        if ((unit.Status & UnitStatus.Mustered) != 0)
        {
            row.AddChild(HudWidgets.Value(Tribes.Client.Ui.Text.UiText.T("hud.unit.army"), PixelTheme.Variations.Muted));
        }
        if (unit.Happiness < RuleNumbers.WillingHappiness)
        {
            row.AddChild(HudWidgets.Value(Tribes.Client.Ui.Text.UiText.T("hud.unit.unwilling"), PixelTheme.Variations.Muted));
        }
        return row;
    }

    private static Control SkillGrid(Unit unit)
    {
        var grid = new GridContainer { Columns = 3, MouseFilter = Control.MouseFilterEnum.Ignore };
        grid.AddThemeConstantOverride("h_separation", 6);
        grid.AddThemeConstantOverride("v_separation", 0);
        foreach ((UnitSkill skill, int icon, string key) in Skills)
        {
            HBoxContainer cell = HudPanels.Row(2);
            cell.TooltipText = key;
            cell.MouseFilter = Control.MouseFilterEnum.Pass;
            cell.AddChild(HudArt.Icon(HudArt.Inline(icon)));
            cell.AddChild(HudWidgets.Value(unit.Skill(skill).ToString(CultureInfo.InvariantCulture)));
            grid.AddChild(cell);
        }
        return grid;
    }

    private static Control Orders(IMatchActions actions, Unit unit, HudMetrics m)
    {
        var row = (HFlowContainer)OtherCards.GroupOrders(actions, new[] { unit.Id });
        row.Name = "UnitOrders";
        int tribe = actions.Tribe;
        Button eat = HudWidgets.IconButton(m, HudArt.Painted(PaintedIcon.Eat), "hud.order.eat",
            () => actions.Send(new TendNeed(tribe, unit.Id, UnitNeed.Food)), name: "EatButton");
        Button rest = HudWidgets.IconButton(m, HudArt.Painted(PaintedIcon.Rest), "hud.order.rest",
            () => actions.Send(new TendNeed(tribe, unit.Id, UnitNeed.Rest)), name: "RestButton");
        row.AddChild(eat);
        row.AddChild(rest);
        row.MoveChild(eat, 0);
        row.MoveChild(rest, 1);
        return row;
    }
}
