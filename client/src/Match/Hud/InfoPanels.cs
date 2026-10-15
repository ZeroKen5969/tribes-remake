using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;
using Tribes.Client.App;
using Tribes.Client.Ui.Art;
using Tribes.Client.Ui.Game;
using Tribes.Client.Ui.Text;
using Tribes.Core.Commands;

namespace Tribes.Client.MatchPlay.Hud;

/// <summary>
/// Help: what the parts of the screen show (the original's help texts) and how to play with a
/// finger and with the mouse and keys. Opening it tells the rules the player looked at the help
/// (a tutorial step waits for that).
/// </summary>
public partial class HelpPanel : HudPopup
{
    /// <summary>The original help texts of the resource bar and the villager panel.</summary>
    private const int FirstHelpText = 38, LastHelpText = 55;

    private static readonly string[] TouchKeys =
    {
        "hud.help.touch.select", "hud.help.touch.order", "hud.help.touch.run", "hud.help.touch.deselect", "hud.help.touch.box",
        "hud.help.touch.multi", "hud.help.touch.pan", "hud.help.touch.zoom", "hud.help.touch.build", "hud.help.touch.minimap",
        "hud.help.touch.players",
    };

    private static readonly string[] MouseKeys =
    {
        "hud.help.mouse.select", "hud.help.mouse.add", "hud.help.mouse.box", "hud.help.mouse.order", "hud.help.mouse.run",
        "hud.help.mouse.deselect", "hud.help.mouse.pan", "hud.help.mouse.zoom", "hud.help.mouse.build", "hud.help.mouse.players",
    };

    private static readonly string[] Keys =
    {
        "hud.help.key.pan", "hud.help.key.zoom", "hud.help.key.pause", "hud.help.key.speed", "hud.help.key.groups",
        "hud.help.key.centre", "hud.help.key.build", "hud.help.key.roster", "hud.help.key.army", "hud.help.key.demolish",
        "hud.help.key.minimap", "hud.help.key.players", "hud.help.key.help", "hud.help.key.fullscreen", "hud.help.key.cancel",
        "hud.help.key.scene",
    };

    private readonly IMatchActions actions;

    /// <summary>The help of the match <paramref name="actions"/> plays.</summary>
    public HelpPanel(IMatchActions actions)
        : base("hud.help", actions?.Metrics ?? throw new ArgumentNullException(nameof(actions)))
    {
        this.actions = actions;
        Name = "HelpPanel";
        MaxWidth = 460;
    }

    /// <summary>The keyboard shortcuts, as shown in the help and the game menu.</summary>
    public static IReadOnlyList<string> ShortcutKeys => Keys;

    /// <inheritdoc/>
    public override void _Ready()
    {
        base._Ready();
        if (!actions.Spectating)
        {
            actions.Send(new ViewedHelp(actions.Tribe));
        }
    }

    /// <inheritdoc/>
    protected override void Fill()
    {
        Section(Metrics.Touch ? "hud.help.touch" : "hud.help.mouse", Metrics.Touch ? TouchKeys : MouseKeys);
        if (!Metrics.Touch)
        {
            Section("hud.help.keys", Keys);
        }
        Content.AddChild(HudPanels.Heading("hud.help.screen"));
        for (int id = FirstHelpText; id <= LastHelpText; id++)
        {
            string text = GameTexts.T(id);
            if (text.Length > 0)
            {
                Content.AddChild(HudWidgets.Text(text, translate: false));
            }
        }
    }

    private void Section(string heading, IEnumerable<string> lines)
    {
        Content.AddChild(HudPanels.Heading(heading));
        foreach (string key in lines)
        {
            Content.AddChild(HudWidgets.Text(key));
        }
        Content.AddChild(HudPanels.Separator());
    }
}

/// <summary>
/// The match's objective. In the campaign: the mission's name, its briefing (the opening
/// dialogue, as the campaign list shows it) and what the story said most recently (the original
/// gives its goals only through the story). On a custom map: the map's name and how it is won and
/// lost (its end conditions). Versus and endless: how a tribe goes out and how the match ends.
/// </summary>
public partial class ObjectivePanel : HudPopup
{
    private readonly IMatchActions actions;
    private readonly IReadOnlyList<int> recentPages;

