using System;

namespace Tribes.Client.Ui.Logic;

/// <summary>
/// The amounts the gift dialog offers when giving an ally goods: at least one, at most what the
/// giver has and what still fits in the ally's store under the goods cap (the rules keep what
/// does not fit with the giver).
/// </summary>
public static class GoodsGift
{
    /// <summary>The amount picked first: a handful.</summary>
    public const int Handful = 10;

    /// <summary>The most worth giving: what the giver has, no more than fits under <paramref name="cap"/>.</summary>
    public static int Most(int have, int allyHas, int cap) => Math.Max(0, Math.Min(have, cap - Math.Max(0, allyHas)));

    /// <summary><paramref name="asked"/> kept between one and <paramref name="most"/> (0 when nothing can be given).</summary>
    public static int Clamp(int asked, int most) => most <= 0 ? 0 : Math.Clamp(asked, 1, most);

    /// <summary>The amount the dialog starts with.</summary>
    public static int FirstAmount(int most) => Clamp(Handful, most);
}
