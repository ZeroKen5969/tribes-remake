using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Tribes.Core.Content.Rules;

/// <summary>
/// Checks that the rule tables are complete, numbered by position, of the expected sizes and
/// refer to entries that exist. Every failure is an <see cref="InvalidDataException"/> naming the
/// table path (as in the JSON files).
/// </summary>
internal static class RuleValidation
{
    private const int Levels = 3;
    private const int Materials = 3;
    private const int Entrances = 3;
    private const int Tribes = 2;
    private const int StockSlots = 11;

    public static void Check(GameRules rules)
    {
        RuleCompleteness.Check(rules);
        CheckNumbering(rules);
        CheckActions(rules);
        CheckBuildings(rules);
        CheckTargetsAndArmoury(rules);
        CheckMissions(rules);
        CheckStory(rules.Story);
    }

    private static void CheckNumbering(GameRules rules)
    {
        Numbered(rules.Movement.Directions, d => d.Facing, "movement.directions");
        Numbered(rules.Movement.LineSteps, s => s.Octant, "movement.lineSteps");
        Numbered(rules.Movement.SpeedClasses, s => s.Id, "movement.speedClasses");
        Numbered(rules.Movement.RevealDiscs, d => d.Radius, "movement.revealDiscs");
        Numbered(rules.Actions.Actions, x => x.Id, "actions.actions");
        Numbered(rules.Actions.Categories, x => x.Id, "actions.categories");
        Numbered(rules.Actions.PauseVariants, x => x.Id, "actions.pauseVariants");
        Numbered(rules.Skills.TrainableStats, x => x.Id, "skills.trainableStats");
        Numbered(rules.Skills.Skills, x => x.Id, "skills.skills");
        Numbered(rules.Buildings.Kinds, k => k.Id, "buildings.kinds");
        Numbered(rules.Weapons.Weapons, w => w.Id, "weapons.weapons");
        Numbered(rules.Weapons.Projectiles, p => p.Id, "weapons.projectiles");
        Numbered(rules.Armoury.Products, p => p.Id, "armoury.products");
        Numbered(rules.Armoury.Recipes, r => r.Id, "armoury.recipes");
        Numbered(rules.Armoury.Traps, t => t.Id, "armoury.traps");
        Numbered(rules.Animals.Kinds, k => k.Id, "animals.kinds");
        Numbered(rules.Animations.Animations, x => x.Id, "animations.animations");
        Numbered(rules.Targets.CellCategories, c => c.Id, "targets.cellCategories");
        Numbered(rules.Targets.ThreatResponses, t => t.Threat, "targets.threatResponses");
        Numbered(rules.Targets.ObjectGroups, g => g.Id, "targets.objectGroups");
        Numbered(rules.Units.Thoughts, t => t.Thought, "units.thoughts");
        Numbered(rules.Units.AgeSteps, s => s.Age, "units.ageSteps");
        Numbered(rules.Missions.Missions, m => m.Id, "missions.missions");
        Numbered(rules.Story.Scenes, s => s.Id, "story.scenes");
        Numbered(rules.Story.Poses, p => p.Id, "story.poses");
        Numbered(rules.Story.Dialogues, d => d.Id, "story.dialogues");
        Numbered(rules.Story.TutorialSteps, s => s.Id, "story.tutorialSteps");
        Numbered(rules.Story.Hints, h => h.Id, "story.hints");
        Numbered(rules.Interface.Panels, p => p.Id, "interface.panels");
    }

    private static void CheckActions(GameRules rules)
    {
        ActionTables a = rules.Actions;
        int actions = a.Actions.Count;
        foreach (ActionDefinition action in a.Actions)
        {
            string where = $"actions.actions[{action.Id}]";
            InRange(action.StandingTask, actions, where + ".standingTask");
            InRange(action.NextActionOnComplete, actions, where + ".nextActionOnComplete");
            InRange(action.Category, a.Categories.Count, where + ".category");
            InRange(action.TrainedStat, rules.Skills.TrainableStats.Count, where + ".trainedStat");
            if (action.PauseVariant != -1)
            {
                InRange(action.PauseVariant, a.PauseVariants.Count, where + ".pauseVariant");
            }
            ExactlyOne(action.EffectDelta.Constant.HasValue, action.EffectDelta.Property.HasValue, where + ".effectDelta");
            ExactlyOne(action.Range.Constant.HasValue, action.Range.Property.HasValue, where + ".range");
            ExactlyOne(action.RateTarget.Property.HasValue, action.RateTarget.Counter.HasValue, where + ".rateTarget");
            foreach (AnimationChoice choice in action.AnimationChoices)
            {
                InRange(choice.Animation, rules.Animations.Animations.Count, where + ".animationChoices animation");
            }
        }
        foreach (TrainingTarget target in rules.Skills.TrainingTargets)
        {
            InRange(target.Skill, rules.Skills.Skills.Count, "skills.trainingTargets skill");
            InRange(target.BuildingKind, rules.Buildings.Kinds.Count, "skills.trainingTargets buildingKind");
            InRange(target.Action, actions, "skills.trainingTargets action");
        }
    }