    /// <summary>The objective of the match <paramref name="actions"/> plays, with the story pages shown so far.</summary>
    public ObjectivePanel(IMatchActions actions, IReadOnlyList<int> recentPages)
        : base("hud.objective", actions?.Metrics ?? throw new ArgumentNullException(nameof(actions)))
    {
        this.actions = actions;
        this.recentPages = recentPages ?? throw new ArgumentNullException(nameof(recentPages));
        Name = "ObjectivePanel";
        MaxWidth = 440;
    }

    /// <summary>End condition bits of a mission: won without enemy buildings; lost with the first building.</summary>
    private const int WinsWithoutEnemyBuildings = 1, LosesFirstBuilding = 2;

    /// <summary>The mission's name as the menus show it.</summary>
    public static string MissionName(int mission) =>
        MissionCatalog.Find(mission)?.Name ?? UiText.F("mission.unnamed", mission);

    /// <summary>The name of the campaign chapter <paramref name="mission"/> is in (the map's name outside the campaign).</summary>
    public static string ChapterName(IMatchActions actions, int mission)
    {
        ArgumentNullException.ThrowIfNull(actions);
        return actions.Mode == MatchMode.Campaign && CampaignEntry(mission) is { } entry ? entry.Name : MissionName(mission);
    }

    /// <summary>The campaign entry of <paramref name="mission"/> (its briefing), or null.</summary>
    private static MissionEntry? CampaignEntry(int mission)
    {
        foreach (MissionEntry entry in MissionCatalog.Campaign)
        {
            if (entry.Mission == mission)
            {
                return entry;
            }
        }
        return null;
    }

    /// <inheritdoc/>
    protected override void Fill()
    {
        int mission = actions.World.Mission;
        bool campaign = actions.Mode == MatchMode.Campaign;
        // A campaign mission is named as its chapter; a custom map as the map.
        // An endless world makes its own land: it is no map of the menus.
        string heading = actions.Mode == MatchMode.Endless ? UiText.T("endless.world") : ChapterName(actions, mission);
        Content.AddChild(HudWidgets.Value(heading, PixelTheme.Variations.Heading));
        switch (actions.Mode)
        {
            case MatchMode.Campaign:
                foreach (int page in CampaignEntry(mission)?.BriefingPages ?? [])
                {
                    AddPage(page);
                }
                break;
            case MatchMode.Custom:
                int conditions = actions.World.Content.Rules.Missions.Missions[mission].EndConditions;
                if ((conditions & WinsWithoutEnemyBuildings) != 0)
                {
                    Content.AddChild(HudWidgets.Text("hud.objective.custom.win"));
                }
                Content.AddChild(HudWidgets.Text("hud.objective.custom.lose"));
                if ((conditions & LosesFirstBuilding) != 0)
                {
                    Content.AddChild(HudWidgets.Text("hud.objective.custom.first_building"));
                }
                break;
            case MatchMode.Versus:
                Content.AddChild(HudWidgets.Text("hud.objective.versus"));
                break;
            case MatchMode.Endless:
                Content.AddChild(HudWidgets.Text("hud.objective.endless"));
                if (actions.GraceEnd() is int end)
                {
                    int minutes = Ui.Logic.EndlessGrace.MinutesLeft(actions.World.Frame, end, Tribes.Session.LocalMatchRunner.TickMilliseconds);
                    Content.AddChild(HudWidgets.Value(minutes > 0 ? UiText.F("endless.grace.left", minutes) : UiText.T("endless.grace.past"), PixelTheme.Variations.Muted));
                }
                break;
        }
        // The story is the campaign's; other matches play none.
        if (campaign && recentPages.Count > 0)
        {
            Content.AddChild(HudPanels.Separator());
            Content.AddChild(HudPanels.Heading("hud.objective.recent"));
            for (int i = recentPages.Count - 1; i >= 0; i--)
            {
                AddPage(recentPages[i]);
            }
        }
    }

    private void AddPage(int page)
    {
        string text = GameTexts.Clean(UiText.Page(page));
        if (text.Length > 0)
        {
            Content.AddChild(HudWidgets.Text(text, translate: false));
        }
    }
}

/// <summary>A save slot as the slot list shows it.</summary>
/// <param name="Slot">"auto" or "1".."5".</param>
/// <param name="Info">The save in it, or null when it is empty.</param>
public sealed record SlotView(string Slot, SaveInfo? Info);

