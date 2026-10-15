using System;
using System.Collections.Generic;
using Tribes.Core.Content;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Match;

/// <summary>
/// The step of adding a tribe that finds where its camp goes (see <see cref="MatchStart.AddTribe"/>).
/// A world that can grow (endless) finds room by making more land, then looking again.
/// </summary>
public interface ICampFinder
{
    /// <summary>The origin (the chief's hut's cell) of a new camp in <paramref name="world"/>, or null when there is no room.</summary>
    GridPoint? FindCamp(GameWorld world);
}

/// <summary>
/// Room for a camp on the map as it is: open, empty ground in the largest walkable area, at least
/// <c>CampPlacer.MinimumDistance</c> cells from every tribe's camp and from where each tribe is now,
/// as far from the nearest as can be where the new camp has room to build and every camp keeps its
/// own (<c>CampRoom</c>).
/// </summary>
public sealed class MapCampFinder : ICampFinder
{
    private MapCampFinder()
    {
    }

    /// <summary>The finder.</summary>
    public static MapCampFinder Instance { get; } = new();

    /// <inheritdoc/>
    public GridPoint? FindCamp(GameWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        var places = new List<GridPoint>();
        var camps = new List<GridPoint>();
        foreach (TribeState tribe in world.Tribes)
        {
            camps.Add(tribe.Camp);
            places.Add(tribe.Camp);
            places.Add(TribeStanding.Seat(world, tribe.Index));
        }
        return new CampPlacer(world, MatchStart.Template(world)).ChooseWithRoom(camps, places);
    }
}
