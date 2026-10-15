using System;
using System.Collections.Generic;
using Tribes.Core.Content.Rules;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Map;

/// <summary>What an object group holds.</summary>
internal enum ObjectGroupKind
{
    /// <summary>The units of one tribe.</summary>
    Units,

    /// <summary>The buildings of one tribe.</summary>
    Buildings,

    /// <summary>The wild animals.</summary>
    Animals,
}

/// <summary>
/// The object groups searched for targets and threats (the sector index's categories). The
/// original's five come from the content: 0 and 1 the units of tribes 0 and 1, 2 and 3 their
/// buildings, 4 the animals; every further tribe t adds its units as group 5 + 2(t - 2) and its
/// buildings right after, made like the original's groups of tribe 0. A tribe's group holds the
/// tribe's slots (<see cref="GameWorld.UnitsOf"/>, <see cref="GameWorld.BuildingsOf"/>; for the
/// original's two tribes the content's slot ranges), entry e being the tribe's e-th slot; the
/// animals' group holds its range of animal slots.
/// </summary>
internal sealed class ObjectGroupTable
{
    /// <summary>The group of the animals.</summary>
    public const int AnimalGroup = 4;

    /// <summary>Tribes whose groups are the content's own.</summary>
    private const int OriginalTribes = 2;

    /// <summary>The first group of the tribes after the original two.</summary>
    private const int FirstAddedGroup = 5;

    private readonly GameWorld world;
    private readonly IReadOnlyList<ObjectGroup> original;
    private readonly List<ObjectGroup> groups = new();

    /// <summary>The groups of <paramref name="world"/>.</summary>
    public ObjectGroupTable(GameWorld world)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        original = world.Content.Rules.Targets.ObjectGroups;
        groups.AddRange(original);
    }

    /// <summary>Groups of the world now (it has more when tribes were added).</summary>
    public int Count
    {
        get
        {
            Extend();
            return groups.Count;
        }
    }

    /// <summary>Group <paramref name="group"/>.</summary>
    public ObjectGroup this[int group]
    {
        get
        {
            Extend();
            return groups[group];
        }
    }

    /// <summary>The slots of group <paramref name="group"/>, by entry (none for a tribe the world does not have).</summary>
    public GroupSlots Slots(int group) => TribeOf(group) >= world.Tribes.Count ? default : KindOf(group) switch
    {
        ObjectGroupKind.Units => new GroupSlots(world.UnitsOf(TribeOf(group)), null, 0, 0),
        ObjectGroupKind.Buildings => new GroupSlots(null, world.BuildingsOf(TribeOf(group)), 0, 0),
        _ => new GroupSlots(null, null, this[group].FirstIndex, this[group].LastIndex - this[group].FirstIndex + 1),
    };

    /// <summary>The group of <paramref name="tribe"/>'s units.</summary>
    public static int Units(int tribe) => tribe < OriginalTribes ? tribe : FirstAddedGroup + (2 * (tribe - OriginalTribes));

    /// <summary>The group of <paramref name="tribe"/>'s buildings.</summary>
    public static int Buildings(int tribe) =>
        tribe < OriginalTribes ? OriginalTribes + tribe : FirstAddedGroup + (2 * (tribe - OriginalTribes)) + 1;

    /// <summary>What group <paramref name="group"/> holds.</summary>
    public static ObjectGroupKind KindOf(int group) => group switch
    {
        < OriginalTribes => ObjectGroupKind.Units,
        < AnimalGroup => ObjectGroupKind.Buildings,
        AnimalGroup => ObjectGroupKind.Animals,
        _ => (group - FirstAddedGroup) % 2 == 0 ? ObjectGroupKind.Units : ObjectGroupKind.Buildings,
    };

    /// <summary>The tribe whose units or buildings group <paramref name="group"/> holds (-1 for the animals).</summary>
    public static int TribeOf(int group) => group switch
    {
        < OriginalTribes => group,
        < AnimalGroup => group - OriginalTribes,
        AnimalGroup => -1,
        _ => OriginalTribes + ((group - FirstAddedGroup) / 2),
    };

    private void Extend()
    {
        if (world.AnimalSlots - 1 > groups[AnimalGroup].LastIndex)
        {
            // An endless world adds animal slots: the animals' group covers them all.
            groups[AnimalGroup] = groups[AnimalGroup] with { LastIndex = world.AnimalSlots - 1 };
        }
        int wanted = GameWorld.ObjectGroupsFor(world.Tribes.Count);
        while (groups.Count < wanted)
        {
            int group = groups.Count;
            groups.Add((KindOf(group) == ObjectGroupKind.Units ? original[Units(0)] : original[Buildings(0)]) with { Id = group });
        }
    }
}

/// <summary>
/// The slots of an object group by entry: a tribe's unit or building slots, or a range of animal
/// slots. Entries only ever get added (a tribe's new slot is its last entry).
/// </summary>
internal readonly struct GroupSlots
{
    private readonly ListView<Unit>? units;
    private readonly ListView<Building>? buildings;
    private readonly int first, count;

    internal GroupSlots(ListView<Unit>? units, ListView<Building>? buildings, int first, int count)
    {
        (this.units, this.buildings, this.first, this.count) = (units, buildings, first, count);
    }

    /// <summary>Entries of the group.</summary>
    public int Count => units?.Count ?? buildings?.Count ?? count;

    /// <summary>The slot of entry <paramref name="entry"/>.</summary>
    public int this[int entry] => units != null ? units[entry].Id : buildings != null ? buildings[entry].Id : first + entry;
}
