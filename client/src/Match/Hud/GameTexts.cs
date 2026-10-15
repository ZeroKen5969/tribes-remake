using System;
using System.Globalization;
using System.Text;
using Godot;
using Tribes.Client.MatchPlay.Logic;
using Tribes.Client.Ui.Text;
using Tribes.Core.World;

namespace Tribes.Client.MatchPlay.Hud;

/// <summary>
/// The original game's words for what the match shows (tables s, t and d of the original
/// strings): building, weapon, trap and spell names, villager names, notices, hints, help and
/// the result screen; plus the original's control codes inside texts (inline icons {0}..{22},
/// value columns {23}..{30}).
/// </summary>
public static class GameTexts
{
    private const int FirstWeaponName = 12, FirstTrapName = 18, FirstSpellName = 30, FirstUnitName = 108;
    private const int ChiefName = 104, ShamanName = 105;
    private const int FirstHintText = 12;
    private const int FirstNotice = 41, BornMale = 42, BornFemale = 43, DiedMale = 44, DiedFemale = 45;
    private const int UnitBorn = 1, UnitDied = 3;
    private const int LastInlineIcon = 22;

    /// <summary>Name of building kind <paramref name="kind"/> (0..11; sites and traps get the name of what they become).</summary>
    public static string Building(int kind) => Tribes.Core.Systems.Endless.AllianceBuilding.Is(kind) ? UiText.T("building.alliance.name")
        : kind >= 0 && kind < RuleNumbers.PlannedKinds ? UiText.S(kind)
        : RuleNumbers.IsTrap(kind) ? UiText.S(FirstTrapName + kind - RuleNumbers.FirstTrap)
        : UiText.T("hud.building.site");

    /// <summary>The build menu's description of building kind <paramref name="kind"/> (0..11).</summary>
    public static string BuildingDescription(int kind) => Tribes.Core.Systems.Endless.AllianceBuilding.Is(kind)
        ? UiText.T("building.alliance.desc")
        : UiText.T("t." + kind.ToString(CultureInfo.InvariantCulture));

    /// <summary>Name of weapon or trap product <paramref name="product"/> (0..5 weapons, 6..8 traps).</summary>
    public static string Product(int product) => UiText.S(FirstWeaponName + product);

    /// <summary>Name of spell <paramref name="spell"/> (0 healing ... 3 disease).</summary>
    public static string Spell(int spell) => UiText.S(FirstSpellName + spell);

    /// <summary>The original's action-bar entry name s.(21 + entry - 10) for entries 10..29.</summary>
    public static string Entry(int entry) => UiText.S(21 + entry - 10);

    /// <summary>A villager's name.</summary>
    public static string UnitName(Unit unit)
    {
        ArgumentNullException.ThrowIfNull(unit);
        return UiText.S(FirstUnitName + unit.NameIndex);
    }

    /// <summary>The name a story scene shows for its speaker: the chief is "chief", the shaman of some scenes "shaman".</summary>
    public static string SpeakerName(GameWorld world, Unit unit, bool shaman)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(unit);
        if (shaman)
        {
            return UiText.S(ShamanName);
        }
        return world.Tribes[unit.Tribe][TribeCounter.Chief] == unit.Id ? UiText.S(ChiefName) : UnitName(unit);
    }

    /// <summary>Text of hint <paramref name="hint"/>.</summary>
    public static string Hint(int hint) => T(FirstHintText + hint);

    /// <summary>Text <paramref name="id"/> of the original's table t (ids from 71 on are the story pages, keys d.N).</summary>
    public static string T(int id) => Clean(Raw(id));

    /// <summary>Text <paramref name="id"/> of table t with its control codes kept.</summary>
    public static string Raw(int id) =>
        UiText.T((id >= FirstStoryText ? "d." : "t.") + id.ToString(CultureInfo.InvariantCulture));

    private const int FirstStoryText = 71;

    /// <summary>The message-list text of notice kind <paramref name="kind"/>; born and died name the unit.</summary>
    public static string Notice(GameWorld world, int kind, int unit)
    {
        ArgumentNullException.ThrowIfNull(world);
        if ((kind == UnitBorn || kind == UnitDied) && unit >= 1 && unit <= world.UnitSlots)
        {
            Unit u = world.Unit(unit);
            bool female = u.Gender != 0;
            int verb = kind == UnitBorn ? (female ? BornFemale : BornMale) : (female ? DiedFemale : DiedMale);
            return UnitName(u) + " " + UiText.S(verb);
        }
        return UiText.S(FirstNotice + kind);
    }

    /// <summary>
    /// The text without the original's control codes: inline icons are dropped, value columns
    /// take <paramref name="value"/> (or are dropped).
    /// </summary>
    public static string Clean(string text, Func<int, string?>? value = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        var result = new StringBuilder(text.Length);
        foreach ((string? plain, int code) in Pieces(text))
        {
            if (plain is not null)
            {
                result.Append(plain);
            }
            else if (code > LastInlineIcon && value?.Invoke(code) is { } v)
            {
                result.Append(v);
            }
        }
        return result.ToString().Trim();
    }

    /// <summary>
    /// Fills <paramref name="label"/> with <paramref name="text"/>, showing inline icons as the
    /// original's pictures (at <paramref name="iconScale"/>) and value columns as <paramref name="value"/>.
    /// </summary>
    public static void Fill(RichTextLabel label, string text, int iconScale = 1, Func<int, string?>? value = null)
    {
        ArgumentNullException.ThrowIfNull(label);
        ArgumentNullException.ThrowIfNull(text);
        label.Clear();
        foreach ((string? plain, int code) in Pieces(text))
        {
            if (plain is not null)
            {
                label.AddText(plain);
            }
            else if (code <= LastInlineIcon)
            {
                Texture2D icon = HudArt.Inline(code);
                label.AddImage(icon, (int)icon.GetSize().X * iconScale, (int)icon.GetSize().Y * iconScale);
            }
            else if (value?.Invoke(code) is { } v)
            {
                label.AddText(v);
            }
        }
    }

    /// <summary>Splits a text into plain runs and control codes ({N}).</summary>
    private static System.Collections.Generic.IEnumerable<(string? Plain, int Code)> Pieces(string text)
    {
        int start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] != '{')
            {
                continue;
            }
            int end = text.IndexOf('}', i + 1);
            if (end < 0 || !int.TryParse(text.AsSpan(i + 1, end - i - 1), NumberStyles.None, CultureInfo.InvariantCulture, out int code))
            {
                continue;
            }
            if (i > start)
            {
                yield return (text[start..i], 0);
            }
            yield return (null, code);
            start = end + 1;
            i = end;
        }
        if (start < text.Length)
        {
            yield return (text[start..], 0);
        }
    }
}
