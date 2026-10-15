using System;
using System.Collections.Generic;
using Tribes.Core.Maps;

namespace Tribes.Core.World;

/// <summary>
/// A property of a unit, building, animal or resource node, as the content tables name one (for
/// example "the action works on the target's hit points"). The numbers are the original game's
/// field numbers, which the tables store: 0..82 unit fields, 83..98 building fields, 99..115 animal
/// fields, 116..120 resource node fields. <see cref="EntityProperties"/> reads and writes them on a
/// <see cref="GameWorld"/>.
/// </summary>
public enum EntityProperty
{
    /// <summary><see cref="Unit.X"/>.</summary>
    UnitX = 0,

    /// <summary><see cref="Unit.Y"/>.</summary>
    UnitY = 1,

    /// <summary><see cref="Unit.OffsetX"/>.</summary>
    UnitOffsetX = 2,

    /// <summary><see cref="Unit.OffsetY"/>.</summary>
    UnitOffsetY = 3,

    /// <summary><see cref="Unit.WorkProgress"/>.</summary>
    UnitWorkProgress = 4,

    /// <summary><see cref="Unit.Happiness"/>.</summary>
    UnitHappiness = 5,

    /// <summary><see cref="Unit.Facing"/>.</summary>
    UnitFacing = 6,

    /// <summary><see cref="Unit.PathIndex"/>.</summary>
    UnitPathIndex = 7,

    /// <summary><see cref="Unit.PathLength"/>.</summary>
    UnitPathLength = 8,

    /// <summary>The selection group the player put the unit in; user interface only, not held by the rules model.</summary>
    UnitSelectionGroup = 9,

    /// <summary><see cref="Unit.MoveDelay"/>.</summary>
    UnitMoveDelay = 10,

    /// <summary><see cref="Unit.StepAndActionTicks"/>.</summary>
    UnitStepAndActionTicks = 11,

    /// <summary>A field the original never uses; always 0.</summary>
    UnitUnused = 12,

    /// <summary><see cref="Unit.ActionSubState"/>.</summary>
    UnitActionSubState = 13,

    /// <summary><see cref="Unit.Age"/>.</summary>
    UnitAge = 14,

    /// <summary><see cref="Unit.Gender"/>.</summary>
    UnitGender = 15,

    /// <summary><see cref="Unit.RangedSkill"/>.</summary>
    UnitRangedSkill = 16,

    /// <summary><see cref="Unit.MeleeSkill"/>.</summary>
    UnitMeleeSkill = 17,

    /// <summary><see cref="Unit.ThrowRange"/>.</summary>
    UnitThrowRange = 18,

    /// <summary><see cref="Unit.HitPoints"/>.</summary>
    UnitHitPoints = 19,

    /// <summary><see cref="Unit.Satiety"/>.</summary>
    UnitSatiety = 20,

    /// <summary><see cref="Unit.Energy"/>.</summary>
    UnitEnergy = 21,

    /// <summary>Icon of the unit's task category; user interface only, not held by the rules model.</summary>
    UnitTaskIcon = 22,

    /// <summary>Icon of the unit's best civil skill; user interface only, not held by the rules model.</summary>
    UnitCivilSkillIcon1 = 23,

    /// <summary>Icon of the unit's second civil skill; user interface only, not held by the rules model.</summary>
    UnitCivilSkillIcon2 = 24,

    /// <summary>Icon of the unit's best combat skill; user interface only, not held by the rules model.</summary>
    UnitCombatSkillIcon1 = 25,

    /// <summary>Icon of the unit's second combat skill; user interface only, not held by the rules model.</summary>
    UnitCombatSkillIcon2 = 26,

    /// <summary><see cref="Unit.Portrait"/>.</summary>
    UnitPortrait = 27,

    /// <summary><see cref="Unit.Action"/>.</summary>
    UnitAction = 28,

    /// <summary><see cref="Unit.Animation"/>.</summary>
    UnitAnimation = 29,

    /// <summary><see cref="Unit.AnimationTick"/>.</summary>
    UnitAnimationTick = 30,

    /// <summary><see cref="Unit.DestinationX"/>.</summary>
    UnitDestinationX = 31,

    /// <summary><see cref="Unit.DestinationY"/>.</summary>
    UnitDestinationY = 32,

