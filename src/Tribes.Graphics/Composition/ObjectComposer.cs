using System;
using System.Collections.Generic;
using Tribes.Graphics.Model;

namespace Tribes.Graphics.Composition;

/// <summary>
/// Composes buildings, resource nodes, traps, terrain objects and the title screen scenery with
/// the original's two object passes (see <see cref="ObjectPass"/>).
/// </summary>
public sealed class ObjectComposer
{
    /// <summary>Number of occupant slots of an object.</summary>
    public const int GroupCount = 8;

    /// <summary>Effect added over a hut's working pair.</summary>
    public const int HeartsEffect = 90;

    private const int FirstComputerSlot = 31;
    private const int FlagGroup = 1;
    private const int DustGroup = 2;
    private const int UpgradingMode = 1;
    private const int RepairingMode = 2;
    private const int AmbientClockPeriod = 120;
    private const int StoreKind = 6;
    private const int StorePileCount = 5;
    private const int StoreFood = 5;
    private const int StoreMeat = 8;
    private const int StoreFish = 9;
    private const int StoreBones = 10;
    private const int StorePileStep = 7;
    private const int StoreSmallPileDivisor = 10;
    private const int StoreFullPile = 4;
    private const int StorePileLevels = 5;
    private const int KitchenKind = 4;
    private const int KitchenFood = 0;
    private const int KitchenMinimumFood = 5;
    private const int KitchenFullFood = 35;
    private const int KitchenFoodStep = 15;
    private const int KitchenFullLevel = 2;
    private const int HutKind = 2;
    private const int HutPairWorkState = 2;
    private const int SeenWorkState = 51;
    private const int MissionThreeAnimation = 116;
    private const int TrainingGroundKind = 10;
    private const int TowerKind = 11;

    private readonly SpriteComposer sprites;
    private readonly BodyComposer bodies;
    private readonly SpriteCatalog catalog;
    private readonly ObjectPart[] parts;

    /// <summary>Creates a composer drawing through <paramref name="sprites"/> and <paramref name="bodies"/>.</summary>
    public ObjectComposer(SpriteComposer sprites, BodyComposer bodies)
    {
        ArgumentNullException.ThrowIfNull(sprites);
        ArgumentNullException.ThrowIfNull(bodies);
        this.sprites = sprites;
        this.bodies = bodies;
        catalog = sprites.Catalog;
        var all = new List<ObjectPart>();
        foreach (ObjectGraphic graphic in catalog.ObjectGraphics)
        {
            if (catalog.ObjectGraphicFirstParts[graphic.Id] != all.Count)
            {
                throw new ArgumentException($"object graphic {graphic.Id} is not stored after the one before it", nameof(sprites));
            }
            all.AddRange(graphic.Parts);
        }
        parts = all.ToArray();
    }

    /// <summary>The parts of one object, relative to its top-left cell.</summary>
    public ObjectComposition Compose(ObjectRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var placed = new List<PlacedOccupant>();
        DrawList drawing = SpriteComposer.Run(request.PaletteVariant, buffer =>
        {
            if (request.Pass == ObjectPass.Ground)
            {
                AppendGround(buffer, request);
            }
            else
            {
                AppendUpper(buffer, request, placed);
            }
        });
        return new ObjectComposition(drawing, placed);
    }

    private ObjectPart Part(int index) => SpriteComposer.Lookup(parts, index, "object part");

    private void AppendGround(PartBuffer buffer, ObjectRequest request)
    {
        int index = SpriteComposer.Lookup(catalog.ObjectGraphicFirstParts, request.Graphic, "object graphic");
        if (request.CellIndex >= 0)
        {
            int counted = 0;
            while (true)
            {
                ObjectPart candidate = Part(index);
                if (candidate.Stage == 0)
                {
                    if (counted == request.CellIndex)
                    {
                        break;
                    }
                    counted++;
                }
                if (candidate.Kind != ObjectPartKind.Ground)
                {
                    break;
                }
                index++;
            }
        }
        ObjectPart part;
        do
        {
            part = Part(index);
            if (part.Stage <= request.Stage)
            {
                sprites.AppendSprite(buffer, part.Id, part.X, part.Y);
            }
            index++;
            part = Part(index);
        }
        while (part.Kind == ObjectPartKind.Ground && part.Stage != 0);
    }

