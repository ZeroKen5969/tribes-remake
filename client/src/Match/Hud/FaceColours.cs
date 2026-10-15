using System.Collections.Generic;
using Godot;
using Tribes.Client.World;
using Tribes.Client.World.Scene;

namespace Tribes.Client.MatchPlay.Hud;

/// <summary>
/// Gives a face picture the skin of a tribe colour. The face pictures have a skin palette of their
/// own (not the villagers' ramps), and the data maps each of its shades to the same shade of every
/// tribe colour (<see cref="TribeColours.FaceSkinMap"/>). Which colour a face is drawn in depends on
/// the portrait (the computer's two portraits show its skin, every other the player's), so a face
/// already in its tribe's colour is left as it is.
/// </summary>
public static class FaceColours
{
    /// <summary>Recolours <paramref name="image"/> of face <paramref name="portrait"/> in place for tribe colour <paramref name="colour"/>.</summary>
    public static void Recolour(Image image, int colour, int portrait)
    {
        TribeColours colours = WorldArt.Shared.Colours;
        if (colour < 0 || colour >= colours.Colours.Count || colour == colours.FaceNative(portrait))
        {
            return;
        }
        IReadOnlyDictionary<int, int> map = colours.FaceSkinMap(colour);
        if (map.Count == 0)
        {
            return;
        }
        for (int y = 0; y < image.GetHeight(); y++)
        {
            for (int x = 0; x < image.GetWidth(); x++)
            {
                Color pixel = image.GetPixel(x, y);
                int rgb = (pixel.R8 << 16) | (pixel.G8 << 8) | pixel.B8;
                if (pixel.A > 0 && map.TryGetValue(rgb, out int skin))
                {
                    image.SetPixel(x, y, Color.Color8((byte)(skin >> 16), (byte)((skin >> 8) & 0xFF), (byte)(skin & 0xFF), (byte)pixel.A8));
                }
            }
        }
    }
}