    /// <summary><see cref="Unit.Thought"/>.</summary>
    UnitThought = 33,

    /// <summary><see cref="Unit.Home"/>.</summary>
    UnitHome = 34,

    /// <summary><see cref="Unit.CarriedItem"/>.</summary>
    UnitCarriedItem = 35,

    /// <summary><see cref="Unit.CarriedAmount"/>.</summary>
    UnitCarriedAmount = 36,

    /// <summary><see cref="Unit.CarryCapacity"/>.</summary>
    UnitCarryCapacity = 37,

    /// <summary><see cref="Unit.EffectRecipient"/>.</summary>
    UnitEffectRecipient = 38,

    /// <summary><see cref="Unit.SavedAction"/>.</summary>
    UnitSavedAction = 39,

    /// <summary><see cref="Unit.EntranceFacingOrOrder"/>.</summary>
    UnitEntranceFacingOrOrder = 40,

    /// <summary>Always 0; tables use it where a property is required but nothing should count.</summary>
    UnitConstantZero = 41,

    /// <summary>Always 1; tables use it for a target that never disappears.</summary>
    UnitConstantOne = 42,

    /// <summary>Always -1.</summary>
    UnitConstantMinusOne = 43,

    /// <summary><see cref="Unit.TargetIndex"/>.</summary>
    UnitTargetIndex = 44,

    /// <summary><see cref="Unit.NextOccupantOrCellObject"/>.</summary>
    UnitNextOccupantOrCellObject = 45,

    /// <summary><see cref="Unit.BuildingSlot"/>.</summary>
    UnitBuildingSlot = 46,

    /// <summary><see cref="Unit.TargetCellCode"/>.</summary>
    UnitTargetCellCode = 47,

    /// <summary><see cref="Unit.PathBlock"/>.</summary>
    UnitPathBlock = 48,

    /// <summary><see cref="Unit.SightRadius"/>.</summary>
    UnitSightRadius = 49,

    /// <summary><see cref="Unit.IdleTicks"/>.</summary>
    UnitIdleTicks = 50,

    /// <summary><see cref="Unit.Appearance"/>.</summary>
    UnitAppearance = 51,

    /// <summary><see cref="Unit.ProjectileX"/>.</summary>
    UnitProjectileX = 52,

    /// <summary><see cref="Unit.ProjectileY"/>.</summary>
    UnitProjectileY = 53,

    /// <summary><see cref="Unit.ProjectileSubX"/>.</summary>
    UnitProjectileSubX = 54,

    /// <summary><see cref="Unit.ProjectileSubY"/>.</summary>
    UnitProjectileSubY = 55,

    /// <summary><see cref="Unit.ProjectileVelocityX"/>.</summary>
    UnitProjectileVelocityX = 56,

    /// <summary><see cref="Unit.ProjectileVelocityY"/>.</summary>
    UnitProjectileVelocityY = 57,

    /// <summary><see cref="Unit.ProjectileStep"/>.</summary>
    UnitProjectileStep = 58,

    /// <summary><see cref="Unit.ProjectileFlightSteps"/>.</summary>
    UnitProjectileFlightSteps = 59,

    /// <summary>Next projectile on the same screen cell, for drawing; not held by the rules model.</summary>
    UnitProjectileDrawNext = 60,

    /// <summary><see cref="Unit.OrderMode"/>.</summary>
    UnitOrderMode = 61,

    /// <summary><see cref="Unit.TaskSiteX"/>.</summary>
    UnitTaskSiteX = 62,

    /// <summary><see cref="Unit.TaskSiteY"/>.</summary>
    UnitTaskSiteY = 63,

    /// <summary><see cref="Unit.AssignedTask"/>.</summary>
    UnitAssignedTask = 64,

    /// <summary><see cref="Unit.Weapon"/>.</summary>
    UnitWeapon = 65,

    /// <summary><see cref="Unit.AgeTicks"/>.</summary>
    UnitAgeTicks = 66,

    /// <summary><see cref="Unit.MaxHitPoints"/>.</summary>
    UnitMaxHitPoints = 67,

    /// <summary><see cref="Unit.NameIndex"/>.</summary>
    UnitNameIndex = 68,

