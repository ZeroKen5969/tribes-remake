using System.Collections.Generic;
using System.Linq;
using Tribes.Core.Simulation;
using Tribes.Core.Systems.Story;
using Tribes.Core.World;
using Tribes.Legacy;
using Xunit;
using Xunit.Abstractions;

namespace Tribes.Core.Tests.Systems;

/// <summary>Parity of <see cref="ScenePlayer"/> (and the scene scripts it runs) with the original's story player.</summary>
public sealed class ScenePlayerTests
{
    /// <summary>Staged steps whose scene poses the actors and runs no frames: dialogue, with the pose on its first step.</summary>
    private static readonly int[] PosedDialogues = { 2, 33, 40, 50, 56 };

    private readonly ITestOutputHelper output;

    public ScenePlayerTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void Start_MatchesTheOriginalForEveryDialogue()
    {
        var run = new ParityRun();
        foreach ((int dialogue, LegacySample sample) in DialogueSamples(perDialogue: 3))
        {
            int[] actors = Actors(sample);
            run.Check(sample, $"Start {dialogue}", g =>
            {
                ScenarioSamples.CastOriginal(g, actors);
                g.b_I_m(dialogue);
            }, s =>
            {
                ScenarioSamples.Cast(s.World, actors);
                s.Scenes.Start(dialogue, chapterScreen: false);
            });
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 150);
    }

    [Fact]
    public void Continue_MatchesTheOriginalToTheSceneEnd()
    {
        var run = new ParityRun();
        foreach ((int dialogue, LegacySample sample) in DialogueSamples(perDialogue: 2))
        {
            int[] actors = Actors(sample);
            int steps = LegacySamples.Content.Rules.Story.Dialogues[dialogue].PageCount;
            int stage = sample.Restore().world_a.b_f;
            foreach (bool objective in stage < 6 ? new[] { false, true } : new[] { false })
            {
                run.Check(sample, $"Continue {dialogue} {objective}", g =>
                {
                    ScenarioSamples.CastOriginal(g, actors);
                    g.b_I_m(dialogue);
                    if (objective && g.world_a.b_f < 6)
                    {
                        g.world_a.b_f += 50;
                    }
                    for (int i = 0; i < steps; i++)
                    {
                        g.b_I_m(-1);
                    }
                }, s =>
                {
                    ScenarioSamples.Cast(s.World, actors);
                    ScenePlayer scenes = s.Scenes;
                    scenes.Start(dialogue, chapterScreen: false);
                    if (objective && s.World.Script.ObjectiveStage < 6)
                    {
                        s.World.Script.ObjectiveStage += 50;
                    }
                    for (int i = 0; i < steps; i++)
                    {
                        scenes.Continue();
                    }
                });
            }
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 200);
    }

    [Fact]
    public void AdvanceStep_PosesTheActorsAsTheOriginal()
    {
        var run = new ParityRun();
        foreach (int dialogue in PosedDialogues)
        {
            foreach (LegacySample sample in ScenarioSamples.Played(everyNth: 4))
            {
                int[] actors = Actors(sample);
                run.Check(sample, $"Pose {dialogue}", g =>
                {
                    ScenarioSamples.CastOriginal(g, actors);
                    g.b_I_m(dialogue);
                    g.i_aP = 0;
                    g.Q_();
                    return (g.i_aR, g.z_j);
                }, s =>
                {
                    ScenarioSamples.Cast(s.World, actors);
                    ScenePlayer scenes = s.Scenes;
                    scenes.Start(dialogue, chapterScreen: false);
                    SceneFrame frame = scenes.AdvanceStep();
                    return (s.World.Script.StepFrame, frame == SceneFrame.AutoContinue);
                });
            }
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 30);
    }

    [Fact]
    public void AdvanceStep_RunsAsManyWorldFramesAsTheOriginal()
    {
        LegacySample sample = ScenarioSamples.Played(everyNth: 5).First();
        foreach (int dialogue in new[] { 28, 34, 38, 44, 60 })
        {
            GameCanvas game = sample.Restore();
            game.b_I_m(dialogue);
            game.i_aP = 0;
            var original = new List<(int Counter, bool Advanced, bool Auto)>();
            for (int i = 0; i < 45; i++)
            {
                int frame = game.i_c;
                game.Q_();
                original.Add((game.i_aR, game.i_c != frame, game.z_j));
            }
            RuleSystems systems = new RuleSystems(Parity.LegacyRules.Import(sample.Restore(), LegacySamples.Content));
            systems.Scenes.Start(dialogue, chapterScreen: false);
            var rewritten = new List<(int Counter, bool Advanced, bool Auto)>();
            bool auto = false;
            for (int i = 0; i < 45; i++)
            {
                SceneFrame frame = systems.Scenes.AdvanceStep();
                auto |= frame == SceneFrame.AutoContinue;
                rewritten.Add((systems.World.Script.StepFrame, frame == SceneFrame.AdvanceWorld, auto));
            }
            Assert.Equal(original, rewritten);
            sample.Restore();
        }
    }

    [Fact]
    public void Scenes_ReportTheirStepsAndEnd()
    {
        RuleSystems systems = ScenarioSamples.Started(2);
        Assert.Null(systems.CurrentSceneStep);
        Assert.False(systems.CanContinueScene);
        Assert.Equal(SceneFrame.Hold, systems.AdvanceSceneStep());
        systems.ContinueScene();
        systems.Scenes.Start(35, chapterScreen: false);
        SceneStep step = systems.CurrentSceneStep!;
        Assert.Equal(35, step.Dialogue);
        Assert.Equal(0, step.Step);
        Assert.False(step.ChapterScreen);
        for (int i = 0; i < step.StepCount; i++)
        {
            systems.ContinueScene();
        }
        Assert.Null(systems.CurrentSceneStep);
        Assert.Contains(systems.TakeEvents(), e => e is SceneEnded { Dialogue: 35, ResumesPlay: true });
    }

    /// <summary>Every dialogue with some played states of the missions it belongs to.</summary>
    private static IEnumerable<(int Dialogue, LegacySample Sample)> DialogueSamples(int perDialogue)
    {
        Dictionary<int, HashSet<int>> missions = ScenarioSamples.DialogueMissions();
        List<LegacySample> played = ScenarioSamples.Played(everyNth: 1).ToList();
        for (int dialogue = 0; dialogue < LegacySamples.Content.Rules.Story.Dialogues.Count; dialogue++)
        {
            HashSet<int>? owners = missions.GetValueOrDefault(dialogue);
            IEnumerable<LegacySample> candidates = played.Where(s => owners == null || owners.Contains(ScenarioSamples.MissionOf(s)));
            List<LegacySample> chosen = candidates.ToList();
            foreach (LegacySample sample in SampleInputs.Spread(chosen.Count > 0 ? chosen : played, perDialogue))
            {
                yield return (dialogue, sample);
            }
        }
    }

    private static int[] Actors(LegacySample sample) => LegacySamples.LiveUnits(sample.Restore()).Take(12).ToArray();
}