    private static void CheckBuildings(GameRules rules)
    {
        IReadOnlyList<BuildingKind> kinds = rules.Buildings.Kinds;
        int actions = rules.Actions.Actions.Count;
        foreach (BuildingKind kind in kinds)
        {
            string where = $"buildings.kinds[{kind.Id}]";
            Sized(kind.WorkerSlots, Levels, where + ".workerSlots");
            Sized(kind.MaxHitPoints, Levels, where + ".maxHitPoints");
            Sized(kind.BuildCost, Materials, where + ".buildCost");
            Sized(kind.UpgradeCost, Materials, where + ".upgradeCost");
            Sized(kind.InsideHappinessBonus, Levels, where + ".insideHappinessBonus");
            InRange(kind.SiteKind, kinds.Count, where + ".siteKind");
            if (kind.Operation is BuildingOperation op)
            {
                Sized(op.Entrances, Entrances, where + ".operation.entrances");
                Sized(op.TrainingEffects, Levels, where + ".operation.trainingEffects");
                InRange(op.UseAction, actions, where + ".operation.useAction");
                InRange(op.UseActionWhenOccupied, actions, where + ".operation.useActionWhenOccupied");
                InRange(op.ActionWhileBusy, actions, where + ".operation.actionWhileBusy");
                foreach (TrainingEffect effect in op.TrainingEffects.SelectMany(e => e))
                {
                    InRange(effect.Stat, rules.Skills.TrainableStats.Count, where + ".operation.trainingEffects stat");
                }
                foreach (BuildingSlot slot in op.Slots.Concat(op.SlotsWhileBusy))
                {
                    InRange(slot.IdleAction, actions, where + ".operation slot idleAction");
                    InRange(slot.WorkAction, actions, where + ".operation slot workAction");
                }
            }
            if (kind.Construction is ConstructionPlan plan)
            {
                if (plan.ProgressHitPoints.Count != plan.DeliveryActions.Count + 1)
                {
                    throw new InvalidDataException($"rules: {where}.construction needs one hit point limit per delivery and one more");
                }
                AllInRange(plan.DeliveryActions, actions, where + ".construction.deliveryActions");
            }
        }
    }

    private static void CheckTargetsAndArmoury(GameRules rules)
    {
        int actions = rules.Actions.Actions.Count;
        TargetTables t = rules.Targets;
        // Rules without tables (GameRules.Empty) still carry the fixed target action fields.
        if (actions > 0)
        {
            AllInRange(new[] { t.TargetActions.EmptyCell, t.TargetActions.OwnUnit, t.TargetActions.Animal, t.TargetActions.Other }
                .Concat(t.TargetActions.TerrainResource).Concat(t.TargetActions.EnemyAttack), actions, "targets.targetActions");
        }
        AllInRange(t.GatherActionByNodeKind, actions, "targets.gatherActionByNodeKind");
        AllInRange(t.DepositActionByCarriedItem, actions, "targets.depositActionByCarriedItem");
        AllInRange(t.ThreatResponses.Select(r => r.Action), actions, "targets.threatResponses action");
        AllInRange(rules.Armoury.SpellActions, actions, "armoury.spellActions");
        AllInRange(rules.Armoury.Products.Select(p => p.CraftAction), actions, "armoury.products craftAction");
        AllInRange(rules.Armoury.AutoEquipOrder.Select(c => c.Weapon), rules.Weapons.Weapons.Count, "armoury.autoEquipOrder weapon");
        foreach (WeaponKind weapon in rules.Weapons.Weapons.Where(w => w.ProjectileKind >= 0))
        {
            InRange(weapon.ProjectileKind, rules.Weapons.Projectiles.Count, $"weapons.weapons[{weapon.Id}].projectileKind");
        }
    }

    private static void CheckMissions(GameRules rules)
    {
        int actions = rules.Actions.Actions.Count;
        int dialogues = rules.Story.Dialogues.Count;
        int kinds = rules.Buildings.Kinds.Count;
        foreach (MissionRules mission in rules.Missions.Missions)
        {
            string where = $"missions.missions[{mission.Id}]";
            if (mission.Difficulties.Count != 2)
            {
                throw new InvalidDataException($"rules: {where}.difficulties must hold easy and hard");
            }
            if (mission.ActiveAnimalSpawnPoints > mission.AnimalSpawnPoints.Count)
            {
                throw new InvalidDataException($"rules: {where} has fewer spawn points than active ones");
            }
            Sized(mission.StartStock, Tribes, where + ".startStock");
            foreach (IReadOnlyList<CounterAmount> stock in mission.StartStock)
            {
                Sized(stock, StockSlots, where + ".startStock");
            }
            AllInRange(mission.Ai.Posts.Select(p => p.Action), actions, where + ".ai.posts action");
            AllInRange(mission.Ai.BuildPlan.Select(p => p.BuildingKind), kinds, where + ".ai.buildPlan buildingKind");
            AllInRange(mission.TechUnlocks.Select(u => u.BuildingKind), kinds, where + ".techUnlocks buildingKind");
            AllInRange(mission.TechUnlocks.Select(u => u.Dialogue), dialogues, where + ".techUnlocks dialogue");
            AllInRange(mission.StorySteps.Select(s => s.Dialogue), dialogues, where + ".storySteps dialogue");
            if (mission.OpeningDialogue is int opening)
            {
                InRange(opening, dialogues, where + ".openingDialogue");
            }
        }
    }

