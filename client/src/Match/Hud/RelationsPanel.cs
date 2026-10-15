using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;
using Tribes.Client.MatchPlay.Logic;
using Tribes.Client.Ui;
using Tribes.Client.Ui.Art;
using Tribes.Client.Ui.Logic;
using Tribes.Client.Ui.Text;
using Tribes.Client.Ui.Widgets;
using Tribes.Core.Systems.Endless;
using Tribes.Core.World;

namespace Tribes.Client.MatchPlay.Hud;

/// <summary>
/// The tribes of an endless world and how they stand with this player's tribe (see
/// <see cref="RelationsList"/>): allies, open invitations, the other people's and the computer's
/// tribes. With a finished alliance hall the player invites, answers, gives allies goods and
/// leaves alliances from here; a watcher only reads it. Rebuilt when the alliances change.
/// </summary>
public partial class RelationsPanel : HudPopup
{
    private readonly IMatchActions actions;
    private IReadOnlyList<TribeFacts> shown = Array.Empty<TribeFacts>();
    private bool[] halls = Array.Empty<bool>();
    private int waiting;

    /// <summary>The panel for the match <paramref name="actions"/> plays.</summary>
    public RelationsPanel(IMatchActions actions)
        : base("relations.title", actions?.Metrics ?? throw new ArgumentNullException(nameof(actions)), hold: null)
    {
        this.actions = actions;
        Name = "RelationsPanel";
        MaxWidth = 420;
    }

    /// <summary>Rebuilds the rows when what they show changed (a hall built or lost, an alliance, a player away).</summary>
    public void FollowWorld()
    {
        GameWorld world = actions.World;
        IReadOnlyList<TribeFacts> facts = AllianceFacts.For(world, actions.Spectating ? -1 : actions.Tribe);
        if (!SameFacts(facts, shown) || !SameHalls(world) || (world.Endless?.WaitingTribes ?? 0) != waiting)
        {
            Refill();
        }
    }

    private bool SameHalls(GameWorld world)
    {
        if (halls.Length != world.Tribes.Count)
        {
            return false;
        }
        for (int t = 0; t < halls.Length; t++)
        {
            if (halls[t] != AllianceRules.HasAllianceBuilding(world, t))
            {
                return false;
            }
        }
        return true;
    }

    private static bool SameFacts(IReadOnlyList<TribeFacts> a, IReadOnlyList<TribeFacts> b)
    {
        if (a.Count != b.Count)
        {
            return false;
        }
        for (int i = 0; i < a.Count; i++)
        {
            if (a[i] != b[i])
            {
                return false;
            }
        }
        return true;
    }

    /// <inheritdoc/>
    protected override void Fill()
    {
        GameWorld world = actions.World;
        int local = actions.Spectating ? -1 : actions.Tribe;
        shown = AllianceFacts.For(world, local);
        waiting = world.Endless?.WaitingTribes ?? 0;
        halls = new bool[world.Tribes.Count];
        for (int t = 0; t < halls.Length; t++)
        {
            halls[t] = AllianceRules.HasAllianceBuilding(world, t);
        }
        if (RelationsList.HallNote(local >= 0 && AllianceRules.HasAllianceBuilding(world, local), actions.Spectating) is { } note)
        {
            Content.AddChild(HudWidgets.Text(note));
        }
        if (world.Endless is { WaitingTribes: > 0 } endless)
        {
            // Computer tribes asked for at the start that wait for land the people uncover (above the long list).
            Content.AddChild(HudWidgets.Value(UiText.F("relations.waiting", endless.WaitingTribes), PixelTheme.Variations.Muted));
        }
        foreach (RelationRow row in RelationsList.Build(shown))
        {
            Content.AddChild(Row(row, local));
        }
    }

    private Control Row(RelationRow row, int local)
    {
        var panel = new PanelContainer { ThemeTypeVariation = PixelTheme.Variations.Slot, Name = "Tribe" + row.Tribe.ToString(CultureInfo.InvariantCulture) };
        VBoxContainer column = HudPanels.Column(2);
        panel.AddChild(column);
        HBoxContainer head = HudPanels.Row(4);
        int colour = Math.Clamp(actions.ColourOf(row.Tribe), 0, 7);
        head.AddChild(new ColorRect
        {
            Color = new Color(Tribes.Client.World.WorldView.Colours.Colours[colour].Swatch),
            CustomMinimumSize = new Vector2(10, 10),
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
            MouseFilter = MouseFilterEnum.Ignore,
        });
        Label name = HudWidgets.Value(PlayersStrip.TribeName(actions, row.Tribe), PixelTheme.Variations.Heading);
        name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        name.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        head.AddChild(name);
        if (AllianceRules.HasAllianceBuilding(actions.World, row.Tribe))
        {
            TextureRect hall = HudArt.Icon(HudArt.Painted(PaintedIcon.Alliance));
            hall.TooltipText = "relations.has_hall";
            hall.MouseFilter = MouseFilterEnum.Pass;
            head.AddChild(hall);
        }
        head.AddChild(HudWidgets.Value(UiText.T(row.StatusKey), PixelTheme.Variations.Muted));
        column.AddChild(head);
        if (local >= 0 && row.Tribe != local && actions.World.Relations.AreAllies(local, row.Tribe)
            && AllianceRules.HasAllianceBuilding(actions.World, local) && !AllianceRules.HasAllianceBuilding(actions.World, row.Tribe))
        {
            // Goods go from hall to hall: say why there is no "give" for this ally.
            column.AddChild(HudWidgets.Value(UiText.T("relations.ally_no_hall"), PixelTheme.Variations.Muted));
        }
        if (row.Actions.Count > 0)
        {
            HFlowContainer buttons = HudPanels.Flow(Metrics.Gap);
            buttons.Alignment = FlowContainer.AlignmentMode.End;
            foreach (RelationAction action in row.Actions)
            {
                buttons.AddChild(ActionButton(action, row.Tribe));
            }
            column.AddChild(buttons);
        }
        return panel;
    }

