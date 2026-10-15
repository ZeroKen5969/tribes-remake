using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;
using Tribes.Client.MatchPlay.Logic;
using Tribes.Client.Ui.Art;
using Tribes.Core.Commands;
using Tribes.Core.Content;
using Tribes.Core.World;

namespace Tribes.Client.MatchPlay.Hud;

/// <summary>
/// The store's menu for the selected units, as the original opens it when units are sent to
/// the tribe's own store: unload what they carry, or take a weapon the tribe has in stock.
/// Also shows the stock itself.
/// </summary>
public partial class StorePanel : HudPopup
{
    private const int UnloadEntry = 29;
    private readonly IMatchActions actions;
    private readonly int store;
    private readonly GridPoint cell;
    private readonly UnitSet units;

    /// <summary>The menu of store <paramref name="store"/> (walking to <paramref name="cell"/>) for <paramref name="units"/>.</summary>
    public StorePanel(IMatchActions actions, int store, GridPoint cell, UnitSet units)
        : base("hud.store.title", actions?.Metrics ?? throw new ArgumentNullException(nameof(actions)), hold: null)
    {
        this.actions = actions;
        this.store = store;
        this.cell = cell;
        this.units = units ?? throw new ArgumentNullException(nameof(units));
        Name = "StorePanel";
    }

    /// <summary>The tribe's stock: food, wood, stones, bones, meals and each weapon.</summary>
    public static Control Stock(GameWorld world, int tribe)
    {
        ArgumentNullException.ThrowIfNull(world);
        TribeState stock = world.Tribes[tribe];
        HFlowContainer flow = HudPanels.Flow(6);
        flow.Name = "Stock";
        Add(flow, HudArt.Piece(HudArt.FoodPiece), stock[TribeCounter.Food], "hud.top.food");
        Add(flow, HudArt.Piece(HudArt.WoodPiece), stock[TribeCounter.Wood], "hud.top.wood");
        Add(flow, HudArt.Piece(HudArt.StonesPiece), stock[TribeCounter.Stones], "hud.top.stones");
        Add(flow, HudArt.Piece(HudArt.BonesPiece), stock[TribeCounter.Bones], "hud.top.bones");
        Add(flow, HudArt.Painted(PaintedIcon.Eat), stock[TribeCounter.Meals], "hud.store.meals");
        for (int weapon = 0; weapon < RuleNumbers.StoreWeapons; weapon++)
        {
            int count = weapon == RuleNumbers.Stone ? stock[TribeCounter.Stones] / 5 : stock.Counter((int)TribeCounter.Clubs + weapon);
            Add(flow, HudArt.BarItem(weapon), count, GameTexts.Product(weapon));
        }
        return flow;
    }

    /// <inheritdoc/>
    protected override void Fill()
    {
        GameWorld world = actions.World;
        Content.AddChild(Stock(world, actions.Tribe));
        Content.AddChild(HudPanels.Separator());
        HudMetrics m = Metrics;
        if (BuildingOffers.AnyCarrying(world, units))
        {
            Content.AddChild(HudWidgets.TextButton(m, HudArt.Effect(UnloadEntry), GameTexts.Entry(UnloadEntry), () =>
            {
                actions.Send(new DeliverToStore(actions.Tribe, units, store, cell));
                Close();
            }, name: "DeliverButton"));
        }
        IReadOnlyList<int> weapons = BuildingOffers.WeaponsInStock(world, actions.Tribe);
        foreach (int weapon in weapons)
        {
            int chosen = weapon;
            Content.AddChild(HudWidgets.TextButton(m, HudArt.BarItem(weapon), GameTexts.Product(weapon), () =>
            {
                actions.Send(new FetchWeapon(actions.Tribe, units, store, chosen, cell));
                Close();
            }, name: "Fetch" + weapon.ToString(CultureInfo.InvariantCulture)));
        }
        if (weapons.Count == 0 && !BuildingOffers.AnyCarrying(world, units))
        {
            Content.AddChild(HudWidgets.Text("hud.store.nothing", PixelTheme.Variations.Muted));
        }
    }

    private static void Add(HFlowContainer flow, Texture2D icon, int count, string tip)
    {
        HBoxContainer box = HudPanels.Row(2);
        box.TooltipText = tip;
        box.MouseFilter = MouseFilterEnum.Pass;
        box.AddChild(HudArt.Icon(icon));
        box.AddChild(HudWidgets.Value(Math.Max(0, count).ToString(CultureInfo.InvariantCulture)));
        flow.AddChild(box);
    }
}
