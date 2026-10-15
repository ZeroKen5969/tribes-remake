using System.Collections.Generic;
using System.IO;

namespace Tribes.Core.World;

/// <summary>
/// The unit and building slots. A slot belongs to one tribe for good and keeps its number, and each
/// tribe's slots are listed in slot order (<see cref="UnitsOf"/>, <see cref="BuildingsOf"/>). In the
/// campaign every tribe has the original's fixed slots (50 units and 30 buildings, numbered in
/// blocks); in a match (and a single-player custom game, see <see cref="LiftLimits"/>) a tribe whose
/// slots are all in use gets a new one after every slot there is, so a tribe can have any number of
/// units and buildings, and the world only holds the slots that were ever needed.
/// </summary>
public sealed partial class GameWorld
{
    /// <summary>More slots than this in a save mean it is damaged (far more than any world uses).</summary>
    private const int MostSlots = 1 << 22;

    private readonly List<List<Unit>> unitsByTribe = new();
    private readonly List<List<Building>> buildingsByTribe = new();
    private readonly List<ListView<Unit>> unitViews = new();
    private readonly List<ListView<Building>> buildingViews = new();
    private bool limitsLifted;

    /// <summary>
    /// Whether a tribe gets a new slot when all of its slots are in use, and the shared tables
    /// (construction sites, upgrades, traps, dust, couples) and each tribe's armoury queues and task
    /// orders grow when all of their entries are taken: in a match, and in a world whose limits were
    /// lifted (a single-player custom game). The campaign keeps the original's sizes.
    /// </summary>
    public bool GrowsOnDemand => Mode == WorldMode.Match || limitsLifted;

    /// <summary>
    /// From now on the world has no limit on units and buildings and its tables grow, as a match's
    /// (a single-player custom game on the original's two tribes). Every unit takes the match's
    /// couple links, which name any couple slot.
    /// </summary>
    internal void LiftLimits()
    {
        limitsLifted = true;
        WidenLinks();
    }

    /// <summary>Every unit takes the couple links that name any couple slot (a world whose tables grow).</summary>
    private void WidenLinks()
    {
        for (int id = 1; id < units.Count; id++)
        {
            units[id].WidenLinks();
        }
    }

    /// <summary>The most units a tribe can have at once, or null when there is no limit (<see cref="GrowsOnDemand"/>).</summary>
    public int? UnitLimit => GrowsOnDemand ? null : Layout.UnitsPerTribe;

    /// <summary>
    /// The most buildings (construction sites and ruins included) a tribe can have at once, or null
    /// when there is no limit (<see cref="GrowsOnDemand"/>).
    /// </summary>
    public int? BuildingLimit => GrowsOnDemand ? null : Layout.BuildingsPerTribe;

    /// <summary>
    /// The unit slots of <paramref name="tribe"/> in slot order, in use or free. Walking it with
    /// <c>foreach</c> allocates nothing; a loop whose body may bring a unit into the world walks it
    /// by index, since the tribe can get a new slot at the end.
    /// </summary>
    public ListView<Unit> UnitsOf(int tribe) => unitViews[tribe];

    /// <summary>The building slots of <paramref name="tribe"/> in slot order, in use or free (see <see cref="UnitsOf"/>).</summary>
    public ListView<Building> BuildingsOf(int tribe) => buildingViews[tribe];

    /// <summary>
    /// The first unit slot of <paramref name="tribe"/> (the unit the original treats as the tribe's
    /// leader), or 0 when the tribe has no unit slot yet.
    /// </summary>
    public int FirstUnitSlot(int tribe) => unitsByTribe[tribe].Count > 0 ? unitsByTribe[tribe][0].Id : 0;

    /// <summary>
    /// The first building slot of <paramref name="tribe"/> (the chief's hut of the original's camps),
    /// or 0 when the tribe has no building slot yet.
    /// </summary>
    public int FirstBuildingSlot(int tribe) => buildingsByTribe[tribe].Count > 0 ? buildingsByTribe[tribe][0].Id : 0;

    /// <summary>The tribe unit slot <paramref name="id"/> belongs to (1-based slot).</summary>
    public int TribeOfUnit(int id) => Unit(id).Tribe;

    /// <summary>The tribe building slot <paramref name="id"/> belongs to (1-based slot).</summary>
    public int TribeOfBuilding(int id) => Building(id).Tribe;

    /// <summary>
    /// The first free unit slot of <paramref name="tribe"/> in slot order; when all are in use, a new
    /// slot if the world grows (<see cref="GrowsOnDemand"/>), else null. A new slot nobody takes stays
    /// free for the next unit.
    /// </summary>
    internal Unit? FreeUnitSlot(int tribe)
    {
        foreach (Unit unit in unitsByTribe[tribe])
        {
            if (unit.HitPoints == 0)
            {
                return unit;
            }
        }
        return GrowsOnDemand ? AddUnitSlot(tribe) : null;
    }

    /// <summary>
    /// The first free building slot of <paramref name="tribe"/> in slot order; when all are in use, a
    /// new slot if the world grows, else null.
    /// </summary>
    internal Building? FreeBuildingSlot(int tribe)
    {
        foreach (Building building in buildingsByTribe[tribe])
        {
            if (building.HitPoints == 0)
            {
                return building;
            }
        }
        return GrowsOnDemand ? AddBuildingSlot(tribe) : null;
    }

