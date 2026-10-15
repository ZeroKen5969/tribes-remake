using Tribes.Core.Systems.Endless;
using Tribes.Core.Systems.Match;
using Tribes.Core.World;

namespace Tribes.Core.Simulation;

/// <summary>The endless mode: land made as needed, camps, new tribes, wild animals and alliances.</summary>
public sealed partial class RuleSystems
{
    private LandKeeper? land;
    private CampSites? campSites;
    private EndlessCampFinder? endlessCampFinder;
    private TribeSpawner? tribeSpawner;
    private EndlessWildlife? endlessWildlife;
    private Alliances? alliances;
    private EndlessUpdate? endlessUpdate;

    /// <summary>The land of an endless world.</summary>
    internal LandKeeper Land => land ??= new LandKeeper(World, Raise, chunk => EndlessWildlife.OnChunkMade(chunk));

    /// <summary>Camp sites of an endless world.</summary>
    internal CampSites CampSites => campSites ??= new CampSites(World);

    /// <summary>The camp finder of an endless world (it makes land until a camp fits).</summary>
    internal EndlessCampFinder EndlessCampFinder => endlessCampFinder ??= new EndlessCampFinder(Land, CampSites);

    /// <summary>New computer tribes of an endless world.</summary>
    internal TribeSpawner TribeSpawner => tribeSpawner ??=
        new TribeSpawner(World, CampSites, (controller, finder) => MatchStart.AddTribe(this, controller, TribeState.NoTeam, finder), Raise);

    /// <summary>Wild animals of an endless world.</summary>
    internal EndlessWildlife EndlessWildlife => endlessWildlife ??= new EndlessWildlife(World, SpiralSearch, WildlifeAndNodes);

    /// <summary>Alliances made in an endless world.</summary>
    internal Alliances Alliances => alliances ??= new Alliances(World, UnitActions, Raise);

    /// <summary>The endless part of each frame.</summary>
    internal EndlessUpdate EndlessUpdate => endlessUpdate ??= new EndlessUpdate(World, Land, TribeSpawner, Alliances);
}
