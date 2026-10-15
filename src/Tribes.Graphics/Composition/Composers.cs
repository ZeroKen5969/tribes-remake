using System;
using Tribes.Graphics.Model;

namespace Tribes.Graphics.Composition;

/// <summary>All composers of one catalog, wired together.</summary>
public sealed class Composers
{
    /// <summary>Creates the composers for <paramref name="catalog"/> (validated first).</summary>
    public Composers(SpriteCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        Sprites = new SpriteComposer(catalog);
        Bodies = new BodyComposer(Sprites);
        Objects = new ObjectComposer(Sprites, Bodies);
        Terrain = new TerrainComposer(Sprites, Objects);
    }

    /// <summary>Pieces, sprites, effects.</summary>
    public SpriteComposer Sprites { get; }

    /// <summary>Villagers and animals.</summary>
    public BodyComposer Bodies { get; }

    /// <summary>Buildings, resource nodes, traps, terrain objects.</summary>
    public ObjectComposer Objects { get; }

    /// <summary>The terrain layer.</summary>
    public TerrainComposer Terrain { get; }
}
