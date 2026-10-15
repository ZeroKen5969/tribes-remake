using System;
using System.Collections.Generic;
using Tribes.Core.Content;

namespace Tribes.Core.World;

/// <summary>
/// A tribe's counters. The numbers are the original's counter slots, which the content tables
/// refer to (for example "deliveries of this item go to counter n").
/// </summary>
public enum TribeCounter
{
    /// <summary>Cooked meals in stock.</summary>
    Meals = 0,

    /// <summary>Food.</summary>
    Food = 1,

    /// <summary>Wood.</summary>
    Wood = 2,

    /// <summary>Stones.</summary>
    Stones = 3,

    /// <summary>Bones.</summary>
    Bones = 4,

    /// <summary>Clubs in stock.</summary>
    Clubs = 5,

    /// <summary>Stone weapons that can be made (shown in the armoury menu only).</summary>
    StoneWeapons = 6,

    /// <summary>Spears in stock.</summary>
    Spears = 7,

    /// <summary>Stone axes in stock.</summary>
    StoneAxes = 8,

    /// <summary>Bone maces in stock.</summary>
    BoneMaces = 9,

    /// <summary>Tomahawks in stock.</summary>
    Tomahawks = 10,

    /// <summary>Living units, children included.</summary>
    Population = 11,

    /// <summary>Living males.</summary>
    Males = 12,

    /// <summary>Living females.</summary>
    Females = 13,

    /// <summary>Buildings.</summary>
    Buildings = 14,

    /// <summary>Not used.</summary>
    Unused = 15,

    /// <summary>Living space (4 per hut), shown next to the population.</summary>
    LivingSpace = 16,

    /// <summary>The chief's unit slot.</summary>
    Chief = 17,

    /// <summary>Birth cool-down: counts up from 0, -1 when a couple may have a child.</summary>
    BirthCooldown = 18,

    /// <summary>Statistic: births.</summary>
    Born = 19,

    /// <summary>Statistic: buildings built.</summary>
    Built = 20,

    /// <summary>Statistic: enemy buildings destroyed.</summary>
    BuildingsDestroyed = 21,

    /// <summary>Statistic: enemy units killed.</summary>
    UnitsKilled = 22,

    /// <summary>Statistic: resources delivered.</summary>
    Gathered = 23,

    /// <summary>Score at the end of a mission.</summary>
    Score = 24,

    /// <summary>Weapons in stock as the storehouse picture counts them (presentation).</summary>
    StorehouseWeapons = 25,
}

/// <summary>
/// Everything kept per tribe: who plays it, its team, counters, the orders its buildings work
/// through, and the state of its player (army calls, hints) or of the computer playing it.
/// </summary>
public sealed class TribeState
{
    /// <summary>The <see cref="Team"/> of a tribe on no team.</summary>
    public const int NoTeam = -1;

    /// <summary>Counter slots in the original.</summary>
    public const int CounterCount = 26;

    /// <summary>Armoury production slots per tribe in the original (a match's tribe gets more as it builds armouries).</summary>
    public const int ArmourySlots = 10;

    /// <summary>Task orders per tribe in the original (a match's tribe gets more when all are in use).</summary>
    public const int TaskOrderSlots = 30;

    /// <summary>The most armoury queues or task orders a saved tribe may have (far more than any tribe uses).</summary>
    private const int MostOrders = 1 << 20;

    /// <summary>Alarm calls per tribe.</summary>
    public const int AlarmSlots = 5;

    /// <summary>Frames without fighting after which a tribe counts as quiet (its count stops there).</summary>
    public const int QuietAfterFrames = 50;

    private readonly int[] counters = new int[CounterCount];
    private readonly List<ArmouryOrders> armouries = new();
    private readonly List<TaskOrder> taskOrders = new();

