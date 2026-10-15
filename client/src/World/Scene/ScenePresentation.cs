using System;
using System.Collections.Generic;
using Tribes.Core.Simulation;
using Tribes.Core.Systems.Combat;
using Tribes.Core.Systems.Entities;
using Tribes.Core.Systems.Units.Combat;
using Tribes.Core.World;

namespace Tribes.Client.World.Scene;

/// <summary>One slot of the effect list: an animation that follows a unit, an animal or a building cell.</summary>
public struct EffectSlot
{
    /// <summary>Effect number; 0 (or below, as the original keeps it in a byte) for a free slot.</summary>
    public sbyte Effect;

    /// <summary>Frames shown so far.</summary>
    public sbyte Frame;

    /// <summary>For units the slot type it is attached to; for buildings the cell (column + 16 * row).</summary>
    public sbyte Anchor;

    /// <summary>1 a unit, 2 an animal, anything else a building.</summary>
    public sbyte Subject;

    /// <summary>The unit, animal or building slot (the original's fit a byte; a world of many tribes has more).</summary>
    public int Slot;
}

/// <summary>A projectile's flight as the original draws it: its height and picture offset at each step.</summary>
/// <param name="Heights">Pixels above the ground at step 0..steps.</param>
/// <param name="Frames">Flight picture at step 0..steps.</param>
public sealed record ProjectileArc(IReadOnlyList<int> Heights, IReadOnlyList<int> Frames);

/// <summary>
/// What only the screen keeps about a game: the effect list, the floating numbers, projectile
/// arcs, the two animation clocks and the palette variant left over from the last picture. It
/// moves on once per rule frame from the rules' events, like the original's drawing state, and
/// never changes the world.
/// </summary>
public sealed class ScenePresentation
{
    /// <summary>Slots of the effect list.</summary>
    public const int EffectCount = 40;

    /// <summary>Period of <see cref="Blink"/>.</summary>
    public const int BlinkPeriod = 8;

    /// <summary>Period of <see cref="Cycle"/>.</summary>
    public const int CyclePeriod = 120;

    private const int MaxFlightSteps = 12;

    /// <summary>How far the ranged attacks 5..9 throw downwards (the original's table 123).</summary>
    private static readonly int[] RangedDrops = { 3, 3, 8, 0, 0 };

    private readonly List<int> landed = new();
    private const int TomahawkKind = 2;
    private const int FrameClampMax = 16;
    private const int FrameCentre = 8;
    private const int FrameSlope = -4;

    /// <summary>The fast clock (0..7): cursor, building markers.</summary>
    public int Blink { get; set; }

    /// <summary>The animation clock (0..119) of idle, ambient and status effects.</summary>
    public int Cycle { get; set; }

    /// <summary>The palette variant the original still has selected when it starts drawing.</summary>
    public int PaletteVariant { get; set; } = -1;

    /// <summary>The effect list.</summary>
    public EffectSlot[] Effects { get; } = new EffectSlot[EffectCount];

    /// <summary>The floating numbers.</summary>
    public FloatingLabels Labels { get; } = new();

    /// <summary>The jitter of the floating numbers (the original's presentation generator, seed 3).</summary>
    public JavaRandom Jitter { get; set; } = new(3);

    /// <summary>The arcs of the projectiles in flight, by thrower.</summary>
    public Dictionary<int, ProjectileArc> Arcs { get; } = new();