    /// <summary><see cref="Unit.HarvestingSkill"/>.</summary>
    UnitHarvestingSkill = 69,

    /// <summary><see cref="Unit.WeaponsmithingSkill"/>.</summary>
    UnitWeaponsmithingSkill = 70,

    /// <summary><see cref="Unit.CookingSkill"/>.</summary>
    UnitCookingSkill = 71,

    /// <summary><see cref="Unit.DjingSkill"/>.</summary>
    UnitDjingSkill = 72,

    /// <summary><see cref="Unit.WitchcraftSkill"/>.</summary>
    UnitWitchcraftSkill = 73,

    /// <summary><see cref="Unit.BuildingSkill"/>.</summary>
    UnitBuildingSkill = 74,

    /// <summary><see cref="Unit.ScoutingSkill"/>.</summary>
    UnitScoutingSkill = 75,

    /// <summary><see cref="Unit.ProjectileFacing"/>.</summary>
    UnitProjectileFacing = 76,

    /// <summary><see cref="Unit.RunBonus"/>.</summary>
    UnitRunBonus = 77,

    /// <summary><see cref="Unit.ProjectileDamageFloor"/>.</summary>
    UnitProjectileDamageFloor = 78,

    /// <summary><see cref="Unit.Status"/> (as the signed byte the original stores).</summary>
    UnitStatus = 79,

    /// <summary><see cref="Unit.DiseaseExposure"/>.</summary>
    UnitDiseaseExposure = 80,

    /// <summary><see cref="Unit.ProtectionTicks"/>.</summary>
    UnitProtectionTicks = 81,

    /// <summary><see cref="Unit.PairLink"/>.</summary>
    UnitPairLink = 82,

    /// <summary><see cref="Building.X"/>.</summary>
    BuildingX = 83,

    /// <summary><see cref="Building.Y"/>.</summary>
    BuildingY = 84,

    /// <summary><see cref="Building.HitPoints"/>.</summary>
    BuildingHitPoints = 85,

    /// <summary><see cref="Building.MaxHitPoints"/>.</summary>
    BuildingMaxHitPoints = 86,

    /// <summary><see cref="Building.WorkMode"/>.</summary>
    BuildingWorkMode = 87,

    /// <summary><see cref="Building.RangeInfo"/>.</summary>
    BuildingRangeInfo = 88,

    /// <summary><see cref="Building.Kind"/>.</summary>
    BuildingKind = 89,

    /// <summary><see cref="Building.OperatorOrBeds"/>.</summary>
    BuildingOperatorOrBeds = 90,

    /// <summary><see cref="Building.Flags"/>.</summary>
    BuildingFlags = 91,

    /// <summary><see cref="Building.OccupantCount"/>.</summary>
    BuildingOccupantCount = 92,

    /// <summary><see cref="Building.FirstOccupant"/>.</summary>
    BuildingFirstOccupant = 93,

    /// <summary><see cref="Building.AlertFlags"/>.</summary>
    BuildingAlertFlags = 94,

    /// <summary><see cref="Building.Level"/>.</summary>
    BuildingLevel = 95,

    /// <summary><see cref="Building.Visibility"/>.</summary>
    BuildingVisibility = 96,

    /// <summary><see cref="Building.Aux"/>.</summary>
    BuildingAux = 97,

    /// <summary><see cref="Building.DispatchedWorkers"/>.</summary>
    BuildingDispatchedWorkers = 98,

    /// <summary><see cref="Animal.X"/>.</summary>
    AnimalX = 99,

    /// <summary><see cref="Animal.Y"/>.</summary>
    AnimalY = 100,

    /// <summary><see cref="Animal.OffsetX"/>.</summary>
    AnimalOffsetX = 101,

    /// <summary><see cref="Animal.OffsetY"/>.</summary>
    AnimalOffsetY = 102,

    /// <summary><see cref="Animal.ProvokeTimer"/>.</summary>
    AnimalProvokeTimer = 103,

    /// <summary><see cref="Animal.SpawnZone"/>.</summary>
    AnimalSpawnZone = 104,

    /// <summary><see cref="Animal.Facing"/>.</summary>
    AnimalFacing = 105,

    /// <summary><see cref="Animal.HitPoints"/>.</summary>
    AnimalHitPoints = 106,

