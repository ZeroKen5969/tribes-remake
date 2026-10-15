using System;
using Godot;
using Tribes.Core.World;

namespace Tribes.Client.World.Dev;

/// <summary>
/// <c>--minimap</c>: the dev scene shows an endless map's minimap at the top right (the same
/// picture as the match screen's, see <see cref="MinimapPicture"/>); a tap on it moves the camera.
/// </summary>
public partial class WorldTest
{
    private const int MinimapEveryRuleFrames = 4, MinimapPaintFrames = 4;
    private MinimapPanel? minimap;

    private void ReadyMinimap(System.Collections.Generic.Dictionary<string, string> args)
    {
        if (!args.ContainsKey("minimap") || view.World == null || !Scene.MapArea.IsChunked(view.World.Map))
        {
            return;
        }
        minimap = new MinimapPanel(this) { Name = "Minimap" };
        GetNode<CanvasLayer>("Hud").AddChild(minimap);
        minimap.Place(GetViewportRect().Size);
        minimap.Repaint(force: true);
    }

    /// <summary>The minimap control of the dev scene.</summary>
    private sealed partial class MinimapPanel : Control
    {
        private readonly WorldTest scene;
        private readonly MinimapPicture picture;
        private long paintedFrame = long.MinValue;

        public MinimapPanel(WorldTest scene)
        {
            this.scene = scene;
            picture = new MinimapPicture(tribe => scene.view.TribeColour(tribe));
            MouseFilter = MouseFilterEnum.Stop;
        }

        /// <summary>A third of the window's shorter side wide, at the top right.</summary>
        public void Place(Vector2 screen)
        {
            float side = Mathf.Floor(Math.Min(screen.X, screen.Y) / 3);
            Size = new Vector2(side, side * 0.75f).Floor();
            Position = new Vector2(screen.X - Size.X - 4, 4);
        }

        public void Repaint(bool force)
        {
            GameWorld? world = scene.view.World;
            if (world == null || (!force && (picture.Painting || world.Frame - paintedFrame < MinimapEveryRuleFrames)))
            {
                return;
            }
            paintedFrame = world.Frame;
            picture.Start(world, scene.view.Sight!, Size, VisibleMap());
            if (force)
            {
                picture.PaintAll(Inside);
            }
        }

        public override void _Process(double delta)
        {
            Place(GetViewportRect().Size);
            Repaint(force: false);
            picture.PaintBand(MinimapPaintFrames, Inside);
            QueueRedraw();
        }

        public override void _Draw()
        {
            DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.1f, 0.08f, 0.05f));
            picture.Draw(this, VisibleMap(), Colors.Black, Colors.White);
        }

        public override void _GuiInput(InputEvent @event)
        {
            if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } or InputEventScreenTouch { Pressed: true })
            {
                Vector2 at = @event is InputEventMouseButton button ? button.Position : ((InputEventScreenTouch)@event).Position;
                if (picture.MapPointAt(at) is { } point)
                {
                    scene.camera.CentreOn(point);
                }
                AcceptEvent();
            }
        }

        private Rect2 VisibleMap()
        {
            Transform2D toMap = scene.camera.GetCanvasTransform().AffineInverse();
            Rect2 screen = GetViewportRect();
            Vector2 a = toMap * screen.Position, b = toMap * screen.End;
            return new Rect2(a, b - a);
        }

        private bool Inside(Unit unit)
        {
            GameWorld world = scene.view.World!;
            var cell = new Tribes.Core.Content.GridPoint(unit.X, unit.Y);
            return world.Map.Contains(cell) && world.Map.CellAt(cell) != Tribes.Core.Maps.MapCell.UnitAt(unit.Id);
        }
    }
}
