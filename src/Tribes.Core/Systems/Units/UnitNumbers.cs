namespace Tribes.Core.Systems.Units;

/// <summary>
/// Animation numbers the unit body and movement rules refer to by name, as the unsigned numbers
/// <see cref="World.Unit.AnimationNumber"/> gives (the model stores them as signed bytes).
/// </summary>
internal static class UnitAnimations
{
    /// <summary>A unit playing a magic spell (a thought icon of its own is shown over it).</summary>
    public const int Casting = 39;

    /// <summary>A unit caught by a trap shakes itself free with one of three animations from this one on.</summary>
    public const int FirstTrapped = 106;

    /// <summary>The last of the trapped animations.</summary>
    public const int LastTrapped = 108;

    /// <summary>The witch doctor brewing (it shows a thought icon of its own).</summary>
    public const int Brewing = 104;

    /// <summary>The witch doctor receiving a patient.</summary>
    public const int Healing = 105;

    /// <summary>Getting up from hiding.</summary>
    public const int Unhide = 150;

    /// <summary>Crouching down to hide.</summary>
    public const int Hide = 149;

    /// <summary>Turning to the building the unit was called to (the chief's call).</summary>
    public const int Summoned = 161;

    /// <summary>Getting up inside a building to give up the work there (the unit's task is dropped on its sixth frame).</summary>
    public const int ChangingWorkplace = 148;

    /// <summary>Frame of <see cref="ChangingWorkplace"/> on which the unit's task is dropped.</summary>
    public const int TaskDroppedFrame = 6;

    /// <summary>Taking up a task of another group inside a building (the task is taken on its first frame).</summary>
    public const int ChangingTask = 147;

    /// <summary>Frame of <see cref="ChangingTask"/> on which the task is taken.</summary>
    public const int TaskTakenFrame = 1;

    /// <summary>Unloading a full load of material at a site or an upgrade.</summary>
    public const int UnloadingMaterial = 154;

    /// <summary>Two tribe mates chatting, this one holding something.</summary>
    public const int ChattingHolding = 156;

    /// <summary>Two tribe mates chatting.</summary>
    public const int Chatting = 155;

    /// <summary>The man of a couple giving his gift.</summary>
    public const int GivingGift = 157;

    /// <summary>The woman of a couple receiving the gift.</summary>
    public const int ReceivingGift = 158;

    /// <summary>Frame of <see cref="GivingGift"/> on which the gift changes hands.</summary>
    public const int GiftHandedFrame = 6;

    /// <summary>Casting the fishing line.</summary>
    public const int CastingLine = 146;

    /// <summary>Fishing.</summary>
    public const int Fishing = 145;

    /// <summary>The first of the fishing animations; a fisher playing anything below it starts fishing.</summary>
    public const int FirstFishingAnimation = 141;

    /// <summary>Delivering at the store (the load counts as gathered on its tenth frame).</summary>
    public const int Delivering = 22;

    /// <summary>Frame of <see cref="Delivering"/> on which the load is handed over.</summary>
    public const int DeliveryFrame = 10;

    /// <summary>First of the two throwing animations (a projectile leaves on the last frame but one).</summary>
    public const int FirstThrow = 35;

    /// <summary>Last of the two throwing animations.</summary>
    public const int LastThrow = 36;

    /// <summary>Waiting for a thrown projectile to land before throwing again.</summary>
    public const int AwaitingImpact = 34;

    /// <summary>Hit by a blow: the reaction played by the unit struck.</summary>
    public const int StruckReaction = 42;
}

/// <summary>Thought icons (<see cref="World.Unit.Thought"/>) the rules set by name.</summary>
internal static class Thoughts
{
    /// <summary>No thought shown.</summary>
    public const int None = -1;

    /// <summary>Hungry (satiety 0).</summary>
    public const int Hungry = 0;

    /// <summary>In a couple.</summary>
    public const int InLove = 1;

    /// <summary>Exhausted (energy 0).</summary>
    public const int Exhausted = 2;

    /// <summary>Homeless and unhappy.</summary>
    public const int Homeless = 9;

    /// <summary>The first of the two spell icons; a spell picks it or the next one.</summary>
    public const int FirstSpell = 11;

    /// <summary>Thought icons that are rolled from the thought rules.</summary>
    public const int RolledIcons = 11;
}

/// <summary>Action categories (<c>ActionDefinition.Category</c>) the unit body and movement rules refer to by name.</summary>
internal static class WorkCategories
{
    /// <summary>Armoury work.</summary>
    public const int Armoury = 8;

    /// <summary>Kitchen work (satiety rises instead of falling).</summary>
    public const int Kitchen = 9;

    /// <summary>Disco work.</summary>
    public const int Disco = 10;

    /// <summary>Sanctuary work (a witch doctor heals slowly).</summary>
    public const int Sanctuary = 11;

    /// <summary>Construction and upgrades.</summary>
    public const int Construction = 12;

    /// <summary>First of the three training categories (pia center, gym, range).</summary>
    public const int FirstTraining = 13;

    /// <summary>Last of the training categories.</summary>
    public const int LastTraining = 15;
}

/// <summary>Facings the unit rules set by name (0 east, then clockwise).</summary>
internal static class FacingNumbers
{
    /// <summary>Facing east.</summary>
    public const int East = 0;

    /// <summary>Facing west.</summary>
    public const int West = 4;
}

/// <summary>Values of <see cref="World.Unit.BuildingSlot"/> outside the work positions.</summary>
internal static class WorkPositionNumbers
{
    /// <summary>No work position (and no overlay drawn over the unit); lower values are overlays.</summary>
    public const int None = -1;
}

/// <summary>Values of <see cref="World.Unit.ActionSubState"/> the movement rules set by name.</summary>
internal static class SubStates
{
    /// <summary>Inside a building, one of its occupants.</summary>
    public const int Inside = -3;

    /// <summary>Just through the door of a building; it takes its place on its next action.</summary>
    public const int AtTheDoor = -2;

    /// <summary>Stepping through an entrance: arrives inside at the middle of the step.</summary>
    public const int Entering = -1;

    /// <summary>Nothing special.</summary>
    public const int Normal = 0;

    /// <summary>The first pose of a unit inside a building.</summary>
    public const int FirstPose = 1;

    /// <summary>Inside a building that is upgraded or repaired.</summary>
    public const int InBusyBuilding = 50;

    /// <summary>Inside a disco.</summary>
    public const int InDisco = 51;
}