    private void AppendUpper(PartBuffer buffer, ObjectRequest request, List<PlacedOccupant> placed)
    {
        ObjectGraphic graphic = SpriteComposer.Lookup(catalog.ObjectGraphics, request.Graphic, "object graphic");
        int first = catalog.ObjectGraphicFirstParts[graphic.Id];
        var pass = new UpperPass(request);
        for (int k = 0; k < graphic.Parts.Count; k++)
        {
            int index = first + k;
            ObjectPart part = Part(index);
            int x = part.X + part.ShiftX;
            int y = part.Y;
            switch (part.Kind)
            {
                case ObjectPartKind.Ground:
                    if (request.Pass == ObjectPass.Preview && part.Stage == request.Stage)
                    {
                        sprites.AppendSprite(buffer, part.Id, x, y);
                    }
                    break;
                case ObjectPartKind.Fixture:
                    if (part.Stage <= request.Stage)
                    {
                        AppendFixture(buffer, request, pass, part, x, y);
                    }
                    break;
                case ObjectPartKind.IdleAnimation:
                case ObjectPartKind.WorkAnimation:
                case ObjectPartKind.AmbientAnimation:
                    if (request.Pass != ObjectPass.Preview && (part.Stage == 0 || part.Stage <= request.Stage))
                    {
                        AppendAnimation(buffer, request, pass, part, index, x, y);
                    }
                    break;
                default:
                    if (request.Pass != ObjectPass.Preview && !pass.OccupantDrawn[part.Group] && pass.Occupants[part.Group] != null)
                    {
                        AppendOccupant(buffer, request, pass, part, x, y, placed);
                    }
                    break;
            }
        }
    }

    private void AppendFixture(PartBuffer buffer, ObjectRequest request, UpperPass pass, ObjectPart part, int x, int y)
    {
        bool computer = request.SlotIndex >= FirstComputerSlot;
        if (request.RuleKind == StoreKind && pass.FixtureCounter < StorePileCount)
        {
            int resource = SpriteComposer.Lookup(catalog.Tables.StoreStockResources, pass.FixtureCounter, "store pile");
            int amount = resource == catalog.Tables.StoreStockResources[0] ? StoreTotal(request) : Stock(request, resource);
            if (amount > 0 || computer)
            {
                int level = amount / StorePileStep / (pass.FixtureCounter == 0 ? 1 : StoreSmallPileDivisor);
                if (level >= StorePileLevels || computer)
                {
                    level = StoreFullPile;
                }
                sprites.AppendSprite(buffer, part.Id + level, x, y);
            }
            pass.FixtureCounter++;
        }
        else if (request.RuleKind == KitchenKind && pass.FixtureCounter < 1)
        {
            int food = Stock(request, KitchenFood);
            if (computer)
            {
                sprites.AppendSprite(buffer, part.Id + KitchenFullLevel, x, y);
            }
            else if (food >= KitchenMinimumFood)
            {
                int level = food > KitchenFullFood ? KitchenFullLevel : (food - KitchenMinimumFood) / KitchenFoodStep;
                sprites.AppendSprite(buffer, part.Id + level, x, y);
            }
            pass.FixtureCounter++;
        }
        else
        {
            sprites.AppendSprite(buffer, part.Id, x, y);
        }
    }

    /// <summary>The store's first pile counts five stocks (food twice), wrapped to a signed byte as in the original.</summary>
    private static int StoreTotal(ObjectRequest request) => unchecked((sbyte)(Stock(request, StoreFood) + Stock(request, StoreMeat)
        + Stock(request, StoreFood) + Stock(request, StoreFish) + Stock(request, StoreBones)));

    private static int Stock(ObjectRequest request, int resource) =>
        resource >= 0 && resource < request.PlayerStock.Count ? request.PlayerStock[resource] : 0;

