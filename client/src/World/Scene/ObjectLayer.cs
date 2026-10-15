using System;
using System.Collections.Generic;
using Tribes.Core.Content;
using Tribes.Core.Maps;
using Tribes.Core.World;
using Tribes.Graphics.Composition;
using Tribes.Graphics.Model;

namespace Tribes.Client.World.Scene;

/// <summary>
/// The original's object layer (GameCanvas.u_): everything above the terrain, drawn cell by cell in
/// rows from the top (one row above the view to two below it), each row from the right; per cell
/// the object, unit or animal there, the projectiles over the cell and the cell's fog of war; then
/// the effect list and the dust clouds. The order, the clocks, the fog rules and the original's
/// slips (a picture that fails stops the rest, a palette variant carried over) are kept, so the
/// picture is the original's. It reads the world and never changes it; what only the screen keeps
/// is in <see cref="ScenePresentation"/>.
/// </summary>
public sealed partial class ObjectLayer
{
    private const int Visible = 0, Explored = 1, Unexplored = 2;
    private const int FogGridWordBits = 32;
    private const int HalfFogPiece = 12, FogEdgePiece = 11;
    private const int RallyEffect = 105;
    private const int StoryUnitsHiddenScene = 9;
    private const int FirstStoryFogScene = 27;
    private const int TallNodeKind = 5;
    private const int LastStandingAnimation = 14;
    private static readonly RgbColor Black = RgbColor.FromChannels(0, 0, 0);

    private readonly SceneAssets assets;
    private readonly Composers composers;
    private readonly int cellWidth, cellHeight;

    private SceneRequest request = null!;
    private GameWorld world = null!;
    private ViewerSight sight = null!;
    private ScenePresentation presentation = null!;
    private SceneList output = null!;
    private ViewRect view;
    private int variant;
    private bool storyFogOff;
    private int[] visibility = Array.Empty<int>();
    private bool[] drawn = Array.Empty<bool>();
    private bool remembering;
    private int[] liveShown = Array.Empty<int>();
    private readonly HashSet<int> rememberedDrawn = new();
    private int[,] projectileHeads = new int[0, 0];
    private int[] projectileNext = Array.Empty<int>();
    private int cursorCode = int.MinValue;
    private CellRect bounds;
    private bool chunked;

    /// <summary>
    /// Map rows above the view scanned as well (0: the original's scan, from the row just above
    /// the view). The original skips a building whose footprint lies above that row even when its
    /// picture reaches down onto the screen, so it shows cut off at the top of the screen; with
    /// <see cref="RowsForWholeBuildings"/> every building on screen is drawn whole.
    /// </summary>
    public int ExtraRowsAbove { get; set; }

    /// <summary>Rows above the view that hold every building whose picture can reach onto the screen.</summary>
    public const int RowsForWholeBuildings = 3;

    /// <summary>Creates the layer drawing with <paramref name="assets"/>.</summary>
    public ObjectLayer(SceneAssets assets)
    {
        this.assets = assets ?? throw new ArgumentNullException(nameof(assets));
        composers = assets.Composers;
        cellWidth = assets.CellWidth;
        cellHeight = assets.CellHeight;
    }

    /// <summary>
    /// The failure that stopped the last composition early, as the original's drawing stops (null
    /// when it ran to the end). The original's worlds have a few; a world of the remake's own
    /// (endless, many tribes) should have none.
    /// </summary>
    public Exception? LastFault { get; private set; }