    private static void CheckStory(StoryTables story)
    {
        foreach (Dialogue dialogue in story.Dialogues)
        {
            if (dialogue.FirstPage < 0 || dialogue.PageCount < 0 || dialogue.FirstPage + dialogue.PageCount > story.Pages.Count)
            {
                throw new InvalidDataException($"rules: story.dialogues[{dialogue.Id}] pages are outside the page list");
            }
            InRange(dialogue.Cast, story.CastLists.Count, $"story.dialogues[{dialogue.Id}].cast");
        }
        foreach (Hint hint in story.Hints)
        {
            InRange(hint.Channel, story.HintChannelCooldowns.Count, $"story.hints[{hint.Id}].channel");
        }
        AllInRange(story.TutorialSteps.Select(s => s.Dialogue), story.Dialogues.Count, "story.tutorialSteps dialogue");
        AllInRange(story.Pages.Select(p => p.Scene), story.Scenes.Count, "story.pages scene");
    }

    private static void Numbered<T>(IReadOnlyList<T> items, Func<T, int> id, string where)
    {
        for (int i = 0; i < items.Count; i++)
        {
            int found = id(items[i]);
            if (found != i)
            {
                throw new InvalidDataException($"rules: {where}[{i}] is numbered {found}");
            }
        }
    }

    private static void Sized<T>(IReadOnlyCollection<T> items, int count, string where)
    {
        if (items.Count != count)
        {
            throw new InvalidDataException($"rules: {where} has {items.Count} entries, expected {count}");
        }
    }

    private static void ExactlyOne(bool first, bool second, string where)
    {
        if (first == second)
        {
            throw new InvalidDataException($"rules: {where} must hold exactly one of its two values");
        }
    }

    private static void AllInRange(IEnumerable<int> values, int count, string where)
    {
        foreach (int value in values)
        {
            InRange(value, count, where);
        }
    }

    private static void InRange(int value, int count, string where)
    {
        if (value < 0 || value >= count)
        {
            throw new InvalidDataException($"rules: {where} = {value} is outside 0..{count - 1}");
        }
    }
}

/// <summary>
/// Checks every member of every table: members that are not nullable are present, list entries
/// are present, and enum values are defined. JSON files edited by hand may leave members out,
/// which deserializing turns into nulls.
/// </summary>
internal sealed class RuleCompleteness
{
    private readonly NullabilityInfoContext context = new();
    private readonly Dictionary<Type, (PropertyInfo Property, NullabilityInfo Info)[]> members = new();

    private RuleCompleteness()
    {
    }

    public static void Check(GameRules rules) => new RuleCompleteness().Members(rules, typeof(GameRules), null);

    private void Members(object value, Type type, string? where)
    {
        if (!members.TryGetValue(type, out (PropertyInfo Property, NullabilityInfo Info)[]? list))
        {
            list = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(p => (p, context.Create(p)))
                .ToArray();
            members.Add(type, list);
        }
        foreach ((PropertyInfo property, NullabilityInfo info) in list)
        {
            string name = char.ToLowerInvariant(property.Name[0]) + property.Name[1..];
            Member(property.GetValue(value), info, property.PropertyType, where is null ? name : $"{where}.{name}");
        }
    }

    private void Member(object? value, NullabilityInfo info, Type type, string where)
    {
        if (value is null)
        {
            if (info.ReadState != NullabilityState.Nullable && Nullable.GetUnderlyingType(type) is null)
            {
                throw new InvalidDataException($"rules: {where} is missing");
            }
            return;
        }
        Type actual = Nullable.GetUnderlyingType(type) ?? type;
        if (actual.IsEnum)
        {
            if (!actual.IsDefined(typeof(FlagsAttribute), inherit: false) && !Enum.IsDefined(actual, value))
            {
                throw new InvalidDataException($"rules: {where} = {value} is not a {actual.Name}");
            }
            return;
        }
        if (value is IEnumerable items && actual.IsGenericType && actual.GetGenericTypeDefinition() == typeof(IReadOnlyList<>))
        {
            Type itemType = actual.GetGenericArguments()[0];
            NullabilityInfo itemInfo = info.GenericTypeArguments[0];
            int i = 0;
            foreach (object? item in items)
            {
                Member(item, itemInfo, itemType, $"{where}[{i++}]");
            }
            return;
        }
        if (actual.Namespace == typeof(GameRules).Namespace)
        {
            Members(value, actual, where);
        }
    }
}
