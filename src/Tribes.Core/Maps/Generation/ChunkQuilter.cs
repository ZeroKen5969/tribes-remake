using System;
using System.Collections.Generic;

namespace Tribes.Core.Maps.Generation;

/// <summary>
/// The first draft of a chunk design: square pieces of the original map designs, one after the
/// other in rows, each picked among random pieces as one whose edges fit what is next to it already
/// (the chunk's earlier pieces, its fixed cells and the neighbouring chunks). Among the pieces that
/// fit, the one whose resource nodes come nearest the designs' average is taken: pieces with few
/// nodes fit more often, and the land would otherwise come out poorer than the original maps.
/// </summary>
internal sealed class ChunkQuilter
{
    /// <summary>Cells along a side of a piece.</summary>
    public const int PieceSize = 32;

    /// <summary>Random pieces looked at for each place at most.</summary>
    private const int Candidates = 400;

    /// <summary>Pieces that fit everywhere looked at before the one with the nearest resource nodes is taken.</summary>
    private const int FittingChoices = 8;

    /// <summary>Cells of the original maps' edges left out of pieces (designs are cut off there).</summary>
    private const int SourceMargin = 1;

    private readonly TerrainModel model;
    private readonly List<(SourceMap Source, int X, int Y)> pieces = new();

    /// <summary>The resource node tiles of each piece (in the order of <see cref="pieces"/>).</summary>
    private readonly List<int> resources = new();

    /// <summary>The resource node tiles of a piece of the designs on average.</summary>
    private readonly int averageResources;

    /// <summary>Copies pieces of <paramref name="model"/>'s designs.</summary>
    public ChunkQuilter(TerrainModel model)
    {
        this.model = model;
        long resourceTiles = 0, cells = 0;
        foreach (SourceMap source in model.Sources)
        {
            int[,] sums = ResourceSums(source);
            for (int y = SourceMargin; y + PieceSize <= source.Height - SourceMargin; y++)
            {
                for (int x = SourceMargin; x + PieceSize <= source.Width - SourceMargin; x++)
                {
                    pieces.Add((source, x, y));
                    resources.Add(sums[y + PieceSize, x + PieceSize] - sums[y, x + PieceSize] - sums[y + PieceSize, x] + sums[y, x]);
                }
            }
            int width = source.Width - (2 * SourceMargin), height = source.Height - (2 * SourceMargin);
            if (width > 0 && height > 0)
            {
                resourceTiles += sums[source.Height - SourceMargin, source.Width - SourceMargin] - sums[SourceMargin, source.Width - SourceMargin]
                    - sums[source.Height - SourceMargin, SourceMargin] + sums[SourceMargin, SourceMargin];
                cells += (long)width * height;
            }
        }
        averageResources = cells == 0 ? 0 : (int)Math.Round(resourceTiles * (double)(PieceSize * PieceSize) / cells);
    }

    /// <summary>Resource node tiles of <paramref name="source"/> above and left of each corner: [y, x] counts rows 0..y-1, columns 0..x-1.</summary>
    private int[,] ResourceSums(SourceMap source)
    {
        var sums = new int[source.Height + 1, source.Width + 1];
        for (int y = 0; y < source.Height; y++)
        {
            for (int x = 0; x < source.Width; x++)
            {
                sums[y + 1, x + 1] = sums[y, x + 1] + sums[y + 1, x] - sums[y, x] + (model.Resource[source.TileAt(x, y)] ? 1 : 0);
            }
        }
        return sums;
    }

    /// <summary>Fills every unlocked cell of the chunk with pieces.</summary>
    public void Fill(ChunkCanvas canvas, GameRandom random)
    {
        for (int y = 0; y < ChunkCanvas.Size; y += PieceSize)
        {
            for (int x = 0; x < ChunkCanvas.Size; x += PieceSize)
            {
                Place(canvas, x, y, random);
            }
        }
    }