    /// <summary><see cref="Animal.Kind"/>.</summary>
    AnimalKind = 107,

    /// <summary><see cref="Animal.Action"/>.</summary>
    AnimalAction = 108,

    /// <summary><see cref="Animal.ActionFrame"/>.</summary>
    AnimalActionFrame = 109,

    /// <summary><see cref="Animal.Target"/>.</summary>
    AnimalTarget = 110,

    /// <summary><see cref="Animal.StepCounter"/>.</summary>
    AnimalStepCounter = 111,

    /// <summary><see cref="Animal.DestinationX"/>.</summary>
    AnimalDestinationX = 112,

    /// <summary><see cref="Animal.DestinationY"/>.</summary>
    AnimalDestinationY = 113,

    /// <summary><see cref="Animal.PathLength"/>.</summary>
    AnimalPathLength = 114,

    /// <summary><see cref="Animal.PathIndex"/>.</summary>
    AnimalPathIndex = 115,

    /// <summary><see cref="ResourceNode.X"/>.</summary>
    NodeX = 116,

    /// <summary><see cref="ResourceNode.Y"/>.</summary>
    NodeY = 117,

    /// <summary><see cref="ResourceNode.Kind"/>.</summary>
    NodeKind = 118,

    /// <summary><see cref="ResourceNode.Amount"/>.</summary>
    NodeAmount = 119,

    /// <summary><see cref="ResourceNode.Stage"/>.</summary>
    NodeStage = 120,
}

/// <summary>The kind of entity an <see cref="EntityProperty"/> belongs to.</summary>
public enum EntityKind
{
    /// <summary>A unit (<see cref="GameWorld.Unit"/>, slots from 1).</summary>
    Unit,

    /// <summary>A building (<see cref="GameWorld.Building"/>, slots from 1).</summary>
    Building,

    /// <summary>An animal (<see cref="GameWorld.Animal"/>, slots from 0).</summary>
    Animal,

    /// <summary>A resource node (<see cref="WorldMap.ResourceNodes"/>, slots from 0).</summary>
    ResourceNode,
}

/// <summary>
/// Reads and writes an <see cref="EntityProperty"/> of one entity of a <see cref="GameWorld"/>.
/// The index is the entity's slot: unit and building slots start at 1, animal and resource node
/// slots at 0. Constant properties (<see cref="EntityProperty.UnitConstantZero"/> and the like)
/// read as their constant and ignore writes. Properties only the user interface keeps
/// (<see cref="IsHeldByModel"/> is false) cannot be read or written.
/// </summary>
public static class EntityProperties
{
    private const int FirstBuildingProperty = (int)EntityProperty.BuildingX;
    private const int FirstAnimalProperty = (int)EntityProperty.AnimalX;
    private const int FirstNodeProperty = (int)EntityProperty.NodeX;

    private static readonly Dictionary<EntityProperty, int> Constants = new()
    {
        [EntityProperty.UnitUnused] = 0,
        [EntityProperty.UnitConstantZero] = 0,
        [EntityProperty.UnitConstantOne] = 1,
        [EntityProperty.UnitConstantMinusOne] = -1,
    };

