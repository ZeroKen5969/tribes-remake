using System;
using System.Collections.Generic;
using Tribes.Core.Content.Rules;
using Tribes.Core.Systems.Entities;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Story;

/// <summary>
/// The council scene: the chief and up to three of the tribe's best fighters sit together in the
/// chief's hut. The fields the scene changes on each seated unit are saved first and put back when
/// the scene ends.
/// </summary>
internal sealed class Council
{
    /// <summary>The task of the tribe's witch doctor, who may take the first seat after the chief.</summary>
    public const int WitchDoctorTask = ActionNumbers.WitchDoctor;

    /// <summary>The chief's seat.</summary>
    private const int ChiefSeat = 0;

    /// <summary>The first seat after the chief's.</summary>
    private const int FirstMemberSeat = 1;

    /// <summary>The saved value that holds a candidate's rank while the seats are chosen.</summary>
    private const int RankValue = 0;

    private readonly GameWorld world;
    private readonly IReadOnlyList<MeetingOverride> overrides;
    private readonly IReadOnlyList<int> seatAnimations;

    /// <summary>Holds councils in <paramref name="world"/>.</summary>
    public Council(GameWorld world)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        overrides = world.Content.Rules.Units.MeetingOverrides;
        seatAnimations = world.Content.Rules.Units.MeetingSeatAnimations;
    }

    /// <summary>The chief's hut, the player's first building.</summary>
    private Building ChiefsHut => world.Building(world.FirstBuildingSlot(TribeRoles.Player(world)));

    private int Chief => world.Tribes[TribeRoles.Player(world)][TribeCounter.Chief];

    /// <summary>
    /// Seats the chief and the three adults with the best fighting skill (melee or ranged, ties
    /// keep the earlier unit). With <paramref name="witchDoctorFirst"/> the first witch doctor
    /// found takes the first seat after the chief and the seat's unit becomes the witch doctor.
    /// </summary>
    public void Convene(bool witchDoctorFirst)
    {
        ScriptState script = world.Script;
        script.ChiefWasInside = ChiefsHut.FirstOccupant == Chief ? 1 : 0;
        Seat(Chief, ChiefSeat);
        for (int seat = FirstMemberSeat; seat < ScriptState.MeetingSeats; seat++)
        {
            script.Seats[seat].Unit = 0;
            script.Seats[seat].Save(RankValue, 0);
        }
        ChooseMembers(witchDoctorFirst);
        int count = FirstMemberSeat;
        while (count < ScriptState.MeetingSeats && script.Seats[count].Unit != 0)
        {
            int unit = script.Seats[count].Unit;
            Seat(unit, count);
            if (count == FirstMemberSeat && witchDoctorFirst)
            {
                world.Unit(unit).AssignedTask = WitchDoctorTask;
            }
            count++;
        }
        script.MeetingCount = count;
        ChiefsHut.OccupantCount = count;
    }

    /// <summary>Puts every seated unit's fields back and the chief's hut as it was.</summary>
    public void Dismiss()
    {
        ScriptState script = world.Script;
        ChiefsHut.OccupantCount = script.ChiefWasInside;
        for (int seat = script.MeetingCount - 1; seat >= 0; seat--)
        {
            int unit = script.Seats[seat].Unit;
            for (int i = 0; i < overrides.Count && unit != 0; i++)
            {
                EntityProperties.Set(world, overrides[i].Property, unit, script.Seats[seat].Saved(i));
            }
        }
        ChiefsHut.FirstOccupant = script.ChiefWasInside > 0 ? Chief : 0;
    }

    /// <summary>The seated unit of each seat, the chief's seat standing in for empty ones (actor slots 1..4 of the scene).</summary>
    public int SeatedOrChief(int seat)
    {
        MeetingSeat[] seats = world.Script.Seats;
        return seats[seat].Unit != 0 ? seats[seat].Unit : seats[ChiefSeat].Unit;
    }

    private void ChooseMembers(bool witchDoctorFirst)
    {
        MeetingSeat[] seats = world.Script.Seats;
        bool witchDoctorSeated = false;
        foreach (Unit unit in world.UnitsOf(TribeRoles.Player(world)))
        {
            int id = unit.Id;
            if (unit.HitPoints <= 0 || id == Chief || unit.Age < UnitAges.Adult)
            {
                continue;
            }
            int rank = Math.Max(unit.MeleeSkill, unit.RangedSkill);
            bool witchDoctor = witchDoctorFirst && !witchDoctorSeated && unit.AssignedTask == WitchDoctorTask;
            for (int seat = FirstMemberSeat + (witchDoctorFirst && witchDoctorSeated ? 1 : 0); seat < seats.Length; seat++)
            {
                if (seats[seat].Saved(RankValue) < rank || witchDoctor)
                {
                    Insert(seat, id, rank);
                    witchDoctorSeated |= witchDoctor;
                    break;
                }
            }
        }
    }

    private void Insert(int seat, int unit, int rank)
    {
        MeetingSeat[] seats = world.Script.Seats;
        for (int moved = seats.Length - 1; moved > seat; moved--)
        {
            seats[moved].Unit = seats[moved - 1].Unit;
            seats[moved].Save(RankValue, seats[moved - 1].Saved(RankValue));
        }
        seats[seat].Unit = unit;
        seats[seat].Save(RankValue, rank);
    }

    /// <summary>Saves the fields the council changes on <paramref name="unit"/> and seats it.</summary>
    private void Seat(int unit, int seat)
    {
        MeetingSeat place = world.Script.Seats[seat];
        place.Unit = unit;
        if (unit == 0)
        {
            return;
        }
        for (int i = 0; i < overrides.Count; i++)
        {
            MeetingOverride change = overrides[i];
            place.Save(i, EntityProperties.Get(world, change.Property, unit));
            int value = change.Property switch
            {
                EntityProperty.UnitAnimation => seatAnimations[seat],
                EntityProperty.UnitBuildingSlot => seat + 1,
                _ => change.Value,
            };
            EntityProperties.Set(world, change.Property, unit, value);
        }
        if (unit != Chief || world.Script.ChiefWasInside == 0)
        {
            Unit seated = world.Unit(unit);
            seated.NextOccupantOrCellObject = ChiefsHut.FirstOccupant;
            ChiefsHut.FirstOccupant = unit;
        }
    }
}