    /// <summary>
    /// One rule frame has passed: the clocks move on, the floating numbers rise and new ones appear,
    /// and the rules' events start effects, add to the numbers and lay out thrown projectiles.
    /// Call after the rules ran, with the events of that frame.
    /// </summary>
    public void OnRuleFrame(GameWorld world, IReadOnlyList<GameEvent> events)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(events);
        Blink = (Blink + 1) % BlinkPeriod;
        // The original moves the numbers at the start of its frame, with the frame counter before the
        // frame adds one to it.
        Labels.Step(world.Frame - 1);
        foreach (GameEvent e in events)
        {
            switch (e)
            {
                case BlowLanded blow:
                    OnBlow(world, blow);
                    break;
                case HitPointsChanged change:
                    Labels.Add(FloatingLabels.UnitKey(change.Unit), change.Delta);
                    break;
                case ProjectileThrown throw_:
                    LayOut(world, throw_);
                    break;
                default:
                    break;
            }
        }
        Cycle = (Cycle + 1) % CyclePeriod;
        DropLandedArcs(world);
    }

    /// <summary>
    /// Moves every effect on by <paramref name="frames"/> frames without drawing it (rule frames the
    /// screen did not get to draw); effects that run out end.
    /// </summary>
    public void SkipEffectFrames(int frames, Tribes.Graphics.Model.SpriteCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        for (int i = 0; i < EffectCount && frames > 0; i++)
        {
            int effect = Effects[i].Effect;
            if (effect <= 0)
            {
                continue;
            }
            int frame = Effects[i].Frame + frames;
            if (effect >= catalog.Effects.Count || frame >= catalog.Effects[effect].Frames.Count)
            {
                Effects[i].Effect = 0;
            }
            else
            {
                Effects[i].Frame = unchecked((sbyte)frame);
            }
        }
    }

    /// <summary>Starts effect <paramref name="effect"/> in the first free slot (none when the list is full).</summary>
    public void StartEffect(int slot, int subject, int anchor, int effect)
    {
        for (int i = 0; i < EffectCount; i++)
        {
            if (Effects[i].Effect == 0)
            {
                Effects[i] = new EffectSlot
                {
                    Effect = unchecked((sbyte)effect),
                    Frame = 0,
                    Anchor = unchecked((sbyte)anchor),
                    Subject = unchecked((sbyte)subject),
                    Slot = slot,
                };
                return;
            }
        }
    }

    private void OnBlow(GameWorld world, BlowLanded blow)
    {
        IReadOnlyList<int> subjects = world.Content.Rules.Interface.HitEffectAnchors;
        int subject = blow.Category >= 0 && blow.Category < subjects.Count ? subjects[blow.Category] : 0;
        StartEffect(blow.Slot, subject, blow.Anchor, blow.Style);
        const int UnitCategory = 1, AnimalCategory = 5;
        if (blow.Category == UnitCategory)
        {
            Labels.Add(FloatingLabels.UnitKey(blow.Slot), -blow.Damage);
        }
        else if (blow.Category == AnimalCategory)
        {
            Labels.Add(FloatingLabels.AnimalKey(blow.Slot), -blow.Damage);
        }
    }

    /// <summary>Lays out the arc of a projectile just thrown, as the original does at the throw.</summary>
    private void LayOut(GameWorld world, ProjectileThrown thrown)
    {
        Unit thrower = world.Unit(thrown.Unit);
        ProjectileArc? arc = ArcOf(world, thrower, thrown.StartHeight, thrown.ImpactDrop);
        if (arc != null)
        {
            Arcs[thrower.Id] = arc;
        }
    }

    /// <summary>
    /// The arc the original computes when <paramref name="thrower"/> throws: a parabola over the
    /// pixel distance to its destination, flattened by its facing, starting
    /// <paramref name="startHeight"/> up and dropping <paramref name="impactDrop"/> on the way;
    /// null when there is no flight to lay out.
    /// </summary>
    public static ProjectileArc? ArcOf(GameWorld world, Unit thrower, int startHeight, int impactDrop)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(thrower);
        var weapons = world.Content.Rules.Weapons;
        if (thrower.Weapon < 0 || thrower.Weapon >= weapons.Weapons.Count)
        {
            return null;
        }
        // A weapon that is not thrown (a tower guard's) throws stones, as the rules do.
        int kindIndex = Math.Max(weapons.Weapons[thrower.Weapon].ProjectileKind, 0);
        int steps = thrower.ProjectileFlightSteps;
        if (kindIndex >= weapons.Projectiles.Count || steps <= 0 || steps > MaxFlightSteps)
        {
            return null;
        }
        var kind = weapons.Projectiles[kindIndex];
        var distances = world.Content.Rules.Movement.CellDistances;
        if (distances.Count == 0 || thrower.Facing < 0 || thrower.Facing >= kind.ArcDivisorByFacing.Count)
        {
            return null;
        }
        // A throw further than the distance table is timed at the table's edge, as the rules do.
        int dy = Math.Min(Math.Abs(thrower.DestinationY - thrower.Y), distances.Count - 1);
        int dx = Math.Min(Math.Abs(thrower.DestinationX - thrower.X), distances[dy].Count - 1);
        int pixels = distances[dy][dx];
        int speed = kind.Speed;
        int divisor = kind.ArcDivisorByFacing[thrower.Facing];
        if (pixels == 0 || divisor == 0)
        {
            return null;
        }
        var heights = new int[steps + 1];
        var frames = new int[steps + 1];
        for (int s = 0; s <= steps; s++)
        {
            heights[s] = unchecked((sbyte)((((-speed * s * speed * s) + (pixels * speed * s)) / divisor) + startHeight - (impactDrop * s / steps)));
            frames[s] = kindIndex == TomahawkKind
                ? s & 1
                : Math.Clamp(((pixels - (2 * speed * s) - (impactDrop * divisor / pixels)) * FrameSlope / divisor) + FrameCentre, 0, FrameClampMax);
        }
        return new ProjectileArc(heights, frames);
    }

    /// <summary>
    /// The start height and drop of a projectile thrown during <paramref name="action"/>, for
    /// throws the view did not see happen (a loaded game): the original's ranged attacks 5..9 throw
    /// from the ground, the tower guards from 24 pixels up.
    /// </summary>
    public static (int StartHeight, int ImpactDrop) ThrowHeights(int action)
    {
        const int FirstRanged = 5, LastRanged = 9, TowerHeight = 24, TowerDrop = 37;
        if (action >= FirstRanged && action <= LastRanged)
        {
            return (0, RangedDrops[action - FirstRanged]);
        }
        return (TowerHeight, TowerDrop);
    }

    private void DropLandedArcs(GameWorld world)
    {
        landed.Clear();
        foreach (int unit in Arcs.Keys)
        {
            if (unit < 1 || unit > world.UnitSlots || world.Unit(unit).ProjectileFlightSteps <= 0)
            {
                landed.Add(unit);
            }
        }
        foreach (int unit in landed)
        {
            Arcs.Remove(unit);
        }
    }
}

