namespace Tribes.Core.Maps.Generation;

/// <summary>
/// Makes the design of one chunk: connectors locked, pieces of the original maps laid, the seams
/// mended, paths carved until all connectors are joined, and resource nodes topped up to the
/// original maps' share.
/// </summary>
internal sealed class ChunkDesigner
{
    /// <summary>Rounds of carving and mending at most (one round always joins the connectors).</summary>
    private const int CarveRounds = 3;

    private readonly ChunkQuilter quilter;
    private readonly ChunkRepair repair;
    private readonly ChunkConnectivity connectivity;
    private readonly ChunkEnricher enricher;

    /// <summary>Designs by <paramref name="model"/>.</summary>
    public ChunkDesigner(TerrainModel model)
    {
        quilter = new ChunkQuilter(model);
        repair = new ChunkRepair(model);
        connectivity = new ChunkConnectivity(model);
        enricher = new ChunkEnricher(model, connectivity);
    }

    /// <summary>
    /// Fills the chunk cells of <paramref name="canvas"/> (whose ring holds the neighbouring chunks'
    /// edges); returns whether every seam fits the original maps' rules.
    /// </summary>
    public bool Design(ChunkCanvas canvas, GameRandom random)
    {
        connectivity.LockConnectors(canvas);
        quilter.Fill(canvas, random);
        bool mended = repair.Mend(canvas, random);
        for (int round = 0; round < CarveRounds && !connectivity.Connected(canvas); round++)
        {
            connectivity.Carve(canvas);
            mended = repair.Mend(canvas, random);
        }
        enricher.Enrich(canvas, random);
        return mended;
    }
}
