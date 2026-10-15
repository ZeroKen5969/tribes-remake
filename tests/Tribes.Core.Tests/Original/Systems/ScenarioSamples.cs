using System.Collections.Generic;
using System.IO;
using System.Linq;
using Tribes.Core.Simulation;
using Tribes.Core.Systems.Missions;
using Tribes.Core.World;
using Tribes.Legacy;
using Tribes.Parity;
using Tribes.Trace;
using Xunit;

namespace Tribes.Core.Tests.Systems;

/// <summary>States and helpers the scenario parity tests share (mission start, story, computer player).</summary>
internal static class ScenarioSamples
{
    /// <summary>The frame of the menu state: the game has started, no mission has been played.</summary>
    private const int MenuFrame = 79;

    private static readonly object Gate = new();
    private static LegacySample? menu;

    /// <summary>The original at its first menu, before any mission was started (rules mode).</summary>
    public static LegacySample Menu
    {
        get
        {
            lock (Gate)
            {
                if (menu is null)
                {
                    string resources = Path.Combine(TestPaths.RepoRoot(), "original", "game", "res");
                    Program.Run(resources, MenuFrame, LegacySamples.Seeds[0], 80, (frame, game) =>
                    {
                        if (frame == MenuFrame)
                        {
                            menu = new LegacySample(LegacySamples.Seeds[0], frame, game, LegacySnapshot.Capture(game));
                        }
                    }, rulesMode: true);
                }
                return menu!;
            }
        }
    }

    /// <summary>Played states of every mission: the key-monkey games and the other missions played by the original.</summary>
    public static IEnumerable<LegacySample> Played(int everyNth) =>
        LegacySamples.All.Where((_, i) => i % everyNth == 0).Concat(MissionSamples.All.Where((_, i) => i % everyNth == 0));

    /// <summary>
    /// Runs <paramref name="original"/> on the restored sample and <paramref name="rewritten"/>, which
    /// makes a world of its own, and returns the differences between the two rule states.
    /// </summary>
    public static IReadOnlyList<string> CompareNewWorld(LegacySample sample, System.Action<GameCanvas> original,
        System.Func<GameCanvas, RuleSystems> rewritten)
    {
        GameCanvas game = sample.Restore();
        original(game);
        RuleState expected = LegacyRules.Capture(game);
        game = sample.Restore();
        RuleSystems systems = rewritten(game);
        IReadOnlyList<string> diff = expected.Differences(WorldRules.Capture(systems.World), 20, null);
        sample.Restore();
        return diff;
    }

    /// <summary>A new world playing <paramref name="mission"/> (easy, no opening scene), with its systems.</summary>
    public static RuleSystems Started(int mission, int difficulty = 0) =>
        MissionStart.Begin(LegacySamples.Content, mission, difficulty,
            new MissionOptions { ShowOpening = false });

    private static readonly Dictionary<LegacySample, int> SampleMissions = new();

    /// <summary>The mission the sample plays.</summary>
    public static int MissionOf(LegacySample sample)
    {
        lock (Gate)
        {
            if (!SampleMissions.TryGetValue(sample, out int mission))
            {
                mission = sample.Restore().b_q;
                SampleMissions[sample] = mission;
            }
            return mission;
        }
    }

    /// <summary>The missions each dialogue is played in (story steps, openings, unlocks, tutorial, and the scenes they lead into).</summary>
    public static Dictionary<int, HashSet<int>> DialogueMissions()
    {
        var owners = new Dictionary<int, HashSet<int>>();
        void Add(int dialogue, int mission)
        {
            if (!owners.TryGetValue(dialogue, out HashSet<int>? set))
            {
                owners[dialogue] = set = new HashSet<int>();
            }
            set.Add(mission);
        }
        var rules = LegacySamples.Content.Rules;
        foreach (var mission in rules.Missions.Missions)
        {
            foreach (var step in mission.StorySteps)
            {
                Add(step.Dialogue, mission.Id);
            }
            foreach (var unlock in mission.TechUnlocks)
            {
                Add(unlock.Dialogue, mission.Id);
            }
            if (mission.OpeningDialogue is int opening)
            {
                Add(opening, mission.Id);
            }
        }
        foreach (var step in rules.Story.TutorialSteps)
        {
            Add(step.Dialogue, 0);
            Add(step.Dialogue, 1);
        }
        foreach (int dialogue in new[] { 51, 53, 54 })
        {
            Add(dialogue, 4);
        }
        foreach (int dialogue in new[] { 60, 61, 62 })
        {
            Add(dialogue, 0);
            Add(dialogue, 1);
        }
        foreach (int lead in new[] { 0, 1, 2, 32, 36, 37, 39, 46, 47, 49, 55 })
        {
            foreach (int mission in owners.GetValueOrDefault(lead) ?? new HashSet<int>())
            {
                Add(lead + 1, mission);
            }
        }
        return owners;
    }

    /// <summary>Frames without fighting that let the story move on.</summary>
    private const int QuietFrames = 50;

    /// <summary>Sets the original's frame to <paramref name="frame"/> and makes the game quiet.</summary>
    public static void QuietOriginal(GameCanvas game, int frame)
    {
        game.i_c = frame;
        game.world_a.b_K = QuietFrames;
    }

    /// <summary>Sets the world's frame to <paramref name="frame"/> and makes the game quiet.</summary>
    public static void Quiet(GameWorld world, int frame)
    {
        world.Frame = frame;
        world.FramesSinceCombat = QuietFrames;
    }

    /// <summary>Puts <paramref name="actors"/> into the original's first actor slots (the rest emptied).</summary>
    public static void CastOriginal(GameCanvas game, int[] actors)
    {
        for (int i = 0; i < ScriptState.ActorSlots; i++)
        {
            game.bArr_p[i] = (sbyte)(i < actors.Length ? actors[i] : 0);
        }
    }

    /// <summary>Puts <paramref name="actors"/> into the world's first actor slots (the rest emptied).</summary>
    public static void Cast(GameWorld world, int[] actors)
    {
        for (int i = 0; i < ScriptState.ActorSlots; i++)
        {
            world.Script.SetActor(i, i < actors.Length ? actors[i] : 0);
        }
    }

    /// <summary>Fails with the differences when there are any.</summary>
    public static void AssertNoDifferences(IReadOnlyList<string> diff, string what) =>
        Assert.True(diff.Count == 0, what + ":\n  " + string.Join("\n  ", diff));
}