/// <summary>
/// The original's floating numbers: hit point changes add up per unit (animals from 101) and are
/// shown every third frame as a number that rises and fades out. The units and animals of a world
/// with more tribes than the original's two have keys of their own beyond the original's
/// (<see cref="UnitKey"/>, <see cref="AnimalKey"/>), shown after the original's in key order.
/// </summary>
public sealed class FloatingLabels
{
    /// <summary>Numbers shown at once.</summary>
    public const int RingSize = 20;

    /// <summary>Accumulated changes: units 1..100, animals 101..121.</summary>
    public const int Subjects = 122;

    /// <summary>A number has risen this far when it goes.</summary>
    public const int TopRise = 15;

    /// <summary>Colours of the small digits: gains, the viewer's units hurt, everything else hurt.</summary>
    public const int Positive = 2, PlayerUnit = 1, Other = 0;
    private const int LastPlayerUnit = 50;
    private const int LabelEvery = 3;
    private const int OriginalUnits = 100, OriginalAnimals = 27, FirstAnimalKey = 101;
    private const int ExtendedUnitKeys = 1 << 24, ExtendedAnimalKeys = 1 << 28;

    private readonly SortedDictionary<int, sbyte> extended = new();
    private readonly List<int> shown = new();

    /// <summary>Changes not shown yet (the original's keys).</summary>
    public sbyte[] Pending { get; } = new sbyte[Subjects];

    /// <summary>The key of unit <paramref name="unit"/>'s numbers.</summary>
    public static int UnitKey(int unit) => unit <= OriginalUnits ? unit : ExtendedUnitKeys + unit;

    /// <summary>The key of animal slot <paramref name="animal"/>'s numbers.</summary>
    public static int AnimalKey(int animal) => animal < OriginalAnimals ? FirstAnimalKey + animal : ExtendedAnimalKeys + animal;

    /// <summary>The numbers (ring order from <see cref="First"/> to <see cref="End"/>).</summary>
    public Label[] Ring { get; } = new Label[RingSize];

    /// <summary>Ring index of the oldest number.</summary>
    public int First { get; set; }

    /// <summary>Ring index after the newest number.</summary>
    public int End { get; set; }

    /// <summary>Adds <paramref name="delta"/> to the change shown over <paramref name="subject"/>.</summary>
    public void Add(int subject, int delta)
    {
        if (subject >= 0 && subject < Subjects)
        {
            Pending[subject] = unchecked((sbyte)(Pending[subject] + delta));
        }
        else if (subject >= ExtendedUnitKeys)
        {
            extended[subject] = unchecked((sbyte)((extended.TryGetValue(subject, out sbyte sum) ? sum : 0) + delta));
        }
    }

    /// <summary>One frame: numbers rise, and every third frame a subject's change becomes a number.</summary>
    public void Step(int frame)
    {
        for (int k = First; k != End; k = (k + 1) % RingSize)
        {
            Label label = Ring[k];
            label.Rise = (short)(label.Rise + (label.Rise / 4) + 1);
            if (label.Rise >= TopRise)
            {
                label.Key = 0;
                First = (First + 1) % RingSize;
            }
            Ring[k] = label;
        }
        for (int subject = 0; subject < Subjects; subject++)
        {
            if (Pending[subject] != 0 && subject % LabelEvery == frame % LabelEvery)
            {
                int value = Pending[subject];
                Show(subject, (value > 0 ? "+" : string.Empty) + value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    value > 0 ? Positive : (subject <= LastPlayerUnit ? PlayerUnit : Other));
                Pending[subject] = 0;
            }
        }
        if (extended.Count == 0)
        {
            return;
        }
        shown.Clear();
        foreach ((int key, sbyte value) in extended)
        {
            if (value == 0 || key % LabelEvery == frame % LabelEvery)
            {
                if (value != 0)
                {
                    Show(key, (value > 0 ? "+" : string.Empty) + value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        value > 0 ? Positive : Other);
                }
                shown.Add(key);
            }
        }
        foreach (int key in shown)
        {
            extended.Remove(key);
        }
    }

    /// <summary>Starts a number unless the ring is full (the original then drops it).</summary>
    private void Show(int key, string text, int colour)
    {
        if (Ring[End].Key != 0)
        {
            return;
        }
        Ring[End] = new Label { Key = key, Rise = 0, Colour = colour, Text = text };
        End = (End + 1) % RingSize;
    }

    /// <summary>One floating number.</summary>
    public struct Label
    {
        /// <summary>The key of the unit or animal (see <see cref="UnitKey"/>, <see cref="AnimalKey"/>); 0 for a free slot.</summary>
        public int Key;

        /// <summary>How far it has risen (drawn 2 pixels per step up).</summary>
        public short Rise;

        /// <summary>Colour of the small digits as the sheet stacks them: 0 yellow (others), 1 red (the player's units hurt), 2 cyan (gains).</summary>
        public int Colour;

        /// <summary>The characters.</summary>
        public string Text;
    }
}
