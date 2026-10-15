using System;
using Tribes.Core.Maps;
using Tribes.Core.Systems.Vision;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Missions;

/// <summary>
/// Starts a mission on a world: the world is cleared, the mission's map is built, its objects are
/// placed, the chief is named and the player's sight is worked out.
/// </summary>
internal sealed class MissionStarter
{
    /// <summary>The mission that starts without a chief (the tribe has lost its own).</summary>
    public const int ChieflessMission = 3;

    /// <summary>The unit that is the chief at the start of the other missions (the first one placed).</summary>
    public const int FirstChief = 1;

    private readonly GameWorld world;
    private readonly MissionReset reset;
    private readonly MissionSetup setup;
    private readonly SightSystem sight;

    /// <summary>Starts missions in <paramref name="world"/>.</summary>
    public MissionStarter(GameWorld world, MissionReset reset, MissionSetup setup, SightSystem sight)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        this.reset = reset ?? throw new ArgumentNullException(nameof(reset));
        this.setup = setup ?? throw new ArgumentNullException(nameof(setup));
        this.sight = sight ?? throw new ArgumentNullException(nameof(sight));
    }

    /// <summary>Starts mission <paramref name="mission"/> at the world's difficulty.</summary>
    public void Start(int mission)
    {
        world.Mission = mission;
        reset.Reset();
        BuildMap();
        setup.PlaceObjects();
        world.Tribes[TribeRoles.Player(world)][TribeCounter.Chief] = world.Mission == ChieflessMission ? 0 : FirstChief;
        sight.UpdateSight();
    }

    /// <summary>Replaces the world's map with the start map of the mission set in <see cref="GameWorld.Mission"/>.</summary>
    public void BuildMap() => world.Map = MapBuilder.Build(world.Mission, world.Content, world.Random);
}