    private Button ActionButton(RelationAction action, int other)
    {
        string key = action switch
        {
            RelationAction.Accept => "relations.accept",
            RelationAction.Decline => "relations.decline",
            RelationAction.Invite => "relations.invite",
            RelationAction.Give => "relations.give",
            _ => "relations.leave",
        };
        Button button = HudWidgets.TextButton(Metrics, null, key, () => Act(action, other),
            action + "Button" + other.ToString(CultureInfo.InvariantCulture), action is RelationAction.Accept or RelationAction.Invite ? PixelTheme.Variations.Primary : null);
        return button;
    }

    private void Act(RelationAction action, int other)
    {
        int local = actions.Tribe;
        switch (action)
        {
            case RelationAction.Give:
                Close();
                actions.Open(new GiftPanel(actions, other));
                return;
            case RelationAction.Leave:
                ModalDialog.Show(actions.DialogLayer, UiText.F("relations.leave_confirm", PlayersStrip.TribeName(actions, other)),
                    (UiKeys.Yes, () => actions.Send(AllianceOrders.Leave(local, other))), (UiKeys.No, null));
                return;
            case RelationAction.Invite:
                actions.Send(AllianceOrders.Invite(local, other));
                break;
            case RelationAction.Accept:
                actions.Send(AllianceOrders.Answer(local, other, accept: true));
                break;
            case RelationAction.Decline:
                actions.Send(AllianceOrders.Answer(local, other, accept: false));
                break;
        }
    }
}

/// <summary>
/// Giving an ally goods: pick one of the goods allies may give (with how many the tribe has),
/// the amount (at most what fits in the ally's store, see <see cref="GoodsGift"/>), then give.
/// </summary>
public partial class GiftPanel : HudPopup
{
    private readonly IMatchActions actions;
    private readonly int ally;
    private TribeCounter chosen = TribeCounter.Food;
    private NumberStepper amount = null!;
    private Label most = null!;
    private Button give = null!;

    /// <summary>The gift to <paramref name="ally"/> in the match <paramref name="actions"/> plays.</summary>
    public GiftPanel(IMatchActions actions, int ally)
        : base("relations.gift.title", actions?.Metrics ?? throw new ArgumentNullException(nameof(actions)), hold: null)
    {
        this.actions = actions;
        this.ally = ally;
        Name = "GiftPanel";
        MaxWidth = 420;
    }

    /// <inheritdoc/>
    protected override void Fill()
    {
        GameWorld world = actions.World;
        Content.AddChild(HudWidgets.Value(UiText.F("relations.gift.to", PlayersStrip.TribeName(actions, ally)), PixelTheme.Variations.Heading));
        // A grid, so every good shows its name and how many the tribe has.
        var goods = new GridContainer { Name = "Goods", Columns = Metrics.Portrait ? 2 : 3 };
        goods.AddThemeConstantOverride("h_separation", Metrics.Gap);
        goods.AddThemeConstantOverride("v_separation", Metrics.Gap);
        var group = new ButtonGroup();
        IReadOnlyList<TribeCounter> offered = world.Content.Endless?.Alliance.Goods ?? Array.Empty<TribeCounter>();
        bool first = true;
        foreach (TribeCounter good in offered)
        {
            int have = world.Tribes[actions.Tribe][good];
            Button button = HudWidgets.TextButton(Metrics, AllianceTexts.GoodIcon(good),
                AllianceTexts.GoodName(good) + " " + have.ToString(CultureInfo.InvariantCulture), () => Choose(good), "Good" + good);
            button.ToggleMode = true;
            button.ButtonGroup = group;
            button.AutoTranslateMode = AutoTranslateModeEnum.Disabled;
            button.Disabled = Most(good) <= 0;
            goods.AddChild(button);
            if (first && !button.Disabled)
            {
                first = false;
                chosen = good;
                button.SetPressedNoSignal(true);
            }
        }
        Content.AddChild(goods);
        amount = new NumberStepper(0, 1) { Name = "Amount" };
        Content.AddChild(UiFactory.Row("relations.gift.amount", amount));
        most = HudWidgets.Value("", PixelTheme.Variations.Muted);
        Content.AddChild(most);
        give = HudWidgets.TextButton(Metrics, null, "relations.gift.give", Give, "GiveButton", PixelTheme.Variations.Primary);
        Footer.AddChild(give);
        Choose(chosen);
    }

    /// <summary>Whether the gift can still be given (both tribes allies with their halls).</summary>
    public bool StillPossible() => !actions.Spectating && AllianceRules.CanTransfer(actions.World, actions.Tribe, ally);

    private int Most(TribeCounter good)
    {
        GameWorld world = actions.World;
        int cap = world.Content.Endless?.Alliance.GoodsCap ?? 0;
        return GoodsGift.Most(world.Tribes[actions.Tribe][good], world.Tribes[ally][good], cap);
    }

    private void Choose(TribeCounter good)
    {
        chosen = good;
        int top = Most(good);
        amount.Highest = Math.Max(1, top);
        amount.Value = GoodsGift.FirstAmount(top);
        most.Text = UiText.F("relations.gift.most", top.ToString(CultureInfo.InvariantCulture), AllianceTexts.GoodName(good));
        give.Disabled = top <= 0;
    }

    private void Give()
    {
        int top = Most(chosen);
        int value = GoodsGift.Clamp(amount.Value, top);
        if (value <= 0)
        {
            return;
        }
        actions.Send(AllianceOrders.Gift(actions.Tribe, ally, chosen, value));
        Close();
    }
}