    /// <summary>Composes the object layer of <paramref name="scene"/> into <paramref name="list"/> (cleared first).</summary>
    public void Compose(SceneRequest scene, SceneList list)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(list);
        LastFault = null;
        request = scene;
        world = scene.World;
        sight = scene.Sight is { } given && given.World == world ? given : ViewerSight.Of(world, scene.Looks.Viewer);
        presentation = scene.Presentation;
        view = scene.View;
        output = list;
        list.Clear();
        variant = presentation.PaletteVariant;
        bounds = MapArea.Bounds(world.Map);
        chunked = MapArea.IsChunked(world.Map);
        storyFogOff = world.Script.Active && world.Script.SceneKind < FirstStoryFogScene;
        remembering = sight.RemembersBuildings;
        cursorCode = scene.Marks.CursorCell is { } c && world.Map.Contains(new GridPoint(c.X, c.Y))
            ? CellCode.ToCode(world.Map.CellAt(new GridPoint(c.X, c.Y)))
            : int.MinValue;
        try
        {
            PrepareFrame();
            DrawCells();
            DrawEffects();
            DrawDust();
        }
        catch (Exception e) when (IsDrawingFault(e))
        {
            // The original's object pass stops at the first failure of the frame.
            LastFault = e;
        }
        presentation.PaletteVariant = variant;
    }

    /// <summary>The failures the original's drawing catches (its catch-all blocks), here or in the composers.</summary>
    internal static bool IsDrawingFault(Exception e) =>
        e is IndexOutOfRangeException or ArgumentOutOfRangeException or ArgumentException or DivideByZeroException
            or DrawFailedException or InvalidOperationException;

    /// <summary>
    /// Per frame (GameCanvas.v_): buildings are not drawn yet and the seen ones count as half
    /// visible; then the alarm markers and the trap orders under the objects.
    /// </summary>
    private void PrepareFrame()
    {
        int slots = world.BuildingSlots;
        if (visibility.Length != slots + 1)
        {
            visibility = new int[slots + 1];
            drawn = new bool[slots + 1];
        }
        for (int b = 1; b <= slots; b++)
        {
            drawn[b] = false;
            visibility[b] = world.Building(b).Visibility > 0 ? 5 : world.Building(b).Visibility;
        }
        if (liveShown.Length != slots + 1)
        {
            liveShown = new int[slots + 1];
        }
        else
        {
            Array.Clear(liveShown);
        }
        rememberedDrawn.Clear();
        ReadViewerStock();
        DrawAlarms();
        DrawTrapOrders();
        BuildProjectileLists();
    }

    /// <summary>The rows and cells of the object layer (cells off the map, or land not made yet, are skipped).</summary>
    private void DrawCells()
    {
        int first = -1 - Math.Max(0, ExtraRowsAbove);
        int y = first * cellHeight;
        for (int row = first; row < view.Rows + 3; row++)
        {
            int mapRow = row + view.Row;
            if (mapRow >= bounds.Bottom)
            {
                continue;
            }
            int col = view.Columns;
            while (col >= -1)
            {
                int mapCol = col + view.Column;
                if (mapRow >= bounds.Top && mapCol >= bounds.Left && mapCol < bounds.Right
                    && (!chunked || MapArea.InPlay(world.Map, mapCol, mapRow)))
                {
                    col = DrawCell(row, col, mapRow, mapCol, y);
                }
                col--;
            }
            DrawRallyPoint(row, y);
            y += cellHeight;
        }
    }

    /// <summary>One cell; returns the column the scan goes on from (one less when a tall node on the left was drawn early).</summary>
    private int DrawCell(int row, int col, int mapRow, int mapCol, int y)
    {
        int x = col * cellWidth;
        int fog = storyFogOff ? Visible : FogAt(mapCol, mapRow);
        MapCell cell = world.Map.CellAt(new GridPoint(mapCol, mapRow));
        if (remembering && cell.Content == CellContent.Building && !ShowsLive(cell.Index))
        {
            // Out of sight in a match a building shows only as remembered (below), never as it is now.
            cell = MapCell.Empty;
        }
        int code = CellCode.ToCode(cell);
        int tile = world.Map.TileAt(new GridPoint(mapCol, mapRow));
        int fogColumn = mapCol;
        if (code != CellCode.Empty)
        {
            if (fog >= Unexplored || !TerrainComposer.HasOverlay(tile))
            {
                switch (cell.Content)
                {
                    case CellContent.Unit:
                        if (fog < Explored && !(world.Script.Active && world.Script.SceneKind == StoryUnitsHiddenScene))
                        {
                            (col, fogColumn) = DrawUnitCell(row, col, mapRow, mapCol, x, y, cell.Index, fogColumn);
                        }
                        break;
                    case CellContent.Building:
                        // The original shows a building the player has seen part of whole; in a match every
                        // human tribe has its seen buildings explored whole in its own fog, and the shared
                        // mark must not show one tribe what another has seen.
                        if (fog < Unexplored || (visibility[cell.Index] > 0 && world.Mode == WorldMode.Campaign))
                        {
                            fog = DrawBuildingCell(cell.Index, code, fog, y, mapRow);
                        }
                        break;
                    case CellContent.Animal:
                        if (fog < Explored)
                        {
                            DrawAnimal(cell.Index, x, y);
                        }
                        break;
                    default:
                        DrawOtherCell(cell, code, fog, tile, row, col, mapRow, x, y);
                        break;
                }
            }
            else
            {
                DrawUpper(ObjectRequest.TerrainObject(tile, ObjectPass.Upper) with { Tick = presentation.Cycle, MapRow = mapRow }, x, y);
            }
        }
        if (remembering && fog != Visible && sight.ShowsRememberedAt(mapCol, mapRow, out RememberedBuilding seen)
            && rememberedDrawn.Add(seen.Slot))
        {
            DrawRemembered(seen, mapRow);
        }
        if (col >= 0 && col < view.Columns && row >= 0 && row < view.Rows && projectileHeads[row, col] > 0
            && (!remembering || fog == Visible))
        {
            DrawProjectiles(projectileHeads[row, col]);
            projectileHeads[row, col] = 0;
        }
        DrawFog(fog, fogColumn, mapRow, x, y);
        return col;
    }

    /// <summary>A unit's cell: the trap it is crossing, a tall node just left of it, then the unit.</summary>
    private (int Col, int FogColumn) DrawUnitCell(int row, int col, int mapRow, int mapCol, int x, int y, int unitId, int fogColumn)
    {
        Unit unit = world.Unit(unitId);
        int crossing = unit.NextOccupantOrCellObject;
        int crossingClass = CellClasses.OfCode(crossing, sight);
        if (crossingClass != CellClasses.NotATrap)
        {
            DrawTrapWithUnit(crossing, unitId, x, y);
        }
        if (crossingClass == CellClasses.NotATrap || unit.Animation <= LastStandingAnimation)
        {
            // The original's camera never leaves the map, so its check on the screen column also
            // kept the map column in range; a view wider than the map needs both.
            if (row >= 0 && col - 1 >= 0 && mapCol - 1 >= bounds.Left)
            {
                MapCell left = world.Map.CellAt(new GridPoint(mapCol - 1, mapRow));
                if (left.Content == CellContent.ResourceNode && world.Map.ResourceNodes[left.Index].Kind == TallNodeKind)
                {
                    if (col < view.Columns)
                    {
                        projectileHeads[row, col] = 0;
                    }
                    col--;
                    fogColumn = col + view.Column;
                    DrawNode(left.Index, col * cellWidth, y, mapRow);
                }
            }
            if (sight.Includes(unit.Tribe) || (unit.Status & UnitStatus.Hidden) == 0)
            {
                DrawUnit(unitId, x + unit.OffsetX, y + unit.OffsetY, unit.Facing);
            }
        }
        return (col, fogColumn);
    }

    /// <summary>Nodes, pending traps and spotted traps (the code-dependent rest of a cell).</summary>
    private void DrawOtherCell(MapCell cell, int code, int fog, int tile, int row, int col, int mapRow, int x, int y)
    {
        bool node = cell.Content == CellContent.ResourceNode;
        int trapClass = CellClasses.Of(cell, sight);
        if (fog >= Unexplored || (!node && trapClass != CellClasses.PendingTrap))
        {
            if (fog == Visible && cell.Content == CellContent.SpottedTrap && trapClass == CellClasses.OwnTrap)
            {
                DrawSpottedTrap(tile, row, col, mapRow, x, y);
            }
            return;
        }
        if (node)
        {
            DrawNode(cell.Index, x, y, mapRow);
        }
        else
        {
            DrawUpper(ObjectRequest.Trap(CellClasses.PendingTrapKind, 0, ObjectPass.Upper) with { Tick = presentation.Cycle, MapRow = mapRow }, x, y);
        }
    }

    /// <summary>A trap a scout found blinks: the trap shows half the time, with a flash once per sweep of its row.</summary>
    private void DrawSpottedTrap(int tile, int row, int col, int mapRow, int x, int y)
    {
        const int FlashEffect = 75, FlashStart = 94, FlashEnd = 100, FlashFrames = 6, BlinkPeriod = 10, BlinkOn = 5;
        int sweep = world.RowSweep;
        if (sweep >= FlashStart + mapRow && sweep <= FlashEnd + mapRow)
        {
            AddEffect(FlashEffect, x, y, sweep - (FlashEnd + mapRow) + FlashFrames);
        }
        else if ((sweep + col + row) % BlinkPeriod < BlinkOn)
        {
            AddSprite(tile, x, y);
        }
    }

    /// <summary>The fog of war over a cell: half fog, or the unexplored edge, or black inside unexplored land.</summary>
    private void DrawFog(int fog, int mapCol, int mapRow, int x, int y)
    {
        if (fog == Explored)
        {
            AddPiece(HalfFogPiece, x - 2, y - 4);
        }
        else if (fog == Unexplored)
        {
            // An endless map has no edge: its unexplored land is black up to the explored land.
            bool interior = (chunked || (mapCol != bounds.Left && mapCol != bounds.Right - 1 && mapRow != bounds.Top && mapRow != bounds.Bottom - 1))
                && FogAt(mapCol - 1, mapRow) == Unexplored && FogAt(mapCol + 1, mapRow) == Unexplored
                && FogAt(mapCol, mapRow - 1) == Unexplored && FogAt(mapCol, mapRow + 1) == Unexplored;
            if (interior)
            {
                output.Add(DrawPart.Fill(x, y, cellWidth, cellHeight, Black, -1));
            }
            else
            {
                AddPiece(FogEdgePiece, x - 3, y - 2);
            }
        }
    }

    /// <summary>Whether the building in <paramref name="slot"/> is drawn as it is (worked out once per composition).</summary>
    private bool ShowsLive(int slot)
    {
        if (slot <= 0 || slot >= liveShown.Length)
        {
            return true;
        }
        if (liveShown[slot] == 0)
        {
            liveShown[slot] = sight.ShowsAsItIs(world.Building(slot)) ? 1 : 2;
        }
        return liveShown[slot] == 1;
    }

    /// <summary>The markers of the gathering points of the armies whose marks the viewer sees, drawn after their row.</summary>
    private void DrawRallyPoint(int row, int y)
    {
        IReadOnlyList<int> tribes = sight.Tribes;
        for (int i = 0; i < tribes.Count; i++)
        {
            int tribe = tribes[i];
            ArmyOrders orders = world.Tribes[tribe].Army;
            if (sight.ShowsMarksOf(tribe) && (orders.Rally || orders.AllOut) && orders.PointY - view.Row == row)
            {
                AddEffect(RallyEffect, (orders.PointX - view.Column) * cellWidth, y, presentation.Cycle);
            }
        }
    }

    /// <summary>
    /// The viewer's fog at a map cell (0 visible, 1 explored, 2 unexplored). Like the original's bit
    /// grid it fails outside the grid; an endless map's fog has no grid (land not made yet is unexplored).
    /// </summary>
    private int FogAt(int x, int y)
    {
        IFogView fog = sight;
        if (!chunked)
        {
            GridSize grid = world.Fog(sight.Home).Size;
            int width = (grid.Width + FogGridWordBits - 1) / FogGridWordBits * FogGridWordBits;
            if (x < 0 || y < 0 || x >= width || y >= grid.Height)
            {
                throw new IndexOutOfRangeException($"fog cell {x},{y}");
            }
        }
        var cell = new GridPoint(x, y);
        if (!fog.IsExplored(cell))
        {
            return Unexplored;
        }
        return fog.IsVisible(cell) ? Visible : Explored;
    }
}

/// <summary>A composition the original would have stopped at (a missing picture).</summary>
public sealed class DrawFailedException : Exception
{
    /// <summary>Creates the exception.</summary>
    public DrawFailedException()
    {
    }

    /// <summary>Creates the exception with a message.</summary>
    public DrawFailedException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with a message and a cause.</summary>
    public DrawFailedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
