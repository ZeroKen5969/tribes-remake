namespace Tribes.Core.World;

/// <summary>A wild animal in a fixed slot (<see cref="Index"/>); the slot is free when <see cref="HitPoints"/> is 0.</summary>
public sealed class Animal
{
    /// <summary>Most waypoints an animal's route holds.</summary>
    public const int PathCapacity = 5;

    private readonly int[] pathX = new int[PathCapacity];
    private readonly int[] pathY = new int[PathCapacity];
    private int offsetX, offsetY, provokeTimer, spawnZone, facing, hitPoints, kind, action, actionFrame;
    private int target, stepCounter, pathLength, pathIndex;

    private readonly bool wideZones;

    internal Animal(int index, bool wideZones = false)
    {
        Index = index;
        this.wideZones = wideZones;
    }

    /// <summary>Slot number.</summary>
    public int Index { get; }

    /// <summary>Whether the slot holds an animal.</summary>
    public bool InUse => HitPoints != 0;

    /// <summary>Cell column.</summary>
    public int X { get; set; }

    /// <summary>Cell row.</summary>
    public int Y { get; set; }

    /// <summary>Pixel offset from the cell centre.</summary>
    public int OffsetX { get => offsetX; set => offsetX = Wrap.Signed8(value); }

    /// <summary>Pixel offset from the cell centre.</summary>
    public int OffsetY { get => offsetY; set => offsetY = Wrap.Signed8(value); }

    /// <summary>Whether the animal stands exactly on its cell centre.</summary>
    public bool IsCentred => OffsetX == 0 && OffsetY == 0;

    /// <summary>
    /// Idle counter 1..19, or 19 + the attacking unit's slot after being hit (it turns on the
    /// attacker at its next decision).
    /// </summary>
    public int ProvokeTimer { get => provokeTimer; set => provokeTimer = value; }

    /// <summary>The spawn zone it belongs to and roams around (in an endless world: its habitat, any number).</summary>
    public int SpawnZone { get => spawnZone; set => spawnZone = wideZones ? value : Wrap.Signed8(value); }

    /// <summary>Direction 0..7.</summary>
    public int Facing { get => facing; set => facing = Wrap.Signed8(value); }

    /// <summary>Hit points; 0 frees the slot.</summary>
    public int HitPoints { get => hitPoints; set => hitPoints = Wrap.Signed8(value); }

    /// <summary>Animal kind (index into the animal table).</summary>
    public int Kind { get => kind; set => kind = Wrap.Signed8(value); }

    /// <summary>Current action animation.</summary>
    public int Action { get => action; set => action = Wrap.Signed8(value); }

    /// <summary>Frame counter of <see cref="Action"/>.</summary>
    public int ActionFrame { get => actionFrame; set => actionFrame = Wrap.Signed8(value); }

    /// <summary>Unit slot it chases (positive), building slot negated (negative), or 0.</summary>
    public int Target { get => target; set => target = value; }

    /// <summary>Sub-steps taken within the current step.</summary>
    public int StepCounter { get => stepCounter; set => stepCounter = Wrap.Signed8(value); }

    /// <summary>Destination cell.</summary>
    public int DestinationX { get; set; }

    /// <summary>Destination cell.</summary>
    public int DestinationY { get; set; }

    /// <summary>Waypoints in the current route.</summary>
    public int PathLength { get => pathLength; set => pathLength = Wrap.Signed8(value); }

    /// <summary>Index of the next waypoint.</summary>
    public int PathIndex { get => pathIndex; set => pathIndex = Wrap.Signed8(value); }

    /// <summary>Column of waypoint <paramref name="i"/>.</summary>
    public int PathXAt(int i) => pathX[i];

    /// <summary>Row of waypoint <paramref name="i"/>.</summary>
    public int PathYAt(int i) => pathY[i];

    /// <summary>Sets waypoint <paramref name="i"/>.</summary>
    public void SetPathPoint(int i, int column, int row)
    {
        pathX[i] = column;
        pathY[i] = row;
    }

    /// <summary>Saves, loads or hashes every value of the slot (see <see cref="StateExchange"/>).</summary>
    internal void Exchange(StateExchange s)
    {
        X = s.Value(X);
        Y = s.Value(Y);
        OffsetX = s.Value(OffsetX);
        OffsetY = s.Value(OffsetY);
        ProvokeTimer = s.Value(ProvokeTimer);
        SpawnZone = s.Value(SpawnZone);
        Facing = s.Value(Facing);
        HitPoints = s.Value(HitPoints);
        Kind = s.Value(Kind);
        Action = s.Value(Action);
        ActionFrame = s.Value(ActionFrame);
        Target = s.Value(Target);
        StepCounter = s.Value(StepCounter);
        DestinationX = s.Value(DestinationX);
        DestinationY = s.Value(DestinationY);
        PathLength = s.Value(PathLength);
        PathIndex = s.Value(PathIndex);
        if (s is StateHasher hasher)
        {
            hasher.MixAll(pathX);
            hasher.MixAll(pathY);
            return;
        }
        for (int i = 0; i < PathCapacity; i++)
        {
            pathX[i] = s.Value(pathX[i]);
            pathY[i] = s.Value(pathY[i]);
        }
    }
}
