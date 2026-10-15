using System.Collections.Generic;
using System.IO;
using Tribes.Core.Content;

namespace Tribes.Core.World;

/// <summary>Saving, loading and hashing the world (see <see cref="StateExchange"/>).</summary>
public sealed partial class GameWorld
{
    /// <summary>The longest shared table a save may hold (far more entries than any world uses).</summary>
    private const int MostTableEntries = 1 << 22;

    /// <summary>
    /// Saves, loads or hashes every value of the world. Format 6 keeps each tribe's frames since it
    /// fought (older saves give every tribe the world's count). Format 5 keeps the owner of every unit and
    /// building slot and the length of every table that grows in a match (the mode comes first, so
    /// the slots made while reading know whether they are a match's); formats before it held the
    /// original's slot blocks and tables sized by the number of tribes, and a match loaded from them
    /// goes on with slots and tables that grow. Format 2 keeps each tribe's controller, team, player
    /// and computer state with the tribe, and the alliances; format 1 (the original two tribes only)
    /// kept one computer state and one player state at the end.
    /// </summary>
    internal void Exchange(StateExchange s)
    {
        bool slotted = s.Format >= StateExchange.SlotsFormat;
        Frame = s.Value(Frame);
        RowSweep = s.Value(RowSweep);
        Mission = s.Value(Mission);
        Difficulty = s.Value(Difficulty);
        EndCountdown = s.Value(EndCountdown);
        FramesSinceCombat = s.Value(FramesSinceCombat);
        SpawnGenderMode = s.Value(SpawnGenderMode);
        Random.State = s.Value64(Random.State);
        if (slotted)
        {
            Mode = (WorldMode)s.Value((int)Mode);
            if (s.Flag(limitsLifted) && !limitsLifted)
            {
                LiftLimits();
            }
        }
        Map.Exchange(s);
        if (slotted)
        {
            ExchangeSlots(s);
        }
        for (int id = 1; id < units.Count; id++)
        {
            units[id].Exchange(s);
        }
        for (int id = 1; id < buildings.Count; id++)
        {
            buildings[id].Exchange(s);
        }
        if (Map.IsChunked)
        {
            int count = s.Value(animals.Count);
            while (animals.Count < count)
            {
                AddAnimalSlot();
            }
        }
        foreach (Animal animal in animals)
        {
            animal.Exchange(s);
        }
        bool tribal = s.Format >= StateExchange.TribalFormat;
        for (int t = 0; t < tribes.Count; t++)
        {
            tribes[t].Exchange(s);
            if (tribal)
            {
                tribes[t].ExchangeRoles(s);
            }
            fog[t].Exchange(s, packed: tribal);
            if (s.IsReading && s.Format < StateExchange.CombatFormat)
            {
                // Older saves kept only the world's count: every tribe goes on from it.
                tribes[t].FramesSinceCombat = FramesSinceCombat;
            }
        }
        if (!slotted && s.IsReading)
        {
            GrowTables(tribes.Count);
        }
        ExchangeTables(s);
        Script.Exchange(s);
        if (tribal)
        {
            Relations.Exchange(s);
            if (!slotted)
            {
                Mode = (WorldMode)s.Value((int)Mode);
            }
            Match.Exchange(s);
            ExchangeEndless(s);
            if (s.Format >= StateExchange.SettingsFormat)
            {
                Settings = WorldSettings.Exchange(s, Settings);
            }
            if (!slotted && s.IsReading && GrowsOnDemand)
            {
                GrowFromHere();
            }
            if (s.IsReading && s.Format < StateExchange.MemoryFormat && Mode == WorldMode.Match)
            {
                RememberExploredBuildings();
            }
            return;
        }
        Ai.Exchange(s);
        Orders.Exchange(s);
        Hints.Exchange(s);
    }

    /// <summary>
    /// For a match saved before buildings were remembered (format 6 and older): each human-ruled
    /// tribe remembers, as they are now, the buildings of which it has explored any cell (what its
    /// map showed then).
    /// </summary>
    private void RememberExploredBuildings()
    {
        var kinds = Content.Rules.Buildings.Kinds;
        foreach (TribeState tribe in tribes)
        {
            if (!tribe.Controller.IsHumanRuled)
            {
                continue;
            }
            FogOfWar tribeFog = fog[tribe.Index];
            for (int id = 1; id < buildings.Count; id++)
            {
                Building building = buildings[id];
                if (building.InUse && AnyExplored(tribeFog, building.X, building.Y, kinds[building.Kind].Width, kinds[building.Kind].Height))
                {
                    tribeFog.Buildings.Remember(RememberedBuilding.Of(building, Frame));
                }
            }
        }
    }

    private static bool AnyExplored(FogOfWar tribeFog, int x0, int y0, int width, int height)
    {
        for (int y = y0; y < y0 + height; y++)
        {
            for (int x = x0; x < x0 + width; x++)
            {
                if (tribeFog.IsExplored(new GridPoint(x, y)))
                {
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>The endless state, when the world has one (format 3 on).</summary>
    private void ExchangeEndless(StateExchange s)
    {
        if (s.Format < StateExchange.EndlessFormat || !s.Flag(Endless != null))
        {
            return;
        }
        Endless ??= new EndlessState(0, 0);
        Endless.Exchange(s);
    }

    private void ExchangeTables(StateExchange s)
    {
        ExchangeTable(s, dust);
        ExchangeTable(s, traps);
        ExchangeTable(s, construction);
        ExchangeTable(s, upgrades);
        ExchangeTable(s, couples);
        Sectors.Exchange(s);
        for (int i = 0; i < AnimalZones; i++)
        {
            animalZoneCounts[i] = s.Value(animalZoneCounts[i]);
        }
        for (int i = 0; i < UnlockableKinds; i++)
        {
            unlockLevels[i] = s.Value(unlockLevels[i]);
        }
    }

    /// <summary>The entries of <paramref name="table"/>, after its length (format 5 on; a table never shrinks).</summary>
    private static void ExchangeTable<T>(StateExchange s, List<T> table)
        where T : class, ITableEntry, new()
    {
        if (s.Format >= StateExchange.SlotsFormat)
        {
            int count = s.Value(table.Count);
            if (s.IsReading)
            {
                if (count < table.Count || count > MostTableEntries)
                {
                    throw new InvalidDataException($"saved world: a table of {count} entries");
                }
                GrowTo(table, count, () => new T());
            }
        }
        foreach (T entry in table)
        {
            entry.Exchange(s);
        }
    }
}