    private static readonly Dictionary<EntityProperty, Accessor<Unit>> UnitAccessors = new()
    {
        [EntityProperty.UnitX] = new(u => u.X, (u, v) => u.X = v),
        [EntityProperty.UnitY] = new(u => u.Y, (u, v) => u.Y = v),
        [EntityProperty.UnitOffsetX] = new(u => u.OffsetX, (u, v) => u.OffsetX = v),
        [EntityProperty.UnitOffsetY] = new(u => u.OffsetY, (u, v) => u.OffsetY = v),
        [EntityProperty.UnitWorkProgress] = new(u => u.WorkProgress, (u, v) => u.WorkProgress = v),
        [EntityProperty.UnitHappiness] = new(u => u.Happiness, (u, v) => u.Happiness = v),
        [EntityProperty.UnitFacing] = new(u => u.Facing, (u, v) => u.Facing = v),
        [EntityProperty.UnitPathIndex] = new(u => u.PathIndex, (u, v) => u.PathIndex = v),
        [EntityProperty.UnitPathLength] = new(u => u.PathLength, (u, v) => u.PathLength = v),
        [EntityProperty.UnitMoveDelay] = new(u => u.MoveDelay, (u, v) => u.MoveDelay = v),
        [EntityProperty.UnitStepAndActionTicks] = new(u => u.StepAndActionTicks, (u, v) => u.StepAndActionTicks = v),
        [EntityProperty.UnitActionSubState] = new(u => u.ActionSubState, (u, v) => u.ActionSubState = v),
        [EntityProperty.UnitAge] = new(u => u.Age, (u, v) => u.Age = v),
        [EntityProperty.UnitGender] = new(u => u.Gender, (u, v) => u.Gender = v),
        [EntityProperty.UnitRangedSkill] = new(u => u.RangedSkill, (u, v) => u.RangedSkill = v),
        [EntityProperty.UnitMeleeSkill] = new(u => u.MeleeSkill, (u, v) => u.MeleeSkill = v),
        [EntityProperty.UnitThrowRange] = new(u => u.ThrowRange, (u, v) => u.ThrowRange = v),
        [EntityProperty.UnitHitPoints] = new(u => u.HitPoints, (u, v) => u.HitPoints = v),
        [EntityProperty.UnitSatiety] = new(u => u.Satiety, (u, v) => u.Satiety = v),
        [EntityProperty.UnitEnergy] = new(u => u.Energy, (u, v) => u.Energy = v),
        [EntityProperty.UnitPortrait] = new(u => u.Portrait, (u, v) => u.Portrait = v),
        [EntityProperty.UnitAction] = new(u => u.Action, (u, v) => u.Action = v),
        [EntityProperty.UnitAnimation] = new(u => u.Animation, (u, v) => u.Animation = v),
        [EntityProperty.UnitAnimationTick] = new(u => u.AnimationTick, (u, v) => u.AnimationTick = v),
        [EntityProperty.UnitDestinationX] = new(u => u.DestinationX, (u, v) => u.DestinationX = v),
        [EntityProperty.UnitDestinationY] = new(u => u.DestinationY, (u, v) => u.DestinationY = v),
        [EntityProperty.UnitThought] = new(u => u.Thought, (u, v) => u.Thought = v),
        [EntityProperty.UnitHome] = new(u => u.Home, (u, v) => u.Home = v),
        [EntityProperty.UnitCarriedItem] = new(u => u.CarriedItem, (u, v) => u.CarriedItem = v),
        [EntityProperty.UnitCarriedAmount] = new(u => u.CarriedAmount, (u, v) => u.CarriedAmount = v),
        [EntityProperty.UnitCarryCapacity] = new(u => u.CarryCapacity, (u, v) => u.CarryCapacity = v),
        [EntityProperty.UnitEffectRecipient] = new(u => u.EffectRecipient, (u, v) => u.EffectRecipient = v),
        [EntityProperty.UnitSavedAction] = new(u => u.SavedAction, (u, v) => u.SavedAction = v),
        [EntityProperty.UnitEntranceFacingOrOrder] = new(u => u.EntranceFacingOrOrder, (u, v) => u.EntranceFacingOrOrder = v),
        [EntityProperty.UnitTargetIndex] = new(u => u.TargetIndex, (u, v) => u.TargetIndex = v),
        [EntityProperty.UnitNextOccupantOrCellObject] = new(u => u.NextOccupantOrCellObject, (u, v) => u.NextOccupantOrCellObject = v),
        [EntityProperty.UnitBuildingSlot] = new(u => u.BuildingSlot, (u, v) => u.BuildingSlot = v),
        [EntityProperty.UnitTargetCellCode] = new(u => u.TargetCellCode, (u, v) => u.TargetCellCode = v),
        [EntityProperty.UnitPathBlock] = new(u => u.PathBlock, (u, v) => u.PathBlock = v),
        [EntityProperty.UnitSightRadius] = new(u => u.SightRadius, (u, v) => u.SightRadius = v),
        [EntityProperty.UnitIdleTicks] = new(u => u.IdleTicks, (u, v) => u.IdleTicks = v),
        [EntityProperty.UnitAppearance] = new(u => u.Appearance, (u, v) => u.Appearance = v),
        [EntityProperty.UnitProjectileX] = new(u => u.ProjectileX, (u, v) => u.ProjectileX = v),
        [EntityProperty.UnitProjectileY] = new(u => u.ProjectileY, (u, v) => u.ProjectileY = v),
        [EntityProperty.UnitProjectileSubX] = new(u => u.ProjectileSubX, (u, v) => u.ProjectileSubX = v),
        [EntityProperty.UnitProjectileSubY] = new(u => u.ProjectileSubY, (u, v) => u.ProjectileSubY = v),
        [EntityProperty.UnitProjectileVelocityX] = new(u => u.ProjectileVelocityX, (u, v) => u.ProjectileVelocityX = v),
        [EntityProperty.UnitProjectileVelocityY] = new(u => u.ProjectileVelocityY, (u, v) => u.ProjectileVelocityY = v),
        [EntityProperty.UnitProjectileStep] = new(u => u.ProjectileStep, (u, v) => u.ProjectileStep = v),
        [EntityProperty.UnitProjectileFlightSteps] = new(u => u.ProjectileFlightSteps, (u, v) => u.ProjectileFlightSteps = v),
        [EntityProperty.UnitOrderMode] = new(u => u.OrderMode, (u, v) => u.OrderMode = v),
        [EntityProperty.UnitTaskSiteX] = new(u => u.TaskSiteX, (u, v) => u.TaskSiteX = v),
        [EntityProperty.UnitTaskSiteY] = new(u => u.TaskSiteY, (u, v) => u.TaskSiteY = v),
        [EntityProperty.UnitAssignedTask] = new(u => u.AssignedTask, (u, v) => u.AssignedTask = v),
        [EntityProperty.UnitWeapon] = new(u => u.Weapon, (u, v) => u.Weapon = v),
        [EntityProperty.UnitAgeTicks] = new(u => u.AgeTicks, (u, v) => u.AgeTicks = v),
        [EntityProperty.UnitMaxHitPoints] = new(u => u.MaxHitPoints, (u, v) => u.MaxHitPoints = v),
        [EntityProperty.UnitNameIndex] = new(u => u.NameIndex, (u, v) => u.NameIndex = v),
        [EntityProperty.UnitHarvestingSkill] = new(u => u.HarvestingSkill, (u, v) => u.HarvestingSkill = v),
        [EntityProperty.UnitWeaponsmithingSkill] = new(u => u.WeaponsmithingSkill, (u, v) => u.WeaponsmithingSkill = v),
        [EntityProperty.UnitCookingSkill] = new(u => u.CookingSkill, (u, v) => u.CookingSkill = v),
        [EntityProperty.UnitDjingSkill] = new(u => u.DjingSkill, (u, v) => u.DjingSkill = v),
        [EntityProperty.UnitWitchcraftSkill] = new(u => u.WitchcraftSkill, (u, v) => u.WitchcraftSkill = v),
        [EntityProperty.UnitBuildingSkill] = new(u => u.BuildingSkill, (u, v) => u.BuildingSkill = v),
        [EntityProperty.UnitScoutingSkill] = new(u => u.ScoutingSkill, (u, v) => u.ScoutingSkill = v),
        [EntityProperty.UnitProjectileFacing] = new(u => u.ProjectileFacing, (u, v) => u.ProjectileFacing = v),
        [EntityProperty.UnitRunBonus] = new(u => u.RunBonus, (u, v) => u.RunBonus = v),
        [EntityProperty.UnitProjectileDamageFloor] = new(u => u.ProjectileDamageFloor, (u, v) => u.ProjectileDamageFloor = v),
        [EntityProperty.UnitStatus] = new(u => Wrap.Signed8((int)u.Status), (u, v) => u.Status = (UnitStatus)(v & 0xFF)),
        [EntityProperty.UnitDiseaseExposure] = new(u => u.DiseaseExposure, (u, v) => u.DiseaseExposure = v),
        [EntityProperty.UnitProtectionTicks] = new(u => u.ProtectionTicks, (u, v) => u.ProtectionTicks = v),
        [EntityProperty.UnitPairLink] = new(u => u.PairLink, (u, v) => u.PairLink = v),
    };

