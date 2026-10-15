namespace Tribes.Core.World;

/// <summary>What a building is doing besides its normal function.</summary>
public enum BuildingWorkMode
{
    /// <summary>Working normally.</summary>
    Normal = 0,

    /// <summary>Being upgraded to the next level.</summary>
    Upgrading = 1,

    /// <summary>Being repaired.</summary>
    Repairing = 2,

    /// <summary>The slot was freed after demolition.</summary>
    Freed = 3,
}

/// <summary>
/// A building, construction site or ruin in a fixed slot (<see cref="Id"/>). The slot is free when
/// <see cref="HitPoints"/> is 0; -1 marks a ruin whose dust has not settled yet.
/// </summary>
public sealed class Building
{
    private int hitPoints, maxHitPoints, workMode, rangeInfo, kind, operatorOrBeds, flags, occupantCount;
    private int alertFlags, level, visibility, aux, dispatchedWorkers;

    internal Building(int id, int tribe)
    {
        Id = id;
        Tribe = tribe;
    }

    /// <summary>Slot number, unique in the world; the original numbers tribe t's buildings t*30+1.. .</summary>
    public int Id { get; }

    /// <summary>The tribe the slot belongs to (for good).</summary>
    public int Tribe { get; }

    /// <summary>
    /// The slot's place among its tribe's building slots (<see cref="GameWorld.BuildingsOf"/>): 0 for
    /// the tribe's first slot, the chief's hut in the original's camps.
    /// </summary>
    public int SlotInTribe { get; internal init; }

    /// <summary>Whether the slot holds a building or ruin.</summary>
    public bool InUse => HitPoints != 0;

    /// <summary>Whether it stands (not a ruin).</summary>
    public bool Stands => HitPoints > 0;

    /// <summary>Left column of the footprint.</summary>
    public int X { get; set; }

    /// <summary>Top row of the footprint.</summary>
    public int Y { get; set; }

    /// <summary>Durability; 0 free slot, -1 ruin.</summary>
    public int HitPoints { get => hitPoints; set => hitPoints = Wrap.Signed8(value); }

    /// <summary>Durability limit at the current (or, while upgrading, next) level.</summary>
    public int MaxHitPoints { get => maxHitPoints; set => maxHitPoints = Wrap.Signed8(value); }

    /// <summary>Upgrade, repair or normal work.</summary>
    public BuildingWorkMode WorkMode { get => (BuildingWorkMode)workMode; set => workMode = Wrap.Signed8((int)value); }

    /// <summary>Raw sight/range byte: low 4 bits sight radius, high 4 bits best occupant scouting skill.</summary>
    public int RangeInfo { get => rangeInfo; set => rangeInfo = Wrap.Signed8(value); }

    /// <summary>Sight radius in cells (low 4 bits of <see cref="RangeInfo"/>).</summary>
    public int SightRadius => RangeInfo & 0x0F;

    /// <summary>Building kind (index into the building table).</summary>
    public int Kind { get => kind; set => kind = Wrap.Signed8(value); }

    /// <summary>
    /// The unit at work position 0 (operator or chief builder), or for a hut in normal mode the
    /// number of reserved beds.
    /// </summary>
    public int OperatorOrBeds { get => operatorOrBeds; set => operatorOrBeds = value; }

    /// <summary>Bit 0: drawn this frame (presentation); bit 1: a sick occupant is inside.</summary>
    public int Flags { get => flags; set => flags = Wrap.Signed8(value); }

    /// <summary>Whether a sick occupant is inside.</summary>
    public bool HasSickOccupant => (Flags & 0x02) != 0;

    /// <summary>Units inside or entering (all of them for a hut, else not counting the operator).</summary>
    public int OccupantCount { get => occupantCount; set => occupantCount = Wrap.Signed8(value); }

    /// <summary>First unit of the occupant list (each unit points to the next); 0 empty.</summary>
    public int FirstOccupant { get; set; }

    /// <summary>Bit 0: cancelling the upgrade or repair was asked for; bit 7: attacked recently.</summary>
    public int AlertFlags { get => alertFlags; set => alertFlags = Wrap.Signed8(value); }

    /// <summary>Level 1..3 (construction sites are always 3).</summary>
    public int Level { get => level; set => level = Wrap.Signed8(value); }

    /// <summary>Whether its footprint was revealed (bit 2) and the drawing layer (bits 0-1).</summary>
    public int Visibility { get => visibility; set => visibility = Wrap.Signed8(value); }

    /// <summary>
    /// Depends on the kind and mode: the building a site becomes; a hut's birth stage; a
    /// sanctuary's casting flag; the saved operator while upgrading or repairing.
    /// </summary>
    public int Aux { get => aux; set => aux = Wrap.Signed8(value); }

    /// <summary>Workers sent to the site, upgrade or repair.</summary>
    public int DispatchedWorkers { get => dispatchedWorkers; set => dispatchedWorkers = Wrap.Signed8(value); }

    /// <summary>Saves, loads or hashes every value of the slot (see <see cref="StateExchange"/>).</summary>
    internal void Exchange(StateExchange s)
    {
        X = s.Value(X);
        Y = s.Value(Y);
        HitPoints = s.Value(HitPoints);
        MaxHitPoints = s.Value(MaxHitPoints);
        WorkMode = (BuildingWorkMode)s.Value((int)WorkMode);
        RangeInfo = s.Value(RangeInfo);
        Kind = s.Value(Kind);
        OperatorOrBeds = s.Value(OperatorOrBeds);
        Flags = s.Value(Flags);
        OccupantCount = s.Value(OccupantCount);
        FirstOccupant = s.Value(FirstOccupant);
        AlertFlags = s.Value(AlertFlags);
        Level = s.Value(Level);
        Visibility = s.Value(Visibility);
        Aux = s.Value(Aux);
        DispatchedWorkers = s.Value(DispatchedWorkers);
    }
}
