using System;
using Tribes.Core.Content;
using Tribes.Core.Maps;
using Tribes.Core.Simulation;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Missions;

/// <summary>Choices a mission starts with.</summary>
public sealed record MissionOptions
{
    /// <summary>The seed of the rules' random generator (as <c>java.util.Random</c>'s seed; the original used 1).</summary>
    public long Seed { get; init; } = 1;

    /// <summary>When set, the generator's internal state to start from instead of <see cref="Seed"/>.</summary>
    public long? RandomState { get; init; }

    /// <summary>
    /// The tutorial flags: bit 0 the tutorial is on (it runs in the first two missions); bits 1..3
    /// the one-off tutorial scenes already seen (carried over from earlier missions).
    /// </summary>
    public int TutorialFlags { get; init; }

    /// <summary>Whether the player has hints switched on.</summary>
    public bool HintsEnabled { get; init; } = true;

    /// <summary>
    /// Whether the mission's opening dialogue is played; null: as in the campaign (the missions
    /// before the custom maps that have one).
    /// </summary>
    public bool? ShowOpening { get; init; }

    /// <summary>
    /// Whether the world has no limit on units and buildings and its tables grow as a match's (a
    /// single-player custom game); off: the original's 50 units and 30 buildings per tribe and its
    /// table sizes, as the campaign.
    /// </summary>
    public bool WithoutLimits { get; init; }
}

/// <summary>Starting a mission: a new world in the state the original starts the mission in.</summary>
public static class MissionStart
{
    /// <summary>The first mission of the custom maps; they have no opening dialogue.</summary>
    public const int FirstCustomMission = 7;

    /// <summary>The spawn gender mode a game starts with (keep the balance of men and women).</summary>
    private const int BalancedGenders = 2;

    /// <summary>
    /// A world playing mission <paramref name="missionId"/> at <paramref name="difficulty"/> (0 easy,
    /// 1 hard), with its rule systems. When the opening dialogue is played, its scene is the
    /// world's current scene (see <see cref="RuleSystems.CurrentSceneStep"/>); the world does not
    /// advance until it ends.
    /// </summary>
    public static RuleSystems Begin(GameContent content, int missionId, int difficulty, MissionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        options ??= new MissionOptions();
        if (missionId < 0 || missionId >= content.Missions.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(missionId), missionId, $"missions are 0..{content.Missions.Count - 1}");
        }
        MissionDefinition mission = content.Missions[missionId];
        GridSize size = content.Maps[mission.MapId].Size;
        var random = new GameRandom(options.Seed);
        if (options.RandomState is long state)
        {
            random.State = state;
        }
        var world = new GameWorld(content, new WorldMap(size, mission.PlayableSize), WorldLayout.Original, random)
        {
            Difficulty = difficulty,
            SpawnGenderMode = BalancedGenders,
        };
        if (options.WithoutLimits)
        {
            world.LiftLimits();
        }
        world.Script.TutorialFlags = options.TutorialFlags;
        world.Hints.Enabled = options.HintsEnabled;
        var systems = new RuleSystems(world);
        systems.MissionStarter.Start(missionId);
        int? opening = content.Rules.Missions.Missions[missionId].OpeningDialogue;
        if (opening is int dialogue && (options.ShowOpening ?? missionId < FirstCustomMission))
        {
            systems.PlayOpening(dialogue);
        }
        return systems;
    }
}
