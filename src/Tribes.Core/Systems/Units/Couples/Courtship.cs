using System;
using System.Collections.Generic;
using Tribes.Core.Content.Rules;
using Tribes.Core.Systems.Entities;
using Tribes.Core.Systems.Units.Work;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Units.Couples;

/// <summary>
/// Couples of the player's tribe: a happy adult picks a happy unattached adult of the other gender
/// and a free hut at random, and the two meet in front of the hut; there the man gives his gift,
/// they walk in together, and the hut takes over (the pregnancy goes on without them). A love
/// potion pairs its target without the happiness checks.
/// </summary>
internal sealed class Courtship
{
    /// <summary>Couple states: free, the two on the way to their meeting place, at the meeting, walking into the hut.</summary>
    private const int Free = 0, Courting = 1, Meeting = 2, WalkingIn = 3;

    /// <summary>A unit courts only when a 0..9 roll is at least this.</summary>
    private const int CourtingRollMask = 65535, CourtingRollRange = 10, CourtingRollMin = 4;

    /// <summary>Partners need at least this happiness (without a potion).</summary>
    private const int HappyEnough = 75;

    /// <summary>The couple actions: walking to the meeting place is 110 for the man and 109 for the woman.</summary>
    private const int ManWalksToMeeting = 110, MeetingAction = 109, IntoTheHut = 111;

    /// <summary>The overlay drawn while the man gives the gift.</summary>
    private const int GiftOverlay = -90;

    /// <summary>Hut stages kept in <see cref="Building.Aux"/>: chosen, couple met, couple inside.</summary>
    private const int HutChosen = 1, HutMet = 2, HutEntered = 3;

    /// <summary>The meeting place is this far from the hut's top-left cell (the woman stands one further east).</summary>
    private const int MeetingDx = 1, MeetingDy = 2;

    /// <summary>Dissolving a couple that walked into its hut releases only the partner of the unit that ends it.</summary>
    private const int ReleasePartnerOnly = 0;

    /// <summary>A unit's couple role bit and couple slot bits in <see cref="Unit.PairLink"/>.</summary>
    private const int RoleBit = 1;

    private readonly GameWorld world;
    private readonly UnitActions actions;
    private readonly UnitOrders orders;
    private readonly Occupancy occupancy;
    private readonly CoupleLinks couples;
    private readonly UnitGoals goals;
    private readonly Action<UnitTurn> respond;
    private readonly IReadOnlyList<AnimationDefinition> animations;

