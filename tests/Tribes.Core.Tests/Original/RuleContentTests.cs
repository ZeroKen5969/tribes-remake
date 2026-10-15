using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Tribes.Core.Content;
using Tribes.Core.Content.Rules;
using Tribes.Core.World;
using Xunit;

namespace Tribes.Core.Tests;

/// <summary>The rule files: saving and loading keeps everything, and broken tables are refused.</summary>
public sealed class RuleContentTests
{
    private static readonly string Root = TestPaths.RepoRoot();
    private static readonly GameRules Rules = OriginalTables.ExportedRules;

    [Fact]
    public void Save_ThenLoad_KeepsEveryRuleTable()
    {
        string directory = Path.Combine(Path.GetTempPath(), "tribes-rules-" + Guid.NewGuid());
        try
        {
            Rules.Save(directory);
            GameRules again = GameRules.Load(directory);

            Assert.Equal(Rules.ToJson(), again.ToJson());
            Assert.Equal(GameRules.FileNames.Order(), Directory.GetFiles(directory).Select(Path.GetFileName).Order());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void DataFiles_AreWhatSavingWrites()
    {
        foreach ((string file, string json) in Rules.ToJson())
        {
            Assert.Equal(json, File.ReadAllText(Path.Combine(Root, "data", "rules", file)));
        }
    }

    [Fact]
    public void GameContent_SavesAndLoadsItsRules()
    {
        GameContent content = GameContent.Load(Path.Combine(Root, "data"));
        string directory = Path.Combine(Path.GetTempPath(), "tribes-content-rules-" + Guid.NewGuid());
        try
        {
            content.Save(directory);
            Assert.Equal(content.Rules.ToJson(), GameContent.Load(directory).Rules.ToJson());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ContentWithoutRules_HasEmptyTables()
    {
        GameRules empty = GameRules.Empty;
        Assert.Empty(empty.Actions.Actions);
        Assert.Empty(empty.Missions.Missions);
        Assert.Empty(empty.Buildings.Kinds);
    }

    [Fact]
    public void Rules_ForAMissingMission_AreRefused()
    {
        GameContent content = GameContent.Load(Path.Combine(Root, "data"));
        IEnumerable<MissionDefinition> missions = content.Missions.Values.Where(m => m.Id != 11);
        Assert.Throws<InvalidDataException>(() => new GameContent(missions, content.Maps.Values, content.Terrain,
            content.ResourceNodeKinds.Values, content.Rules));
    }

    [Fact]
    public void MisnumberedTables_AreRefused()
    {
        var actions = Rules.Actions.Actions.ToList();
        actions[3] = actions[3] with { Id = 4 };
        AssertRefused(r => With(r, actions: r.Actions with { Actions = actions }), "actions.actions[3]");

        var kinds = Rules.Buildings.Kinds.ToList();
        kinds.RemoveAt(0);
        AssertRefused(r => With(r, buildings: new BuildingTables(kinds)), "buildings.kinds[0]");
    }

    [Fact]
    public void ReferencesOutsideTheirTables_AreRefused()
    {
        var actions = Rules.Actions.Actions.ToList();
        actions[10] = actions[10] with { NextActionOnComplete = 112 };
        AssertRefused(r => With(r, actions: r.Actions with { Actions = actions }), "nextActionOnComplete");

        actions = Rules.Actions.Actions.ToList();
        actions[28] = actions[28] with { PauseVariant = 2 };
        AssertRefused(r => With(r, actions: r.Actions with { Actions = actions }), "pauseVariant");

        var weapons = Rules.Weapons.Weapons.ToList();
        weapons[1] = weapons[1] with { ProjectileKind = 3 };
        AssertRefused(r => With(r, weapons: r.Weapons with { Weapons = weapons }), "projectileKind");

        var dialogues = Rules.Story.Dialogues.ToList();
        dialogues[69] = dialogues[69] with { PageCount = 1000 };
        AssertRefused(r => With(r, story: r.Story with { Dialogues = dialogues }), "story.dialogues[69]");

        var hints = Rules.Story.Hints.ToList();
        hints[0] = hints[0] with { Channel = 6 };
        AssertRefused(r => With(r, story: r.Story with { Hints = hints }), "story.hints[0]");
    }

    [Fact]
    public void InconsistentTables_AreRefused()
    {
        var kinds = Rules.Buildings.Kinds.ToList();
        ConstructionPlan plan = kinds[2].Construction!;
        kinds[2] = kinds[2] with { Construction = plan with { ProgressHitPoints = plan.ProgressHitPoints.Skip(1).ToArray() } };
        AssertRefused(r => With(r, buildings: new BuildingTables(kinds)), "buildings.kinds[2].construction");

        var missions = Rules.Missions.Missions.ToList();
        missions[2] = missions[2] with { Difficulties = missions[2].Difficulties.Take(1).ToArray() };
        AssertRefused(r => With(r, missions: r.Missions with { Missions = missions }), "difficulties");

        missions = Rules.Missions.Missions.ToList();
        missions[2] = missions[2] with { ActiveAnimalSpawnPoints = 10 };
        AssertRefused(r => With(r, missions: r.Missions with { Missions = missions }), "spawn points");
    }

    [Fact]
    public void MissingTables_AreRefused()
    {
        AssertRefused(r => With(r, skills: r.Skills with { TrainableStats = null! }), "skills.trainableStats");
        AssertRefused(r => With(r, movement: r.Movement with { CellDistances = null! }), "movement.cellDistances");
        AssertRefused(r => new GameRules(null!, r.Actions, r.Skills, r.Buildings, r.Weapons, r.Armoury, r.Animals,
            r.Animations, r.Targets, r.Units, r.Missions, r.Story, r.Interface), "movement.json");
    }

    [Fact]
    public void EmptyRuleFile_IsRefused()
    {
        string directory = Path.Combine(Path.GetTempPath(), "tribes-rules-empty-" + Guid.NewGuid());
        try
        {
            Rules.Save(directory);
            File.WriteAllText(Path.Combine(directory, "units.json"), "null");
            Exception error = Assert.Throws<InvalidDataException>(() => GameRules.Load(directory));
            Assert.Contains("units.json", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("\"targetAliveProperty\": \"UnitHitPoints\"", "\"targetAliveProperty\": 19")]
    [InlineData("\"id\": 0,", "\"id\": 0, \"bogus\": 1,")]
    [InlineData("\"actions\": [", "\"actions\": [[")]
    public void BadlyEditedRuleFiles_AreRefused(string text, string replacement)
    {
        string directory = Path.Combine(Path.GetTempPath(), "tribes-rules-edited-" + Guid.NewGuid());
        try
        {
            Rules.Save(directory);
            string path = Path.Combine(directory, "actions.json");
            string json = File.ReadAllText(path);
            int at = json.IndexOf(text, StringComparison.Ordinal);
            Assert.True(at >= 0, text + " not found");
            File.WriteAllText(path, json[..at] + replacement + json[(at + text.Length)..]);
            Exception error = Assert.Throws<InvalidDataException>(() => GameRules.Load(directory));
            Assert.Contains("actions.json", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void IncompleteRecords_AreRefused()
    {
        var actions = Rules.Actions.Actions.ToList();
        actions[3] = null!;
        AssertRefused(r => With(r, actions: r.Actions with { Actions = actions }), "actions.actions[3] is missing");

        actions = Rules.Actions.Actions.ToList();
        actions[5] = actions[5] with { TargetAliveProperty = (EntityProperty)500 };
        AssertRefused(r => With(r, actions: r.Actions with { Actions = actions }), "actions.actions[5].targetAliveProperty = 500");

        actions = Rules.Actions.Actions.ToList();
        actions[6] = actions[6] with { Range = default };
        AssertRefused(r => With(r, actions: r.Actions with { Actions = actions }), "actions.actions[6].range must hold exactly one");

        actions = Rules.Actions.Actions.ToList();
        actions[7] = actions[7] with { RateTarget = new RateTarget(EntityProperty.UnitX, TribeCounter.Wood) };
        AssertRefused(r => With(r, actions: r.Actions with { Actions = actions }), "actions.actions[7].rateTarget");

        var kinds = Rules.Buildings.Kinds.ToList();
        kinds[0] = kinds[0] with { WorkerSlots = new[] { 1, 2 } };
        AssertRefused(r => With(r, buildings: new BuildingTables(kinds)), "buildings.kinds[0].workerSlots has 2 entries");
    }

    [Fact]
    public void FurtherReferencesOutsideTheirTables_AreRefused()
    {
        var kinds = Rules.Buildings.Kinds.ToList();
        kinds[4] = kinds[4] with { Operation = kinds[4].Operation! with { ActionWhileBusy = 200 } };
        AssertRefused(r => With(r, buildings: new BuildingTables(kinds)), "buildings.kinds[4].operation.actionWhileBusy");

        var missions = Rules.Missions.Missions.ToList();
        missions[1] = missions[1] with { OpeningDialogue = 70 };
        AssertRefused(r => With(r, missions: r.Missions with { Missions = missions }), "missions.missions[1].openingDialogue");

        missions = Rules.Missions.Missions.ToList();
        missions[1] = missions[1] with { StartStock = missions[1].StartStock.Take(1).ToArray() };
        AssertRefused(r => With(r, missions: r.Missions with { Missions = missions }), "missions.missions[1].startStock");

        var pages = Rules.Story.Pages.ToList();
        pages[0] = pages[0] with { Scene = 28 };
        AssertRefused(r => With(r, story: r.Story with { Pages = pages }), "story.pages scene");
    }

    [Fact]
    public void ValueTypes_DescribeWhatTheyHold()
    {
        var rect = new CellRect(2, 3, 4, 5);
        Assert.True(rect.Contains(new GridPoint(2, 5)));
        Assert.True(rect.Contains(new GridPoint(4, 3)));
        Assert.False(rect.Contains(new GridPoint(5, 4)));
        Assert.False(rect.Contains(new GridPoint(3, 2)));
        Assert.Equal(new ValueSource(7, null), ValueSource.Of(7));
        Assert.Equal(new ValueSource(null, EntityProperty.UnitThrowRange), ValueSource.From(EntityProperty.UnitThrowRange));
    }

    private static void AssertRefused(Func<GameRules, GameRules> change, string expectedInMessage)
    {
        Exception error = Assert.Throws<InvalidDataException>(() => change(Rules));
        Assert.Contains(expectedInMessage, error.Message, StringComparison.Ordinal);
    }

    private static GameRules With(GameRules r, MovementTables? movement = null, ActionTables? actions = null,
        SkillTables? skills = null, BuildingTables? buildings = null, WeaponTables? weapons = null,
        MissionRuleTables? missions = null, StoryTables? story = null) =>
        new(movement ?? r.Movement, actions ?? r.Actions, skills ?? r.Skills, buildings ?? r.Buildings,
            weapons ?? r.Weapons, r.Armoury, r.Animals, r.Animations, r.Targets, r.Units, missions ?? r.Missions,
            story ?? r.Story, r.Interface);
}
