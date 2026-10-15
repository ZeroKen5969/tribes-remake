using System;
using System.Collections.Generic;
using Tribes.Core.Content;
using Tribes.Core.World;

namespace Tribes.Client.World.Scene;

/// <summary>
/// What a <see cref="Scene.Viewpoint"/> sees of a world. Its tribes (<see cref="Includes"/>) are
/// the ones whose own things it knows: their traps, hidden units and markers. A player's are its
/// own tribe and, in a match, its allies; its fog is its own tribe's, which the rules already fill
/// with what the allies see and explored. A spectator's are every human-ruled tribe, and its fog
/// is theirs combined: what a computer tribe sees shows only where it is a human player's ally.
/// <see cref="Refresh"/> works the tribes out again from the world (alliances made or ended,
/// tribes joining) and keeps everything when nothing changed; the combined fog is made again only
/// where a tribe's fog changed.
/// </summary>
public sealed class ViewerSight : IFogView
{
    private readonly List<int> tribes = new();
    private readonly List<int> resolved = new();
    private bool[] members = Array.Empty<bool>();
    private CombinedFog fog;
    private readonly Dictionary<long, RememberedBuilding> rememberedCells = new();
    private BuildingMemory? indexedMemory;
    private long indexedRevision = -1;

    /// <summary>The sight of <paramref name="viewpoint"/> in <paramref name="world"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The home tribe is not one of the world's.</exception>
    public ViewerSight(GameWorld world, Viewpoint viewpoint)
    {
        World = world ?? throw new ArgumentNullException(nameof(world));
        if (viewpoint.Tribe < 0 || viewpoint.Tribe >= world.Tribes.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(viewpoint), viewpoint, $"the world has tribes 0..{world.Tribes.Count - 1}");
        }
        Viewpoint = viewpoint;
        Resolve(tribes);
        fog = MakeFog();
        Remember();
    }

    /// <summary>The sight of the player of <paramref name="tribe"/> in <paramref name="world"/>.</summary>
    public static ViewerSight Of(GameWorld world, int tribe) => new(world, Scene.Viewpoint.Player(tribe));

    /// <summary>The world seen.</summary>
    public GameWorld World { get; }

    /// <summary>Whose eyes these are.</summary>
    public Viewpoint Viewpoint { get; }

    /// <summary>The home tribe (the player's own, or where a spectator's screen starts).</summary>
    public int Home => Viewpoint.Tribe;

    /// <summary>Whether this is a spectator's sight.</summary>
    public bool Spectating => Viewpoint.Spectating;

    /// <summary>The tribes whose own things are known, in tribe order.</summary>
    public IReadOnlyList<int> Tribes => tribes;

    /// <summary>The tribes whose fog makes up what is shown, in tribe order.</summary>
    public IReadOnlyList<int> FogTribes => fog.Tribes;

    /// <summary>Grows whenever <see cref="Tribes"/> changes.</summary>
    public int Version { get; private set; }

    /// <summary>Whether <paramref name="tribe"/>'s traps, hidden units and the like are known.</summary>
    public bool Includes(int tribe) => (uint)tribe < (uint)members.Length && members[tribe];

    /// <summary>
    /// Whether <paramref name="tribe"/>'s orders and markers are drawn (gathering point, ordered traps,
    /// alarms, chief): the player's own, or for a spectator every human-ruled tribe's.
    /// </summary>
    public bool ShowsMarksOf(int tribe) => Spectating ? Includes(tribe) : tribe == Home;

    /// <summary>Whether <paramref name="tribe"/> is the player's own (a spectator has none).</summary>
    public bool IsOwn(int tribe) => !Spectating && tribe == Home;

    /// <inheritdoc/>
    public bool IsExplored(GridPoint cell) => fog.IsExplored(cell);

    /// <inheritdoc/>
    public bool IsVisible(GridPoint cell) => fog.IsVisible(cell);

    /// <inheritdoc/>
    public GridRect ExploredBounds => fog.ExploredBounds;

    /// <inheritdoc/>
    public bool AnyVisible(GridRect area) => fog.AnyVisible(area);

    /// <inheritdoc/>
    public BuildingMemory Buildings => fog.Buildings;

    /// <summary>
    /// Whether buildings out of sight are shown as last seen (a match); the campaign shows every
    /// building on explored land as it is, as the original does.
    /// </summary>
    public bool RemembersBuildings => World.Mode == WorldMode.Match;

    /// <summary>
    /// Whether <paramref name="building"/> is shown as it is now: always in the campaign; in a match
    /// while any cell of its footprint is in sight (out of sight the remembered look stands in for it).
    /// </summary>
    public bool ShowsAsItIs(Building building)
    {
        ArgumentNullException.ThrowIfNull(building);
        if (!RemembersBuildings)
        {
            return true;
        }
        var kinds = World.Content.Rules.Buildings.Kinds;
        int kind = Math.Clamp(building.Kind, 0, kinds.Count - 1);
        return fog.AnyVisible(new GridRect(building.X, building.Y, kinds[kind].Width, kinds[kind].Height));
    }

    /// <summary>
    /// The remembered building whose footprint holds cell (<paramref name="x"/>, <paramref name="y"/>),
    /// the one seen last when several do; false when none does (or in the campaign).
    /// </summary>
    public bool RememberedAt(int x, int y, out RememberedBuilding building)
    {
        building = default;
        if (!RemembersBuildings)
        {
            return false;
        }
        BuildingMemory memory = fog.Buildings;
        if (!ReferenceEquals(memory, indexedMemory) || memory.Revision != indexedRevision)
        {
            IndexRemembered(memory);
        }
        return rememberedCells.Count > 0 && rememberedCells.TryGetValue(CellKey(x, y), out building);
    }

    /// <summary>
    /// The building the viewer is shown in cell (<paramref name="x"/>, <paramref name="y"/>) out of
    /// sight in a match: the remembered one there, unless the place is in sight now.
    /// </summary>
    public bool ShowsRememberedAt(int x, int y, out RememberedBuilding building) =>
        RememberedAt(x, y, out building) && !fog.AnyVisible(building.Footprint(World.Content));

    private void IndexRemembered(BuildingMemory memory)
    {
        rememberedCells.Clear();
        foreach (RememberedBuilding seen in memory.Buildings)
        {
            GridRect cells = seen.Footprint(World.Content);
            for (int y = cells.Top; y < cells.Bottom; y++)
            {
                for (int x = cells.Left; x < cells.Right; x++)
                {
                    long key = CellKey(x, y);
                    if (!rememberedCells.TryGetValue(key, out RememberedBuilding other) || other.Frame < seen.Frame)
                    {
                        rememberedCells[key] = seen;
                    }
                }
            }
        }
        indexedMemory = memory;
        indexedRevision = memory.Revision;
    }

    private static long CellKey(int x, int y) => ((long)x << 32) ^ (uint)y;

    /// <summary>
    /// Works the tribes out again from the world; true (and <see cref="Version"/> grows) when they
    /// changed. Costs one pass over the tribes and alliances; the lists are made again only on a change.
    /// </summary>
    public bool Refresh()
    {
        resolved.Clear();
        Resolve(resolved);
        bool same = resolved.Count == tribes.Count && members.Length == World.Tribes.Count;
        for (int i = 0; same && i < resolved.Count; i++)
        {
            same = resolved[i] == tribes[i];
        }
        if (same)
        {
            return false;
        }
        tribes.Clear();
        tribes.AddRange(resolved);
        if (!SameFog())
        {
            fog = MakeFog();
        }
        Remember();
        Version++;
        return true;
    }

    /// <summary>The tribes of the viewpoint now: a spectator's every human-ruled tribe (or the home tribe if none); a player's own tribe and allies.</summary>
    private void Resolve(List<int> into)
    {
        IReadOnlyList<TribeState> all = World.Tribes;
        if (Spectating)
        {
            foreach (TribeState tribe in World.Tribes)
            {
                if (tribe.Controller.IsHumanRuled)
                {
                    into.Add(tribe.Index);
                }
            }
            if (into.Count == 0)
            {
                into.Add(Home);
            }
            return;
        }
        into.Add(Home);
        if (World.Mode != WorldMode.Match)
        {
            return;
        }
        int team = all[Home].Team;
        if (team >= 0)
        {
            foreach (TribeState tribe in World.Tribes)
            {
                if (tribe.Index != Home && tribe.Team == team)
                {
                    into.Add(tribe.Index);
                }
            }
        }
        foreach ((int low, int high) in World.Relations.Alliances)
        {
            int other = low == Home ? high : high == Home ? low : -1;
            if (other >= 0 && !into.Contains(other))
            {
                into.Add(other);
            }
        }
        into.Sort();
    }

    private bool SameFog()
    {
        IReadOnlyList<int> current = fog.Tribes;
        if (!Spectating)
        {
            return current.Count == 1 && current[0] == Home;
        }
        if (current.Count != tribes.Count)
        {
            return false;
        }
        for (int i = 0; i < current.Count; i++)
        {
            if (current[i] != tribes[i])
            {
                return false;
            }
        }
        return true;
    }

    private CombinedFog MakeFog() => new(World, Spectating ? tribes : [Home]);

    private void Remember()
    {
        if (members.Length != World.Tribes.Count)
        {
            members = new bool[World.Tribes.Count];
        }
        else
        {
            Array.Clear(members);
        }
        foreach (int tribe in tribes)
        {
            members[tribe] = true;
        }
    }
}