    private static readonly Dictionary<EntityProperty, Accessor<Building>> BuildingAccessors = new()
    {
        [EntityProperty.BuildingX] = new(b => b.X, (b, v) => b.X = v),
        [EntityProperty.BuildingY] = new(b => b.Y, (b, v) => b.Y = v),
        [EntityProperty.BuildingHitPoints] = new(b => b.HitPoints, (b, v) => b.HitPoints = v),
        [EntityProperty.BuildingMaxHitPoints] = new(b => b.MaxHitPoints, (b, v) => b.MaxHitPoints = v),
        [EntityProperty.BuildingWorkMode] = new(b => (int)b.WorkMode, (b, v) => b.WorkMode = (BuildingWorkMode)v),
        [EntityProperty.BuildingRangeInfo] = new(b => b.RangeInfo, (b, v) => b.RangeInfo = v),
        [EntityProperty.BuildingKind] = new(b => b.Kind, (b, v) => b.Kind = v),
        [EntityProperty.BuildingOperatorOrBeds] = new(b => b.OperatorOrBeds, (b, v) => b.OperatorOrBeds = v),
        [EntityProperty.BuildingFlags] = new(b => b.Flags, (b, v) => b.Flags = v),
        [EntityProperty.BuildingOccupantCount] = new(b => b.OccupantCount, (b, v) => b.OccupantCount = v),
        [EntityProperty.BuildingFirstOccupant] = new(b => b.FirstOccupant, (b, v) => b.FirstOccupant = v),
        [EntityProperty.BuildingAlertFlags] = new(b => b.AlertFlags, (b, v) => b.AlertFlags = v),
        [EntityProperty.BuildingLevel] = new(b => b.Level, (b, v) => b.Level = v),
        [EntityProperty.BuildingVisibility] = new(b => b.Visibility, (b, v) => b.Visibility = v),
        [EntityProperty.BuildingAux] = new(b => b.Aux, (b, v) => b.Aux = v),
        [EntityProperty.BuildingDispatchedWorkers] = new(b => b.DispatchedWorkers, (b, v) => b.DispatchedWorkers = v),
    };