/// <summary>
/// The save slots of the match's kind: the autosave and five manual slots, each with its picture,
/// mission, date and play time. The screen writes the save (and its picture) when a slot is picked.
/// </summary>
public partial class SaveSlotsPanel : HudPopup
{
    private readonly IMatchActions actions;
    private readonly bool saving;
    private readonly string mode;

    /// <summary>The slots for saving (<paramref name="saving"/>) or loading a game of kind <paramref name="mode"/>.</summary>
    public SaveSlotsPanel(IMatchActions actions, bool saving, string mode)
        : base(saving ? "hud.save.title" : "hud.load.title", actions?.Metrics ?? throw new ArgumentNullException(nameof(actions)), HoldReason.Menu)
    {
        this.actions = actions;
        this.saving = saving;
        this.mode = mode;
        Name = saving ? "SavePanel" : "LoadPanel";
        MaxWidth = 440;
    }

    /// <summary>A slot was picked to load: the save's description.</summary>
    public event Action<SaveInfo>? LoadPicked;

    /// <summary>A slot was picked to save in.</summary>
    public event Action<string>? SavePicked;

    /// <summary>The slots of <paramref name="mode"/> in order: auto, then 1..5.</summary>
    public static IReadOnlyList<SlotView> Slots(string mode, IEnumerable<SaveInfo> saves)
    {
        var slots = new List<SlotView>();
        foreach ((string slot, SaveInfo? save) in Logic.SaveSlots.Arrange(mode, saves, s => s.Mode, s => s.Slot, s => s.SavedAt))
        {
            slots.Add(new SlotView(slot, save));
        }
        return slots;
    }

    /// <inheritdoc/>
    protected override void Fill()
    {
        foreach (SlotView slot in Slots(mode, SaveCatalog.List()))
        {
            bool canUse = saving ? Logic.SaveSlots.CanSaveInto(slot.Slot) : slot.Info is not null;
            Content.AddChild(Row(slot, canUse));
        }
    }

    private Control Row(SlotView slot, bool canUse)
    {
        var button = new Button
        {
            Name = "Slot" + slot.Slot,
            ThemeTypeVariation = PixelTheme.Variations.Tool,
            FocusMode = FocusModeEnum.None,
            Disabled = !canUse,
            CustomMinimumSize = new Vector2(0, Math.Max(52, Metrics.Button)),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        HBoxContainer row = HudPanels.Row(6);
        row.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        row.OffsetLeft = 4;
        row.OffsetRight = -4;
        var picture = new TextureRect
        {
            Texture = slot.Info is null ? null : MatchConnection.Thumbnail(Logic.SaveSlots.Stem(slot.Info.Mode, slot.Info.Slot)),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            CustomMinimumSize = new Vector2(80, 45),
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        row.AddChild(HudPanels.Framed(picture));
        VBoxContainer text = HudPanels.Column(0);
        text.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        text.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        string name = slot.Slot == Logic.SaveSlots.Auto ? UiText.T("load.autosave") : UiText.F("load.slot", slot.Slot);
        text.AddChild(HudWidgets.Value(name, PixelTheme.Variations.Heading));
        if (slot.Info is { } info)
        {
            text.AddChild(HudWidgets.Value(info.Title.Length > 0 ? info.Title : ObjectivePanel.MissionName(info.Mission)));
            string when = info.SavedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            text.AddChild(HudWidgets.Value(UiText.F("load.details", when, SaveCatalog.PlayTime(info.PlaySeconds)), PixelTheme.Variations.Muted));
        }
        else
        {
            text.AddChild(HudWidgets.Value(UiText.T("hud.save.empty"), PixelTheme.Variations.Muted));
        }
        row.AddChild(text);
        button.AddChild(row);
        button.Pressed += () => Pick(slot);
        return button;
    }

    private void Pick(SlotView slot)
    {
        if (saving)
        {
            if (slot.Info is not null)
            {
                Tribes.Client.Ui.Widgets.ModalDialog.Show(actions.DialogLayer, "hud.save.overwrite",
                    (Tribes.Client.Ui.UiKeys.Yes, () => Save(slot.Slot)), (Tribes.Client.Ui.UiKeys.No, null));
                return;
            }
            Save(slot.Slot);
            return;
        }
        if (slot.Info is { } info)
        {
            // The match asks first; the list stays open (and the game held) until it is answered.
            LoadPicked?.Invoke(info);
        }
    }

    private void Save(string slot)
    {
        SavePicked?.Invoke(slot);
        Refill();
    }
}
