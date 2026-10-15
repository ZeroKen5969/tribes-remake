using Tribes.Core.Content;

namespace Tribes.Core.World;

/// <summary>
/// What someone knows of the map: one tribe's fog (<see cref="FogOfWar"/>) or several tribes' fog
/// seen as one (<see cref="CombinedFog"/>, what a spectator of their players is shown).
/// </summary>
public interface IFogView
{
    /// <summary>Whether <paramref name="cell"/> has ever been seen.</summary>
    bool IsExplored(GridPoint cell);

    /// <summary>Whether <paramref name="cell"/> is seen now.</summary>
    bool IsVisible(GridPoint cell);

    /// <summary>Whether any cell of <paramref name="area"/> is seen now.</summary>
    bool AnyVisible(GridRect area);

    /// <summary>The bounding box of the cells explored (empty when none is).</summary>
    GridRect ExploredBounds { get; }

    /// <summary>The buildings seen, each as last seen (kept in a match only).</summary>
    BuildingMemory Buildings { get; }
}