    private void Place(ChunkCanvas canvas, int x0, int y0, GameRandom random)
    {
        Surroundings around = Surroundings.Of(canvas, x0, y0);
        int bestIndex = 0, fewest = int.MaxValue, fitting = 0, nearest = int.MaxValue;
        for (int k = 0; k < Candidates && fitting < FittingChoices; k++)
        {
            int index = random.NextMasked(int.MaxValue, pieces.Count);
            // Counting stops past the fewest so far: a tie is an exact count, a worse piece just more.
            int misfits = Misfits(around, pieces[index], fewest == int.MaxValue ? int.MaxValue : fewest + 1);
            int off = Math.Abs(resources[index] - averageResources);
            if (misfits < fewest)
            {
                (bestIndex, fewest, nearest) = (index, misfits, off);
                fitting = misfits == 0 ? 1 : 0;
            }
            else if (misfits == fewest)
            {
                fitting += misfits == 0 ? 1 : 0;
                if (off < nearest)
                {
                    (bestIndex, nearest) = (index, off);
                }
            }
        }
        (SourceMap Source, int X, int Y) best = pieces[bestIndex];
        for (int dy = 0; dy < PieceSize; dy++)
        {
            for (int dx = 0; dx < PieceSize; dx++)
            {
                if (!canvas.IsLocked(x0 + dx, y0 + dy))
                {
                    canvas[x0 + dx, y0 + dy] = best.Source.TileAt(best.X + dx, best.Y + dy);
                }
            }
        }
    }

    /// <summary>
    /// How many neighbour pairs along the piece's edge the original maps never have, plus its cells
    /// that differ from a locked cell; counting stops at <paramref name="enough"/>.
    /// </summary>
    private int Misfits(Surroundings around, (SourceMap Source, int X, int Y) piece, int enough)
    {
        int misfits = 0;
        SourceMap source = piece.Source;
        for (int i = 0; i < PieceSize && misfits < enough; i++)
        {
            misfits += Misfit(around.Left[i], TerrainModel.Right, source.TileAt(piece.X, piece.Y + i));
            misfits += Misfit(around.Right[i], TerrainModel.Left, source.TileAt(piece.X + PieceSize - 1, piece.Y + i));
            misfits += Misfit(around.Above[i], TerrainModel.Down, source.TileAt(piece.X + i, piece.Y));
            misfits += Misfit(around.Below[i], TerrainModel.Up, source.TileAt(piece.X + i, piece.Y + PieceSize - 1));
        }
        foreach ((int dx, int dy, int tile) in around.Locked)
        {
            misfits += tile != source.TileAt(piece.X + dx, piece.Y + dy) ? 1 : 0;
        }
        return misfits;
    }

    /// <summary>1 when <paramref name="tile"/> may not stand in <paramref name="direction"/> of <paramref name="beside"/> (no tile there: 0).</summary>
    private int Misfit(int beside, int direction, int tile) => beside >= 0 && !model.Fits(beside, direction, tile) ? 1 : 0;

    /// <summary>What a piece's place has around it: the tiles beside each edge (negative: none) and its locked cells.</summary>
    private sealed record Surroundings(int[] Left, int[] Right, int[] Above, int[] Below, List<(int Dx, int Dy, int Tile)> Locked)
    {
        public static Surroundings Of(ChunkCanvas canvas, int x0, int y0)
        {
            var around = new Surroundings(new int[PieceSize], new int[PieceSize], new int[PieceSize], new int[PieceSize], new());
            for (int i = 0; i < PieceSize; i++)
            {
                around.Left[i] = TileAt(canvas, x0 - 1, y0 + i);
                around.Right[i] = TileAt(canvas, x0 + PieceSize, y0 + i);
                around.Above[i] = TileAt(canvas, x0 + i, y0 - 1);
                around.Below[i] = TileAt(canvas, x0 + i, y0 + PieceSize);
            }
            for (int dy = 0; dy < PieceSize; dy++)
            {
                for (int dx = 0; dx < PieceSize; dx++)
                {
                    if (canvas.IsLocked(x0 + dx, y0 + dy))
                    {
                        around.Locked.Add((dx, dy, canvas[x0 + dx, y0 + dy]));
                    }
                }
            }
            return around;
        }

        private static int TileAt(ChunkCanvas canvas, int x, int y) => ChunkCanvas.OnCanvas(x, y) ? canvas[x, y] : ChunkCanvas.Open;
    }
}