    internal TribeState(int index)
    {
        Index = index;
        Armouries = new ListView<ArmouryOrders>(armouries);
        TaskOrders = new ListView<TaskOrder>(taskOrders);
        for (int i = 0; i < ArmourySlots; i++)
        {
            armouries.Add(new ArmouryOrders());
        }
        for (int i = 0; i < TaskOrderSlots; i++)
        {
            taskOrders.Add(new TaskOrder());
        }
        for (int i = 0; i < AlarmSlots; i++)
        {
            Alarms[i] = new AlarmCall();
        }
    }

    /// <summary>Tribe number; 0 is the player in single-player games.</summary>
    public int Index { get; }

    /// <summary>Who plays the tribe (see <see cref="TribeRoles"/>).</summary>
    public TribeController Controller { get; internal set; }

    /// <summary>The tribe's team (team mates are allies), or <see cref="NoTeam"/>.</summary>
    public int Team { get; internal set; } = NoTeam;

    /// <summary>
    /// Where the tribe's camp was placed in a match (its chief's hut), the origin of the computer's
    /// plan for it; (0, 0) in the campaign, where the mission's plans are the map's own.
    /// </summary>
    public GridPoint Camp { get; internal set; }

    /// <summary>In a match: the frame the tribe went out (wiped out or gave up), or -1 while it is in.</summary>
    public int OutAtFrame { get; internal set; } = -1;

    /// <summary>The frame the tribe came into the world (0 for the tribes a game starts with).</summary>
    public int ArrivedAtFrame { get; internal set; }

    /// <summary>In a match: the tribe's chief is gone and its player is asked to name a new one.</summary>
    public bool AwaitingNewChief { get; internal set; }

    /// <summary>
    /// In a match: frames since a unit of the tribe attacked or was attacked, counted up to
    /// <see cref="QuietAfterFrames"/> (from there the tribe is quiet); the campaign keeps one
    /// world-wide count (<see cref="GameWorld.FramesSinceCombat"/>). A tribe starts quiet.
    /// </summary>
    public int FramesSinceCombat { get; internal set; } = QuietAfterFrames;

    /// <summary>The army-wide orders of the tribe's player.</summary>
    public ArmyOrders Army { get; } = new();

    /// <summary>The hint option and hint cool-downs of the tribe's player.</summary>
    public HintState Hints { get; } = new();

    /// <summary>The state of the computer player when it plays the tribe.</summary>
    public AiState Ai { get; } = new();

    /// <summary>Production queue of each armoury the tribe owns.</summary>
    public ListView<ArmouryOrders> Armouries { get; }

    /// <summary>Work orders issued by its buildings (scouting, spells, raids...).</summary>
    public ListView<TaskOrder> TaskOrders { get; }

    /// <summary>Places where its units raised an alarm.</summary>
    public AlarmCall[] Alarms { get; } = new AlarmCall[AlarmSlots];

    /// <summary>The value of <paramref name="counter"/>.</summary>
    public int this[TribeCounter counter]
    {
        get => counters[(int)counter];
        set => counters[(int)counter] = Wrap.Signed16(value);
    }

    /// <summary>Counter by its slot number (as the content tables give it).</summary>
    public int Counter(int slot) => counters[CheckSlot(slot)];

    /// <summary>Sets a counter by its slot number.</summary>
    public void SetCounter(int slot, int value) => counters[CheckSlot(slot)] = Wrap.Signed16(value);

    /// <summary>Saves, loads or hashes the tribe's state (see <see cref="StateExchange"/>).</summary>
    internal void Exchange(StateExchange s)
    {
        for (int i = 0; i < CounterCount; i++)
        {
            counters[i] = s.Value(counters[i]);
        }
        ExchangeCount(s, armouries, () => new ArmouryOrders());
        foreach (ArmouryOrders orders in armouries)
        {
            orders.Exchange(s);
        }
        ExchangeCount(s, taskOrders, () => new TaskOrder());
        foreach (TaskOrder order in taskOrders)
        {
            order.Exchange(s);
        }
        foreach (AlarmCall alarm in Alarms)
        {
            alarm.Exchange(s);
        }
    }