    private static readonly Dictionary<EntityProperty, Accessor<Animal>> AnimalAccessors = new()
    {
        [EntityProperty.AnimalX] = new(a => a.X, (a, v) => a.X = v),
        [EntityProperty.AnimalY] = new(a => a.Y, (a, v) => a.Y = v),
        [EntityProperty.AnimalOffsetX] = new(a => a.OffsetX, (a, v) => a.OffsetX = v),
        [EntityProperty.AnimalOffsetY] = new(a => a.OffsetY, (a, v) => a.OffsetY = v),
        [EntityProperty.AnimalProvokeTimer] = new(a => a.ProvokeTimer, (a, v) => a.ProvokeTimer = v),
        [EntityProperty.AnimalSpawnZone] = new(a => a.SpawnZone, (a, v) => a.SpawnZone = v),
        [EntityProperty.AnimalFacing] = new(a => a.Facing, (a, v) => a.Facing = v),
        [EntityProperty.AnimalHitPoints] = new(a => a.HitPoints, (a, v) => a.HitPoints = v),
        [EntityProperty.AnimalKind] = new(a => a.Kind, (a, v) => a.Kind = v),
        [EntityProperty.AnimalAction] = new(a => a.Action, (a, v) => a.Action = v),
        [EntityProperty.AnimalActionFrame] = new(a => a.ActionFrame, (a, v) => a.ActionFrame = v),
        [EntityProperty.AnimalTarget] = new(a => a.Target, (a, v) => a.Target = v),
        [EntityProperty.AnimalStepCounter] = new(a => a.StepCounter, (a, v) => a.StepCounter = v),
        [EntityProperty.AnimalDestinationX] = new(a => a.DestinationX, (a, v) => a.DestinationX = v),
        [EntityProperty.AnimalDestinationY] = new(a => a.DestinationY, (a, v) => a.DestinationY = v),
        [EntityProperty.AnimalPathLength] = new(a => a.PathLength, (a, v) => a.PathLength = v),
        [EntityProperty.AnimalPathIndex] = new(a => a.PathIndex, (a, v) => a.PathIndex = v),
    };

