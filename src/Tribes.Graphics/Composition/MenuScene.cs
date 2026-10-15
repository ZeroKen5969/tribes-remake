using System.Collections.Generic;

namespace Tribes.Graphics.Composition;

/// <summary>
/// The title screen scenery (object graphic 118) with the three villagers the original places in
/// it while a menu is shown (<c>GameCanvas.a_IB</c> sets them up in unit slots 1..3).
/// </summary>
public static class MenuScene
{
    private const int IdleWorkState = 0;
    private const int NoTask = 0;
    private const int Spear = 2;
    private const int Club = 0;

    /// <summary>The three villagers: group, facing, animation, frame cycle, age, appearance bits, weapon.</summary>
    private static readonly (int Group, int Facing, int Animation, int Cycle, int Age, int Bits, int Weapon)[] Villagers =
    {
        (0, 1, 39, 8, 25, 8, VillagerAppearance.BareHands),
        (1, 0, 42, 8, 45, 5, Spear),
        (2, 6, 42, 10, 70, 10, Club),
    };

    /// <summary>
    /// The scenery request: <see cref="ObjectPass.Preview"/> for the part drawn once behind the menu,
    /// <see cref="ObjectPass.Upper"/> for the part redrawn every frame with the villagers. The
    /// ambient animations' clocks also depend on <see cref="ObjectRequest.MapRow"/>, which the
    /// original takes from the camera row left over from the last map.
    /// </summary>
    /// <param name="pass">Preview or upper pass.</param>
    /// <param name="tick">Game frame counter; it drives the villagers' animations.</param>
    public static ObjectRequest Request(ObjectPass pass, int tick) =>
        new(ObjectGraphicIds.MenuScene, pass)
        {
            RuleKind = ObjectGraphicIds.MenuScene,
            Tick = tick,
            MenuScreen = true,
            Occupants = Occupants(tick),
        };

    /// <summary>The three villagers at <paramref name="tick"/>.</summary>
    public static IReadOnlyList<Occupant> Occupants(int tick)
    {
        var occupants = new List<Occupant>();
        foreach (var v in Villagers)
        {
            var look = new VillagerAppearance { Age = v.Age, Weapon = v.Weapon, AssignedTask = NoTask }.WithAppearanceBits(v.Bits);
            occupants.Add(new Occupant(v.Group, IdleWorkState, v.Facing, v.Animation, tick % v.Cycle, look));
        }
        return occupants;
    }
}
