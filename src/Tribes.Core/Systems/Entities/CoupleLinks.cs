using System;
using Tribes.Core.Content;
using Tribes.Core.Maps;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Entities;

/// <summary>
/// Breaking up couples: a couple on its way to a hut is dissolved when one of them gets another
/// order, dies or loses the hut, and both go back to what adults do on their own.
/// </summary>
internal sealed class CoupleLinks
{
    /// <summary>The courtship gift a suitor carries (a carried item kind).</summary>
    public const int CourtshipGift = 15;

    /// <summary>Couple state: the two walk into their hut together.</summary>
    public const int WalkingIntoHut = 3;

    /// <summary>A hut birth stage from which the pregnancy goes on without the couple.</summary>
    public const int FirstPregnancyStage = 3;

    private const int RoleBit = 0x01;
    private const int SlotShift = 1;

    /// <summary>A hut birth stage from which a couple has been given the hut.</summary>
    private const int CoupleAssigned = 1;

    /// <summary>The <see cref="Dissolve"/> mode that releases both partners.</summary>
    public const int ReleaseBoth = -1;
    private const int FreeState = 0;

    private readonly GameWorld world;
    private readonly UnitActions actions;

    /// <summary>Creates the system for <paramref name="world"/>.</summary>
    public CoupleLinks(GameWorld world, UnitActions actions)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        this.actions = actions ?? throw new ArgumentNullException(nameof(actions));
    }

    /// <summary>The couple slot <paramref name="unit"/> belongs to, or -1.</summary>
    public static int CoupleSlot(Unit unit)
    {
        ArgumentNullException.ThrowIfNull(unit);
        return ((unit.PairLink & unit.CoupleLinkMask) >> SlotShift) - 1;
    }

    /// <summary>
    /// Dissolves the couple of <paramref name="unit"/>. With a positive <paramref name="mode"/> only
    /// a couple already walking into its hut is dissolved; with a negative one the unit itself is
    /// released like its partner (otherwise only the partner is).
    /// </summary>
    public void Dissolve(Unit unit, int mode)
    {
        ArgumentNullException.ThrowIfNull(unit);
        int slot = CoupleSlot(unit);
        if (slot < 0 || slot >= world.Couples.Count)
        {
            return;
        }
        Couple couple = world.Couples[slot];
        if (mode > 0 && couple.State != WalkingIntoHut)
        {
            return;
        }
        couple.State = FreeState;
        int partner = (unit.PairLink & RoleBit) != 0 ? couple.Man : couple.Woman;
        if (partner > 0)
        {
            ReleasePartner(world.Unit(partner));
        }
        if (mode < 0)
        {
            ReleasePartner(unit);
        }
        if (unit.CarriedItem == CourtshipGift)
        {
            unit.CarriedAmount = 0;
        }
        KeepOnlyDelayedDeath(couple.Man);
        KeepOnlyDelayedDeath(couple.Woman);
        if (couple.Hut > 0 && world.Building(couple.Hut).Aux < FirstPregnancyStage)
        {
            world.Building(couple.Hut).Aux = 0;
        }
    }

    /// <summary>
    /// Releases a unit from its couple's actions: it drops the gift, and a unit busy with a couple
    /// action idles where it stands, or leaves the building it is in.
    /// </summary>
    public void ReleasePartner(Unit unit)
    {
        ArgumentNullException.ThrowIfNull(unit);
        if (unit.CarriedItem == CourtshipGift)
        {
            unit.CarriedAmount = 0;
        }
        if (unit.Action < ActionNumbers.FirstCoupleAction)
        {
            return;
        }
        if (IsOnItsCell(world, unit))
        {
            unit.DestinationX = unit.X;
            unit.DestinationY = unit.Y;
            actions.SetAction(unit, ActionNumbers.Idle);
            return;
        }
        unit.OrderMode = OrderModes.Forced;
        actions.SetAction(unit, ActionNumbers.LeaveBuilding);
    }

    /// <summary>Dissolves the couple heading for <paramref name="hut"/>, if the hut has one.</summary>
    public void CancelCoupleAt(Building hut)
    {
        ArgumentNullException.ThrowIfNull(hut);
        if (hut.Aux < CoupleAssigned)
        {
            return;
        }
        foreach (Couple couple in world.Couples)
        {
            if (couple.State != FreeState && couple.Hut == hut.Id)
            {
                if (couple.Man > 0)
                {
                    Dissolve(world.Unit(couple.Man), ReleaseBoth);
                }
                return;
            }
        }
    }

    /// <summary>Whether <paramref name="unit"/> stands on its own cell (it is not inside a building).</summary>
    public static bool IsOnItsCell(GameWorld world, Unit unit)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(unit);
        var cell = new GridPoint(unit.X, unit.Y);
        return world.Map.Contains(cell) && world.Map.CellAt(cell) == MapCell.UnitAt(unit.Id);
    }

    private void KeepOnlyDelayedDeath(int id)
    {
        if (id > 0)
        {
            Unit unit = world.Unit(id);
            unit.PairLink &= unit.DelayedDeathBit;
        }
    }
}