    /// <summary>Saves, loads or hashes the controller, team and player or computer state (format 2 on).</summary>
    internal void ExchangeRoles(StateExchange s)
    {
        TribeController controller = Controller;
        var kind = (TribeControl)s.Value((int)controller.Kind);
        int slot = s.Value(controller.Slot);
        int difficulty = s.Value(controller.Difficulty ?? -1);
        Controller = new TribeController(kind, slot, difficulty < 0 ? null : difficulty);
        Team = s.Value(Team);
        Camp = new GridPoint(s.Value(Camp.X), s.Value(Camp.Y));
        OutAtFrame = s.Value(OutAtFrame);
        if (s.Format >= StateExchange.EndlessFormat)
        {
            ArrivedAtFrame = s.Value(ArrivedAtFrame);
        }
        AwaitingNewChief = s.Flag(AwaitingNewChief);
        if (s.Format >= StateExchange.CombatFormat)
        {
            FramesSinceCombat = s.Value(FramesSinceCombat);
        }
        Army.Exchange(s);
        Hints.Exchange(s);
        Ai.Exchange(s);
        Ai.ExchangeMatch(s);
    }

    /// <summary>
    /// The first free armoury queue; when all are taken a new one at the end if
    /// <paramref name="grow"/> (a match), else null.
    /// </summary>
    internal ArmouryOrders? FreeArmouryQueue(bool grow)
    {
        foreach (ArmouryOrders queue in armouries)
        {
            if (queue.Armoury == 0)
            {
                return queue;
            }
        }
        if (!grow)
        {
            return null;
        }
        var added = new ArmouryOrders();
        armouries.Add(added);
        return added;
    }

    /// <summary>
    /// The task order a new order goes into: the first free one; when all are in use a new one at
    /// the end if <paramref name="grow"/> (a match), else the last one, which the new order
    /// overwrites, as in the original.
    /// </summary>
    internal TaskOrder FreeTaskOrder(bool grow)
    {
        foreach (TaskOrder order in taskOrders)
        {
            if (order.Building == 0)
            {
                return order;
            }
        }
        if (!grow)
        {
            return taskOrders[^1];
        }
        var added = new TaskOrder();
        taskOrders.Add(added);
        return added;
    }

    /// <summary>The length of a table that grows in a match (format 5 on; it never shrinks).</summary>
    private static void ExchangeCount<T>(StateExchange s, List<T> table, Func<T> create)
    {
        if (s.Format < StateExchange.SlotsFormat)
        {
            return;
        }
        int count = s.Value(table.Count);
        if (!s.IsReading)
        {
            return;
        }
        if (count < table.Count || count > MostOrders)
        {
            throw new System.IO.InvalidDataException($"saved tribe: a table of {count} entries");
        }
        while (table.Count < count)
        {
            table.Add(create());
        }
    }

    private static int CheckSlot(int slot) =>
        slot is >= 0 and < CounterCount ? slot : throw new ArgumentOutOfRangeException(nameof(slot), slot, null);
}

/// <summary>An armoury's production queue.</summary>
public sealed class ArmouryOrders
{
    /// <summary>Products (weapons, then traps) an armoury can be asked for.</summary>
    public const int ProductCount = 10;

    private readonly int[] pending = new int[ProductCount];
    private int current;

    /// <summary>Items still to make of product <paramref name="product"/>.</summary>
    public int Pending(int product) => pending[product];

    /// <summary>Sets the items still to make of product <paramref name="product"/>.</summary>
    public void SetPending(int product, int count) => pending[product] = Wrap.Signed8(count);

    /// <summary>The product being made.</summary>
    public int Current { get => current; set => current = Wrap.Signed8(value); }

    /// <summary>The armoury building this queue belongs to; 0 when the slot is free.</summary>
    public int Armoury { get; set; }

    internal void Exchange(StateExchange s)
    {
        for (int i = 0; i < ProductCount; i++)
        {
            pending[i] = s.Value(pending[i]);
        }
        Current = s.Value(Current);
        Armoury = s.Value(Armoury);
    }
}

/// <summary>A work order issued by a building (the original's task order table).</summary>
public sealed class TaskOrder
{
    private int kind, a, b, extra;

