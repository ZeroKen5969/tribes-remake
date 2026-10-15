using System;
using Godot;
using Tribes.Client.Ui.Art;
using Tribes.Client.World;
using Tribes.Core.World;

namespace Tribes.Client.MatchPlay.Hud;

/// <summary>
/// The minimap of an endless map: the land the viewer explored, scaled to fit, or on a very large
/// one the part around the camera, every other tribe in its tribe colour (see
/// <see cref="MinimapPicture"/>). A picture is painted a band of rows per frame and shown once
/// complete, so it costs about the same small amount every frame whatever the size of the map.
/// </summary>
public partial class MinimapView
{
    /// <summary>Frames one picture is spread over.</summary>
    private const int PaintFrames = 4;

    private MinimapPicture? endless;
    private Func<Unit, bool>? isInside;

    /// <summary>A colour from 0xRRGGBB.</summary>
    private static Color Rgb(uint rgb) => new((rgb << 8) | 0xFF);

    private void RefreshEndless(long tick, bool force)
    {
        endless ??= new MinimapPicture(actions.ColourOf);
        isInside ??= IsInside;
        if (force || (!endless.Painting && (tick - drawnTick >= RefreshTicks || tick < drawnTick)))
        {
            drawnTick = tick;
            GameWorld world = actions.World;
            endless.Start(world, actions.Sight, Size, ViewArea);
            if (force)
            {
                endless.PaintAll(isInside);
            }
        }
        QueueRedraw();
    }

    /// <inheritdoc/>
    public override void _Process(double delta)
    {
        if (endless is { Painting: true } picture && isInside != null && picture.PaintBand(PaintFrames, isInside))
        {
            QueueRedraw();
        }
    }

    private bool IsInside(Unit unit) => Logic.BuildingOffers.BuildingInside(actions.World, unit) != 0;

    private void DrawEndless() => endless?.Draw(this, ViewArea, UiPalette.Ink, UiPalette.Paper);

    private void PointEndless(Vector2 local)
    {
        if (endless?.MapPointAt(local) is { } point)
        {
            Pointed?.Invoke(point);
        }
    }
}
