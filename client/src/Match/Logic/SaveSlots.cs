using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Tribes.Client.MatchPlay.Logic;

/// <summary>
/// The save slots of a match kind: the autosave (written by the game, never chosen for saving)
/// and the numbered slots, in the order the slot list shows them, with the save found in each.
/// </summary>
public static class SaveSlots
{
    /// <summary>The autosave slot.</summary>
    public const string Auto = "auto";

    /// <summary>Numbered slots per match kind.</summary>
    public const int Numbered = 5;

    /// <summary>The slot names in list order: auto, then 1..5.</summary>
    public static IReadOnlyList<string> Names()
    {
        var names = new List<string> { Auto };
        for (int i = 1; i <= Numbered; i++)
        {
            names.Add(i.ToString(CultureInfo.InvariantCulture));
        }
        return names;
    }

    /// <summary>Whether the player may save into <paramref name="slot"/> (not the autosave).</summary>
    public static bool CanSaveInto(string slot) => slot != Auto && Names().Contains(slot);

    /// <summary>The file stem of a save: "&lt;mode&gt;-&lt;slot&gt;" (its .save, .json and .png share it).</summary>
    public static string Stem(string mode, string slot) => mode + "-" + slot;

    /// <summary>
    /// The slots of match kind <paramref name="mode"/> in list order, each with the save of
    /// <paramref name="saves"/> that is in it (the newest when several claim one), or null.
    /// </summary>
    public static IReadOnlyList<(string Slot, T? Save)> Arrange<T>(string mode, IEnumerable<T> saves,
        Func<T, string> modeOf, Func<T, string> slotOf, Func<T, DateTime> savedAt) where T : class
    {
        ArgumentNullException.ThrowIfNull(saves);
        ArgumentNullException.ThrowIfNull(modeOf);
        ArgumentNullException.ThrowIfNull(slotOf);
        ArgumentNullException.ThrowIfNull(savedAt);
        var bySlot = new Dictionary<string, T>(StringComparer.Ordinal);
        foreach (T save in saves)
        {
            if (modeOf(save) != mode)
            {
                continue;
            }
            string slot = slotOf(save);
            if (!bySlot.TryGetValue(slot, out T? held) || savedAt(save) > savedAt(held))
            {
                bySlot[slot] = save;
            }
        }
        var slots = new List<(string, T?)>();
        foreach (string name in Names())
        {
            slots.Add((name, bySlot.GetValueOrDefault(name)));
        }
        return slots;
    }
}