    /// <summary>The issuing building; 0 when the slot is free.</summary>
    public int Building { get; set; }

    /// <summary>What to do (0..3).</summary>
    public int Kind { get => kind; set => kind = Wrap.Signed8(value); }

    /// <summary>First argument: a column or a target slot.</summary>
    public int A { get => a; set => a = value; }

    /// <summary>Second argument: a row.</summary>
    public int B { get => b; set => b = Wrap.Signed8(value); }

    /// <summary>Fifth column of the original table.</summary>
    public int Extra { get => extra; set => extra = Wrap.Signed8(value); }

    internal void Exchange(StateExchange s)
    {
        Building = s.Value(Building);
        Kind = s.Value(Kind);
        A = s.Value(A);
        B = s.Value(B);
        Extra = s.Value(Extra);
    }
}

/// <summary>A place where a unit raised an alarm.</summary>
public sealed class AlarmCall
{
    /// <summary>Tribes from this number on are not marked in <see cref="Spotted"/> (they are kept beside it).</summary>
    public const int MarkableTribes = 31;

    private int age = -1, extra;
    private List<int>? knownBeyond;

    /// <summary>Cell column.</summary>
    public int X { get; set; }

    /// <summary>Cell row.</summary>
    public int Y { get; set; }

    /// <summary>Age in eight-frame steps; -1 when the slot is free; it expires at 100.</summary>
    public int Age { get => age; set => age = Wrap.Signed8(value); }

    /// <summary>
    /// The human-ruled tribes below <see cref="MarkableTribes"/> that know of it, a bit per tribe
    /// (bit t for tribe t); in the original 1 when the player's tribe has noticed it. Use
    /// <see cref="IsKnownTo"/> for any tribe.
    /// </summary>
    public int Spotted { get; set; }

    /// <summary>Whether <paramref name="tribe"/> knows of it.</summary>
    public bool IsKnownTo(int tribe) => tribe < MarkableTribes
        ? tribe >= 0 && (Spotted & (1 << tribe)) != 0
        : knownBeyond != null && knownBeyond.BinarySearch(tribe) >= 0;

    /// <summary>Marks that <paramref name="tribe"/> knows of it.</summary>
    public void MarkKnownTo(int tribe)
    {
        if (tribe is >= 0 and < MarkableTribes)
        {
            Spotted |= 1 << tribe;
            return;
        }
        knownBeyond ??= new List<int>();
        int at = knownBeyond.BinarySearch(tribe);
        if (tribe >= 0 && at < 0)
        {
            knownBeyond.Insert(~at, tribe);
        }
    }

    /// <summary>Takes over the place, age and who knows of <paramref name="other"/> (not its fifth column).</summary>
    internal void TakeCallOf(AlarmCall other)
    {
        (X, Y, Age, Spotted) = (other.X, other.Y, other.Age, other.Spotted);
        knownBeyond = other.knownBeyond == null ? null : new List<int>(other.knownBeyond);
    }

    /// <summary>No tribe knows of it.</summary>
    internal void ForgetSpotted()
    {
        Spotted = 0;
        knownBeyond = null;
    }

    /// <summary>Fifth column of the original table.</summary>
    public int Extra { get => extra; set => extra = Wrap.Signed8(value); }

    internal void Exchange(StateExchange s)
    {
        X = s.Value(X);
        Y = s.Value(Y);
        Age = s.Value(Age);
        Spotted = s.Value(Spotted);
        Extra = s.Value(Extra);
        if (s.Format < StateExchange.EndlessFormat)
        {
            return;
        }
        int count = s.Value(knownBeyond?.Count ?? 0);
        if (s.IsReading)
        {
            knownBeyond = count == 0 ? null : new List<int>(count);
            for (int i = 0; i < count; i++)
            {
                knownBeyond!.Add(s.Value(0));
            }
            return;
        }
        for (int i = 0; i < count; i++)
        {
            s.Value(knownBeyond![i]);
        }
    }
}
