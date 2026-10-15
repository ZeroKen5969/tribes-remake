using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;
using Tribes.Client.MatchPlay.Logic;
using Tribes.Client.Ui.Logic;
using Tribes.Client.Ui.Text;
using Tribes.Core.World;

namespace Tribes.Client.MatchPlay.Hud;

/// <summary>
/// Words and icons of an endless world's alliances: the goods allies may give each other, and
/// the messages about new tribes, invitations, alliances and gifts (<see cref="EndlessNewsLines"/>).
/// </summary>
public static class AllianceTexts
{
    /// <summary>The name of store counter <paramref name="good"/>.</summary>
    public static string GoodName(TribeCounter good) => good switch
    {
        TribeCounter.Meals => UiText.T("hud.store.meals"),
        TribeCounter.Food => UiText.T("hud.top.food"),
        TribeCounter.Wood => UiText.T("hud.top.wood"),
        TribeCounter.Stones => UiText.T("hud.top.stones"),
        TribeCounter.Bones => UiText.T("hud.top.bones"),
        // The weapon of stones is named like the stones themselves in the original.
        TribeCounter.StoneWeapons => UiText.T("relations.gift.throwing_stones"),
        _ when IsWeapon(good) => GameTexts.Product((int)good - (int)TribeCounter.Clubs),
        _ => good.ToString(),
    };

    /// <summary>The icon of store counter <paramref name="good"/>, as the store shows it.</summary>
    public static Texture2D GoodIcon(TribeCounter good) => good switch
    {
        TribeCounter.Meals => HudArt.Painted(PaintedIcon.Eat),
        TribeCounter.Food => HudArt.Piece(HudArt.FoodPiece),
        TribeCounter.Wood => HudArt.Piece(HudArt.WoodPiece),
        TribeCounter.Stones => HudArt.Piece(HudArt.StonesPiece),
        TribeCounter.Bones => HudArt.Piece(HudArt.BonesPiece),
        _ when IsWeapon(good) => HudArt.BarItem((int)good - (int)TribeCounter.Clubs),
        _ => HudArt.Painted(PaintedIcon.Objective),
    };

    /// <summary>
    /// The message for this player (<paramref name="local"/>, -1 watching) about
    /// <paramref name="news"/>, or null when it is not for them; gifts name the amount and goods.
    /// </summary>
    public static string? Describe(IMatchActions actions, EndlessNews news, int a, int b, int by, int local,
        TribeCounter good = TribeCounter.Food, int amount = 0)
    {
        ArgumentNullException.ThrowIfNull(actions);
        // An alliance also ends by itself when one side goes out (the rules mark it out that frame).
        bool byIsOut = news == EndlessNews.Ended && by >= 0 && by < actions.World.Tribes.Count && actions.World.Tribes[by].OutAtFrame >= 0;
        if (EndlessNewsLines.For(news, a, b, by, local, byIsOut) is not { } line)
        {
            return null;
        }
        var args = new List<object>();
        foreach (int tribe in line.Tribes)
        {
            args.Add(PlayersStrip.TribeName(actions, tribe));
        }
        if (news == EndlessNews.Transferred)
        {
            args.Add(amount.ToString(CultureInfo.InvariantCulture));
            args.Add(GoodName(good));
        }
        return UiText.F(line.Key, args.ToArray());
    }

    private static bool IsWeapon(TribeCounter good) => good >= TribeCounter.Clubs && good <= TribeCounter.Tomahawks;
}
