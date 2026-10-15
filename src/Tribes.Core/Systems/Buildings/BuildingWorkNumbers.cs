namespace Tribes.Core.Systems.Buildings;

/// <summary>Actions the building rules give the units working in them (rows of the action table).</summary>
internal static class WorkActions
{
    /// <summary>Fetching raw food from the stock for the kitchen.</summary>
    public const int FetchFood = 37;

    /// <summary>First of the fetch actions; the stock recipe taken is the action minus this.</summary>
    public const int FirstFetch = 28;

    /// <summary>Cooking.</summary>
    public const int Cook = 54;

    /// <summary>Building or repairing (working on the building's hit points).</summary>
    public const int Build = 56;

    /// <summary>The cook waiting in the kitchen.</summary>
    public const int KitchenIdle = 66;

    /// <summary>A builder sent to a site, upgrade or repair; also its standing task.</summary>
    public const int SentBuilder = 77;

    /// <summary>A builder sitting at its site until material arrives.</summary>
    public const int WaitForMaterial = 78;

    /// <summary>Building work on an upgrade or repair.</summary>
    public const int UpgradeWork = 76;

    /// <summary>Fetching the material of an upgrade or repair.</summary>
    public const int FetchUpgradeMaterial = 99;

    /// <summary>Waiting between two actions (the interrupted action is saved).</summary>
    public const int Pause = 84;

    /// <summary>The couple's action inside the hut.</summary>
    public const int CoupleInHut = 111;
}

/// <summary>Animations of a builder waiting for material.</summary>
internal static class MaterialWaitAnimations
{
    /// <summary>Waiting, seated.</summary>
    public const int Seated = 113;

    /// <summary>Sitting down.</summary>
    public const int SitDown = 115;

    /// <summary>Standing up.</summary>
    public const int StandUp = 116;
}

/// <summary>Values of <see cref="World.Unit.ActionSubState"/> the building rules read.</summary>
internal static class SubStates
{
    /// <summary>Inside the building.</summary>
    public const int Inside = -3;

    /// <summary>At the building's door.</summary>
    public const int AtDoor = -2;

    /// <summary>Stops on reaching the next cell centre.</summary>
    public const int StopAtCentre = -1;

    /// <summary>The bed pose of a hut's first occupant while a couple is not inside together.</summary>
    public const int Alone = 1;

    /// <summary>The bed pose while both partners of a couple are inside.</summary>
    public const int Together = 2;
}

/// <summary>Workplace groups (<c>ActionCategory.ServiceGroup</c>) the building rules compare.</summary>
internal static class ServiceGroups
{
    /// <summary>Construction: units of this group may be sent as builders.</summary>
    public const int Construction = 2;
}

/// <summary>Bits of <see cref="World.Building.AlertFlags"/>.</summary>
internal static class AlertBits
{
    /// <summary>Cancelling the upgrade or repair was asked for.</summary>
    public const int CancelRequested = 0x01;

    /// <summary>The building was attacked recently (cleared every 16 frames).</summary>
    public const int UnderAttack = 0x80;
}