    /// <summary>The first free dust cloud; when all are taken a new one if the world grows, else null.</summary>
    internal DustCloud? FreeDustCloud()
    {
        foreach (DustCloud cloud in dust)
        {
            if (cloud.Building == 0)
            {
                return cloud;
            }
        }
        return GrowsOnDemand ? Added(dust, new DustCloud()) : null;
    }

    /// <summary>The first free trap order slot; when all are taken a new one if the world grows, else -1.</summary>
    internal int FreeTrapOrderSlot()
    {
        for (int slot = 0; slot < traps.Count; slot++)
        {
            if (traps[slot].OwnerBuilding == 0)
            {
                return slot;
            }
        }
        if (!GrowsOnDemand)
        {
            return -1;
        }
        traps.Add(new TrapOrder());
        return traps.Count - 1;
    }

    /// <summary>The first free construction job; when all are taken a new one if the world grows, else null.</summary>
    internal ConstructionJob? FreeConstructionJob()
    {
        foreach (ConstructionJob job in construction)
        {
            if (job.Building < 1)
            {
                return job;
            }
        }
        return GrowsOnDemand ? Added(construction, new ConstructionJob()) : null;
    }

    /// <summary>The first free upgrade or repair job; when all are taken a new one if the world grows, else null.</summary>
    internal UpgradeJob? FreeUpgradeJob()
    {
        foreach (UpgradeJob job in upgrades)
        {
            if (job.Building < 1)
            {
                return job;
            }
        }
        return GrowsOnDemand ? Added(upgrades, new UpgradeJob()) : null;
    }

    /// <summary>The first free couple slot (state 0); when all are taken a new one if the world grows, else -1.</summary>
    internal int FreeCoupleSlot()
    {
        for (int slot = 0; slot < couples.Count; slot++)
        {
            if (couples[slot].State == 0)
            {
                return slot;
            }
        }
        if (!GrowsOnDemand)
        {
            return -1;
        }
        couples.Add(new Couple());
        return couples.Count - 1;
    }

    private static T Added<T>(List<T> table, T entry)
    {
        table.Add(entry);
        return entry;
    }

    /// <summary>Whether <paramref name="tribe"/> could place a building now (a free slot; always if the world grows).</summary>
    internal bool HasFreeBuildingSlot(int tribe)
    {
        if (GrowsOnDemand)
        {
            return true;
        }
        foreach (Building building in buildingsByTribe[tribe])
        {
            if (building.HitPoints == 0)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// The owner of every unit and building slot (format 5 on). Reading adds the slots the layout
    /// did not give the tribes, in slot order, and checks the ones it gave.
    /// </summary>
    private void ExchangeSlots(StateExchange s)
    {
        ExchangeOwners(s, ofUnits: true);
        ExchangeOwners(s, ofUnits: false);
    }

    private void ExchangeOwners(StateExchange s, bool ofUnits)
    {
        int slots = ofUnits ? UnitSlots : BuildingSlots;
        int count = s.Value(slots);
        if (s.IsReading && (count < slots || count > MostSlots))
        {
            throw new InvalidDataException($"saved world: {count} slots where the layout gives {slots}");
        }
        for (int id = 1; id <= count; id++)
        {
            int owner = s.Value(id <= slots ? OwnerOf(ofUnits, id) : 0);
            if (!s.IsReading || (id <= slots && owner == OwnerOf(ofUnits, id)))
            {
                continue;
            }
            if (id <= slots || owner < 0 || owner >= tribes.Count
                || (ofUnits ? AddUnitSlot(owner).Id : AddBuildingSlot(owner).Id) != id)
            {
                throw new InvalidDataException($"saved world: slot {id} of tribe {owner}");
            }
        }
    }

    private int OwnerOf(bool ofUnits, int id) => ofUnits ? units[id].Tribe : buildings[id].Tribe;

    /// <summary>
    /// A match loaded from a save of the original's slot blocks goes on with slots and tables that
    /// grow: tribes added later get no block, and every unit takes the match's couple links.
    /// </summary>
    private void GrowFromHere()
    {
        Layout = Layout with { UnitsPerTribe = 0, BuildingsPerTribe = 0 };
        WidenLinks();
    }

    /// <summary>The slot lists of new tribe <paramref name="tribe"/> and the slots <paramref name="layout"/> gives every tribe.</summary>
    private void AddSlotsOf(int tribe, WorldLayout layout)
    {
        unitsByTribe.Add(new List<Unit>());
        unitViews.Add(new ListView<Unit>(unitsByTribe[tribe]));
        buildingsByTribe.Add(new List<Building>());
        buildingViews.Add(new ListView<Building>(buildingsByTribe[tribe]));
        for (int i = 0; i < layout.UnitsPerTribe; i++)
        {
            AddUnitSlot(tribe);
        }
        for (int i = 0; i < layout.BuildingsPerTribe; i++)
        {
            AddBuildingSlot(tribe);
        }
    }

    /// <summary>A new free unit slot of <paramref name="tribe"/> after every slot there is.</summary>
    private Unit AddUnitSlot(int tribe)
    {
        var unit = new Unit(units.Count, tribe, wideLinks: Map.IsChunked || GrowsOnDemand) { SlotInTribe = unitsByTribe[tribe].Count };
        units.Add(unit);
        unitsByTribe[tribe].Add(unit);
        return unit;
    }

    /// <summary>A new free building slot of <paramref name="tribe"/> after every slot there is.</summary>
    private Building AddBuildingSlot(int tribe)
    {
        var building = new Building(buildings.Count, tribe) { SlotInTribe = buildingsByTribe[tribe].Count };
        buildings.Add(building);
        buildingsByTribe[tribe].Add(building);
        return building;
    }
}
