using System;
using System.Threading.Tasks;
using Godot;
using Tribes.Session;

namespace Tribes.Client.Session;

/// <summary>
/// Takes the save picture without stopping the game: a small viewport that shares the match's 2D
/// world (so it shows the world but none of the interface layers) renders the middle 16:9 part
/// of the screen once at twice the picture size, and it is read back a frame later, when the GPU
/// has finished it, so only a few kilobytes cross from the GPU and nothing waits for the frame.
/// </summary>
public partial class ThumbnailShooter : SubViewport
{
    /// <summary>Picture size.</summary>
    public const int Width = 160, Height = 90;

    /// <summary>The picture is rendered this many times larger, then shrunk (smoother pixel art).</summary>
    public const int Oversample = 2;

    private readonly Viewport? main;

    /// <summary>Main-thread time of the last read back (milliseconds).</summary>
    public double LastReadbackMs { get; private set; }
    private bool busy;

    /// <summary>Needed by Godot.</summary>
    public ThumbnailShooter()
    {
    }

    /// <summary>A shooter of what <paramref name="main"/> shows.</summary>
    public ThumbnailShooter(Viewport main)
    {
        this.main = main;
        Name = "ThumbnailShooter";
        Size = new Vector2I(Width * Oversample, Height * Oversample);
        Disable3D = true;
        TransparentBg = false;
        GuiDisableInput = true;
        RenderTargetUpdateMode = UpdateMode.Disabled;
    }

    /// <inheritdoc />
    public override void _Ready()
    {
        if (main is not null)
        {
            World2D = main.World2D;
        }
    }

    /// <summary>Waits <paramref name="frames"/> drawn frames (to let the match settle before a warm-up picture).</summary>
    public async Task WaitFrames(int frames)
    {
        for (int i = 0; i < frames && IsInsideTree(); i++)
        {
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        }
    }

    /// <summary>The picture of the world as the screen shows it now, or null (no world, or a picture is being taken).</summary>
    public async Task<Pixels?> Capture()
    {
        if (busy || main is null || !IsInsideTree())
        {
            return null;
        }
        busy = true;
        try
        {
            Vector2 screen = main.GetVisibleRect().Size;
            float cropWidth = Math.Min(screen.X, screen.Y * Width / Height);
            float cropHeight = cropWidth * Height / Width;
            var corner = new Vector2((screen.X - cropWidth) / 2, (screen.Y - cropHeight) / 2);
            float scale = Size.X / cropWidth;
            // Screen point p goes to (p - corner) * scale in this viewport.
            var crop = new Transform2D(new Vector2(scale, 0), new Vector2(0, scale), -corner * scale);
            CanvasTransform = crop * main.CanvasTransform;
            RenderTargetUpdateMode = UpdateMode.Once;
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            if (!IsInsideTree())
            {
                return null;
            }
            long readStart = System.Diagnostics.Stopwatch.GetTimestamp();
            Image image = GetTexture().GetImage();
            if (image is null || image.IsEmpty())
            {
                return null;
            }
            if (image.GetFormat() is not (Image.Format.Rgba8 or Image.Format.Rgb8))
            {
                image.Convert(Image.Format.Rgba8);
            }
            int channels = image.GetFormat() == Image.Format.Rgba8 ? 4 : 3;
            var pixels = new Pixels(image.GetWidth(), image.GetHeight(), channels, image.GetData());
            LastReadbackMs = System.Diagnostics.Stopwatch.GetElapsedTime(readStart).TotalMilliseconds;
            return pixels;
        }
        finally
        {
            busy = false;
        }
    }

    /// <summary>The picture cut from a full screenshot (when no frame is left to render one, e.g. while closing).</summary>
    public static Pixels? FromScreenshot(Image? shot)
    {
        if (shot is null || shot.IsEmpty())
        {
            return null;
        }
        int width = shot.GetWidth(), height = shot.GetHeight();
        int cropWidth = Math.Min(width, height * Width / Height);
        int cropHeight = Math.Min(height, width * Height / Width);
        Image crop = shot.GetRegion(new Rect2I((width - cropWidth) / 2, (height - cropHeight) / 2, cropWidth, cropHeight));
        crop.Resize(Width * Oversample, Height * Oversample, Image.Interpolation.Bilinear);
        crop.Convert(Image.Format.Rgba8);
        return new Pixels(crop.GetWidth(), crop.GetHeight(), 4, crop.GetData());
    }
}