    /// <summary>
    /// Creates the system for <paramref name="world"/>; <paramref name="respond"/> makes a unit
    /// respond to what it was just given to do (the units' call cycle).
    /// </summary>
    public Courtship(GameWorld world, UnitActions actions, UnitOrders orders, Occupancy occupancy, CoupleLinks couples,
        UnitGoals goals, Action<UnitTurn> respond)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        this.actions = actions ?? throw new ArgumentNullException(nameof(actions));
        this.orders = orders ?? throw new ArgumentNullException(nameof(orders));
        this.occupancy = occupancy ?? throw new ArgumentNullException(nameof(occupancy));
        this.couples = couples ?? throw new ArgumentNullException(nameof(couples));
        this.goals = goals ?? throw new ArgumentNullException(nameof(goals));
        this.respond = respond ?? throw new ArgumentNullException(nameof(respond));
        animations = world.Content.Rules.Animations.Animations;
    }

    /// <summary>
    /// <paramref name="unit"/>, outside its own turn (the target of a love potion), looks for a
    /// partner as <see cref="SeekPartner(UnitTurn, bool, int)"/> does.
    /// </summary>
    public bool SeekPartner(Unit unit, bool potion, int excluded) => SeekPartner(goals.StartTurn(unit), potion, excluded);

    /// <summary>
    /// The unit of <paramref name="turn"/> looks for a partner (a love potion: <paramref name="potion"/>,
    /// never pairing with <paramref name="excluded"/>, the caster). Returns whether a couple was formed.
    /// </summary>
    public bool SeekPartner(UnitTurn turn, bool potion, int excluded)
    {
        ArgumentNullException.ThrowIfNull(turn);
        Unit unit = turn.Unit;
        TribeState tribe = world.Tribes[unit.Tribe];
        if (!potion && (tribe[TribeCounter.BirthCooldown] >= 0
            || world.Random.NextMasked(CourtingRollMask, CourtingRollRange) < CourtingRollMin || unit.Happiness < HappyEnough))
        {
            return false;
        }
        int pick = world.Random.NextMasked(CourtingRollMask, (tribe[TribeCounter.Population] / 2) + 1);
        Unit? partner = PickPartner(unit, potion, excluded, pick);
        if (partner is null)
        {
            return false;
        }
        int slot = world.FreeCoupleSlot();
        if (slot < 0)
        {
            return false;
        }
        int hutPick = world.Random.NextMasked(CourtingRollMask, (tribe[TribeCounter.Buildings] / 2) + 1);
        Building? hut = PickHut(unit.Tribe, hutPick);
        if (hut is null)
        {
            return false;
        }
        FormCouple(turn, partner, slot, hut);
        return true;
    }

    /// <summary><paramref name="unit"/>'s couple step outside a turn of its own, as <see cref="Advance(UnitTurn)"/>.</summary>
    public bool Advance(Unit unit) => Advance(goals.StartTurn(unit));

    /// <summary>
    /// The couple step of <paramref name="turn"/>'s unit: a member not yet on its way sets out to the
    /// meeting place; at the meeting the gift changes hands and both walk into the hut, where the
    /// hut takes over. Returns false when the unit is in no couple.
    /// </summary>
    public bool Advance(UnitTurn turn)
    {
        ArgumentNullException.ThrowIfNull(turn);
        Unit unit = turn.Unit;
        int slot = CoupleLinks.CoupleSlot(unit);
        if (slot < 0)
        {
            return false;
        }
        Couple couple = world.Couples[slot];
        if (unit.Action < MeetingAction)
        {
            actions.SetAction(unit, ManWalksToMeeting - unit.Gender);
            couple.State = Meeting;
            if (CoupleLinks.IsOnItsCell(world, unit))
            {
                respond(turn);
            }
            else
            {
                orders.FinishOrder(unit, FinishModes.CoupleMove);
            }
        }
        else if (couple.State == Meeting)
        {
            Meet(unit, couple);
        }
        else if (unit.Action == IntoTheHut)
        {
            world.Building(couple.Hut).Aux = HutEntered;
            couples.Dissolve(unit, ReleasePartnerOnly);
        }
        return true;
    }

    /// <summary>The <paramref name="pick"/>-th suitable partner of <paramref name="unit"/>, counting round its tribe's slots.</summary>
    private Unit? PickPartner(Unit unit, bool potion, int excluded, int pick)
    {
        var candidates = new List<Unit>();
        foreach (Unit other in world.UnitsOf(unit.Tribe))
        {
            if ((other.Happiness >= HappyEnough || potion) && (!potion || other.Id != excluded) && other.Gender != unit.Gender
                && other.HitPoints > 0 && other.Age >= UnitAges.Adult && other.Action < MeetingAction
                && (other.PairLink & other.CoupleLinkMask) == 0)
            {
                candidates.Add(other);
            }
        }
        return candidates.Count == 0 ? null : candidates[pick % candidates.Count];
    }

    /// <summary>The <paramref name="pick"/>-th free hut of <paramref name="tribe"/>, counting round its building slots.</summary>
    private Building? PickHut(int tribe, int pick)
    {
        var huts = new List<Building>();
        foreach (Building hut in world.BuildingsOf(tribe))
        {
            if (hut.HitPoints >= 0 && hut.Kind == BuildingKindNumbers.Hut && hut.WorkMode == BuildingWorkMode.Normal
                && hut.Aux == 0 && hut.OccupantCount <= 1)
            {
                huts.Add(hut);
            }
        }
        return huts.Count == 0 ? null : huts[pick % huts.Count];
    }

    private void FormCouple(UnitTurn turn, Unit partner, int slot, Building hut)
    {
        Unit unit = turn.Unit;
        world.Tribes[unit.Tribe][TribeCounter.BirthCooldown] = 0;
        Couple couple = world.Couples[slot];
        couple.State = Courting;
        couple.Man = unit.Id;
        couple.Woman = partner.Id;
        int link = (slot + 1) << 1;
        unit.PairLink += link;
        partner.PairLink += link + RoleBit;
        couple.Hut = hut.Id;
        hut.Aux = HutChosen;
        couple.EntranceX = hut.X + MeetingDx;
        couple.EntranceY = hut.Y + MeetingDy;
        actions.SetAction(unit, ManWalksToMeeting - unit.Gender);
        respond(turn);
    }

    /// <summary>Both at the meeting place: the gift is given, then they walk into the hut.</summary>
    private void Meet(Unit unit, Couple couple)
    {
        Unit first = world.Unit(couple.Man), second = world.Unit(couple.Woman);
        if (first.X != first.DestinationX || first.Y != first.DestinationY
            || second.X != first.DestinationX + 1 || second.Y != first.DestinationY
            || first.Action != MeetingAction || second.Action != MeetingAction || !first.IsCentred || !second.IsCentred)
        {
            return;
        }
        Unit man = first.Gender == 0 ? first : second, woman = first.Gender == 0 ? second : first;
        Building hut = world.Building(couple.Hut);
        if (man.AnimationNumber != UnitAnimations.GivingGift)
        {
            (man.Animation, woman.Animation, man.Action) = (UnitAnimations.GivingGift, UnitAnimations.ReceivingGift, MeetingAction);
            (man.AnimationTick, woman.AnimationTick) = (0, 0);
            (first.Facing, second.Facing, first.BuildingSlot) = (FacingNumbers.East, FacingNumbers.West, GiftOverlay);
            hut.Aux = HutMet;
            occupancy.Evict(hut, all: true);
        }
        else if (unit.AnimationTick == UnitAnimations.GiftHandedFrame && man.CarriedItem == CoupleLinks.CourtshipGift)
        {
            (man.CarriedItem, woman.CarriedItem, man.CarriedAmount, woman.CarriedAmount) = (0, CoupleLinks.CourtshipGift, 0, 1);
        }
        else if (unit.AnimationTick == animations[unit.AnimationNumber].FrameCount - 1)
        {
            foreach (Unit member in new[] { man, woman })
            {
                (member.DestinationX, member.DestinationY, member.Action, member.PathLength) = (hut.X, hut.Y, IntoTheHut, 0);
            }
            couple.State = WalkingIn;
            orders.FinishOrder(first, FinishModes.CoupleMove);
            orders.FinishOrder(second, FinishModes.CoupleMove);
        }
    }
}