    private void AppendAnimation(PartBuffer buffer, ObjectRequest request, UpperPass pass, ObjectPart part, int index, int x, int y)
    {
        Occupant? occupant = pass.Occupants[part.Group];
        int state = occupant?.WorkState ?? 0;
        int effect = part.Id;
        bool draw = false;
        if (part.Kind == ObjectPartKind.AmbientAnimation)
        {
            pass.Clock += request.MapRow + (index * 2);
            while (pass.Clock >= AmbientClockPeriod)
            {
                pass.Clock -= AmbientClockPeriod;
            }
            bool busy = request.SlotWorkMode == UpgradingMode || request.SlotWorkMode == RepairingMode;
            if (part.Group == FlagGroup || (part.Group == DustGroup && busy))
            {
                if (request.SlotIndex >= FirstComputerSlot)
                {
                    effect++;
                }
                draw = true;
            }
            else if (part.Group != DustGroup)
            {
                draw = true;
            }
        }
        else if (request.MissionThreeHeadquarters && part.Group == 0)
        {
            if (effect == MissionThreeAnimation && part.Kind == ObjectPartKind.WorkAnimation)
            {
                draw = true;
                pass.Clock = 0;
            }
        }
        else if (part.Kind == ObjectPartKind.IdleAnimation && (state <= 0 || state >= ObjectRequest.RestingWorkState))
        {
            draw = true;
            if (state == SeenWorkState)
            {
                pass.WorkShown[part.Group] = true;
            }
        }
        else if (part.Kind == ObjectPartKind.WorkAnimation && !pass.WorkShown[part.Group]
            && state >= 1 && state != ObjectRequest.RestingWorkState)
        {
            effect = Part(index + state - 1).Id;
            pass.Clock = occupant?.Frame ?? 0;
            draw = true;
            pass.WorkShown[part.Group] = true;
            if (request.SlotBuildingKind == HutKind && state == HutPairWorkState)
            {
                sprites.AppendEffect(buffer, HeartsEffect, x, y, request.Tick);
            }
        }
        if (draw)
        {
            sprites.AppendEffect(buffer, effect, x, y, pass.Clock);
        }
    }

    private void AppendOccupant(PartBuffer buffer, ObjectRequest request, UpperPass pass, ObjectPart part, int x, int y,
        List<PlacedOccupant> placed)
    {
        Occupant occupant = pass.Occupants[part.Group]!;
        int state = occupant.WorkState;
        int facing = 0;
        bool draw = false;
        bool insidePose = false;
        if (request.SlotIndex < FirstComputerSlot || request.OccupantsVisible)
        {
            if (part.Kind == ObjectPartKind.OccupantResting && state == ObjectRequest.RestingWorkState)
            {
                draw = true;
            }
            else if (part.Kind == ObjectPartKind.OccupantAtWork && state >= 1 && state != ObjectRequest.RestingWorkState)
            {
                int start = SpriteComposer.Lookup(catalog.Tables.OccupantFacingStart, request.RuleKind, "building kind");
                facing = SpriteComposer.Lookup(catalog.Tables.OccupantFacing, start + part.Group, "occupant facing");
                if (facing == -1)
                {
                    facing = occupant.Facing;
                }
                draw = true;
            }
            else if (part.Kind == ObjectPartKind.OccupantIdle && state <= 0)
            {
                draw = true;
                VillagerAppearance look = occupant.Appearance;
                insidePose = (look.CarriedItem != 0 || look.Weapon == VillagerAppearance.Stone)
                    && request.RuleKind != TrainingGroundKind && request.RuleKind != TowerKind;
                if (request.MenuScreen)
                {
                    facing = occupant.Facing;
                }
            }
        }
        if (!draw)
        {
            return;
        }
        var body = new BodyRequest(BodySubject.Villager, occupant.Animation, facing, occupant.Frame)
        {
            Appearance = occupant.Appearance,
            InsideBuildingPose = insidePose,
            LungeFacing = occupant.Facing,
            MapSelectionMarks = request.MapSelectionMarks,
        };
        (AnchorPoint? torso, AnchorPoint? head) = bodies.Append(buffer, body, x, y);
        placed.Add(new PlacedOccupant(part.Group, x, y, facing, torso, head));
        pass.OccupantDrawn[part.Group] = true;
    }

    /// <summary>The working state of one upper pass.</summary>
    private sealed class UpperPass
    {
        internal UpperPass(ObjectRequest request)
        {
            Clock = request.Tick;
            foreach (Occupant occupant in request.Occupants)
            {
                if (occupant.Group < 0 || occupant.Group >= GroupCount)
                {
                    throw new ArgumentException($"occupant group {occupant.Group} is outside 0..{GroupCount - 1}", nameof(request));
                }
                Occupants[occupant.Group] = occupant;
            }
        }

        internal Occupant?[] Occupants { get; } = new Occupant?[GroupCount];

        internal bool[] OccupantDrawn { get; } = new bool[GroupCount];

        internal bool[] WorkShown { get; } = new bool[GroupCount];

        internal int Clock { get; set; }

        internal int FixtureCounter { get; set; }
    }
}
