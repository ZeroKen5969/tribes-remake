using System.Linq;
using Tribes.Client.World.Scene;
using Tribes.Core.World;
using Xunit;

namespace Tribes.Client.World.Tests;

/// <summary>The smoothing of a dev snapshot's units (built only when the original repository is in original/).</summary>
public sealed partial class PresentationTests
{
    [Fact]
    public void ShortMovesAreSmoothedAndJumpsAreNot()
    {
        Repo repo = Repo.Instance;
        (GameWorld world, _) = repo.Load(repo.Snapshots().First());
        var motion = new MotionTracker();
        var presentation = new ScenePresentation();
        Unit unit = Enumerable.Range(1, world.UnitSlots).Select(world.Unit).First(u => u.InUse);
        motion.OnRuleFrame(world, presentation);
        unit.OffsetX += 4;
        motion.OnRuleFrame(world, presentation);
        Assert.Equal((-4, 0), motion.Offset(MoverKind.Unit, unit.Id, 0f));
        Assert.Equal((-2, 0), motion.Offset(MoverKind.Unit, unit.Id, 0.5f));
        Assert.Equal((0, 0), motion.Offset(MoverKind.Unit, unit.Id, 1f));
        unit.X += 5;
        motion.OnRuleFrame(world, presentation);
        Assert.Equal((0, 0), motion.Offset(MoverKind.Unit, unit.Id, 0f));
    }
}