    private static readonly Dictionary<EntityProperty, Accessor<ResourceNode>> NodeAccessors = new()
    {
        [EntityProperty.NodeX] = new(n => n.X, (n, v) => n.X = v),
        [EntityProperty.NodeY] = new(n => n.Y, (n, v) => n.Y = v),
        [EntityProperty.NodeKind] = new(n => n.Kind, (n, v) => n.Kind = v),
        [EntityProperty.NodeAmount] = new(n => n.Amount, (n, v) => n.Amount = v),
        [EntityProperty.NodeStage] = new(n => n.Stage, (n, v) => n.Stage = v),
    };

    /// <summary>The kind of entity <paramref name="property"/> belongs to.</summary>
    public static EntityKind EntityOf(EntityProperty property)
    {
        int number = CheckDefined(property);
        return number < FirstBuildingProperty ? EntityKind.Unit
            : number < FirstAnimalProperty ? EntityKind.Building
            : number < FirstNodeProperty ? EntityKind.Animal
            : EntityKind.ResourceNode;
    }

    /// <summary>Whether <paramref name="property"/> is a constant (it reads the same for every entity).</summary>
    public static bool IsConstant(EntityProperty property) => Constants.ContainsKey(property);

    /// <summary>
    /// Whether <paramref name="property"/> can be read and written: false for the few properties
    /// the original keeps only for its user interface.
    /// </summary>
    public static bool IsHeldByModel(EntityProperty property) =>
        Constants.ContainsKey(property) || UnitAccessors.ContainsKey(property)
        || BuildingAccessors.ContainsKey(property) || AnimalAccessors.ContainsKey(property)
        || NodeAccessors.ContainsKey(property);

    /// <summary>The value of <paramref name="property"/> of entity slot <paramref name="index"/>.</summary>
    /// <exception cref="NotSupportedException">The model does not hold the property.</exception>
    public static int Get(GameWorld world, EntityProperty property, int index)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (Constants.TryGetValue(property, out int constant))
        {
            return constant;
        }
        return EntityOf(property) switch
        {
            EntityKind.Unit => Find(UnitAccessors, property).Get(world.Unit(index)),
            EntityKind.Building => Find(BuildingAccessors, property).Get(world.Building(index)),
            EntityKind.Animal => Find(AnimalAccessors, property).Get(world.Animal(index)),
            _ => Find(NodeAccessors, property).Get(world.Map.ResourceNodes[index]),
        };
    }

    /// <summary>
    /// Sets <paramref name="property"/> of entity slot <paramref name="index"/>; the value is stored
    /// as the property stores it (see <see cref="Wrap"/>). Writing a constant does nothing.
    /// </summary>
    /// <exception cref="NotSupportedException">The model does not hold the property.</exception>
    public static void Set(GameWorld world, EntityProperty property, int index, int value)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (Constants.ContainsKey(property))
        {
            return;
        }
        switch (EntityOf(property))
        {
            case EntityKind.Unit:
                Find(UnitAccessors, property).Set(world.Unit(index), value);
                break;
            case EntityKind.Building:
                Find(BuildingAccessors, property).Set(world.Building(index), value);
                break;
            case EntityKind.Animal:
                Find(AnimalAccessors, property).Set(world.Animal(index), value);
                break;
            default:
                Find(NodeAccessors, property).Set(world.Map.ResourceNodes[index], value);
                break;
        }
    }

    /// <summary>Adds <paramref name="delta"/> to <paramref name="property"/> of entity slot <paramref name="index"/>.</summary>
    /// <exception cref="NotSupportedException">The model does not hold the property.</exception>
    public static void Add(GameWorld world, EntityProperty property, int index, int delta) =>
        Set(world, property, index, Get(world, property, index) + delta);

    private static int CheckDefined(EntityProperty property) =>
        Enum.IsDefined(property)
            ? (int)property
            : throw new ArgumentOutOfRangeException(nameof(property), property, "not an entity property");

    private static Accessor<T> Find<T>(Dictionary<EntityProperty, Accessor<T>> accessors, EntityProperty property) =>
        accessors.TryGetValue(property, out Accessor<T>? accessor)
            ? accessor
            : throw new NotSupportedException($"{property} is kept only by the original's user interface");

    private sealed record Accessor<T>(Func<T, int> Get, Action<T, int> Set);
}
