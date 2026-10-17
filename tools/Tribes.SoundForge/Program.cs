using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using MeltySynth;
using OggVorbisEncoder;

namespace Tribes.SoundForge;

/// <summary>
/// Renders every sound of <see cref="Recipes.All"/> with a General MIDI SoundFont into
/// <c>ASSETS/sfx/NAME.ogg</c> and lists them in <c>CLIENT/sounds.json</c>.
/// </summary>
/// <remarks>usage: Tribes.SoundForge --soundfont FILE.sf2 --assets data/assets --client data/client</remarks>
internal static class Program
{
    private const int SampleRate = 44100;
    private const int Channels = 2;
    private const float Quality = 0.6f;
    private const int BlockSamples = 64;
    private const double FadeOutMs = 40;
    private const int DrumChannel = 9;
    private const int SerialBase = 0x53465842;

    private static int Main(string[] args)
    {
        string? font = Option(args, "--soundfont");
        string? assets = Option(args, "--assets");
        string? client = Option(args, "--client");
        if (font is null || assets is null || client is null)
        {
            Console.Error.WriteLine("usage: Tribes.SoundForge --soundfont FILE.sf2 --assets data/assets --client data/client");
            return 2;
        }
        var soundFont = new SoundFont(font);
        string folder = Path.Combine(assets, "sfx");
        Directory.CreateDirectory(folder);
        var listed = new List<object>();
        for (int i = 0; i < Recipes.All.Count; i++)
        {
            SoundRecipe recipe = Recipes.All[i];
            (float[] left, float[] right) = Render(soundFont, recipe);
            File.WriteAllBytes(Path.Combine(folder, recipe.Name + ".ogg"), EncodeOgg(left, right, SerialBase + i));
            double seconds = Math.Round(left.Length / (double)SampleRate, 3);
            listed.Add(new { name = recipe.Name, ogg = "sfx/" + recipe.Name + ".ogg", seconds, cooldownMs = recipe.CooldownMs });
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{recipe.Name}: {seconds:0.000} s"));
        }
        string json = JsonSerializer.Serialize(new { format = "tribes-sounds/1", sounds = listed }, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(client, "sounds.json"), json.Replace("\r\n", "\n", StringComparison.Ordinal) + "\n");
        return 0;
    }

    private static string? Option(string[] args, string name)
    {
        int at = Array.IndexOf(args, name);
        return at >= 0 && at + 1 < args.Length ? args[at + 1] : null;
    }

    /// <summary>
    /// Plays the notes on a synthesizer in steps of <see cref="BlockSamples"/>, then fades the end out
    /// and scales the whole to the recipe's peak.
    /// </summary>
    private static (float[] Left, float[] Right) Render(SoundFont font, SoundRecipe recipe)
    {
        var synthesizer = new Synthesizer(font, SampleRate);
        int length = (int)(recipe.LengthMs * SampleRate / 1000);
        var left = new float[length];
        var right = new float[length];
        var channels = new Dictionary<int, int>();
        var events = new List<(int At, Action Do)>();
        foreach (Note note in recipe.Notes)
        {
            int channel = note.Instrument == Recipes.Drum ? DrumChannel : ChannelFor(channels, note.Instrument, synthesizer);
            int start = (int)(note.AtMs * SampleRate / 1000);
            int end = (int)((note.AtMs + note.LengthMs) * SampleRate / 1000);
            events.Add((start, () => synthesizer.NoteOn(channel, note.Key, note.Velocity)));
            events.Add((end, () => synthesizer.NoteOff(channel, note.Key)));
        }
        events.Sort((a, b) => a.At.CompareTo(b.At));
        int next = 0;
        for (int at = 0; at < length; at += BlockSamples)
        {
            while (next < events.Count && events[next].At <= at)
            {
                events[next++].Do();
            }
            int count = Math.Min(BlockSamples, length - at);
            synthesizer.Render(left.AsSpan(at, count), right.AsSpan(at, count));
        }
        int fade = (int)(FadeOutMs * SampleRate / 1000);
        for (int i = 0; i < fade && i < length; i++)
        {
            float gain = i / (float)fade;
            left[length - 1 - i] *= gain;
            right[length - 1 - i] *= gain;
        }
        float peak = Math.Max(left.Max(Math.Abs), right.Max(Math.Abs));
        const float Silent = 1e-4f;
        if (peak < Silent)
        {
            throw new InvalidDataException($"{recipe.Name} renders silent: the SoundFont lacks one of its instruments");
        }
        float scale = (float)recipe.Peak / peak;
        for (int i = 0; i < length; i++)
        {
            left[i] *= scale;
            right[i] *= scale;
        }
        return (left, right);
    }

    /// <summary>The melodic channel playing <paramref name="program"/>, set up the first time it is asked for.</summary>
    private static int ChannelFor(Dictionary<int, int> channels, int program, Synthesizer synthesizer)
    {
        if (!channels.TryGetValue(program, out int channel))
        {
            channel = channels.Count >= DrumChannel ? channels.Count + 1 : channels.Count;
            channels[program] = channel;
            const int ProgramChange = 0xC0;
            synthesizer.ProcessMidiMessage(channel, ProgramChange, program, 0);
        }
        return channel;
    }

    /// <summary>Encodes stereo samples as Ogg Vorbis (variable bit rate), as the music is.</summary>
    private static byte[] EncodeOgg(float[] left, float[] right, int serial)
    {
        using var output = new MemoryStream();
        var info = VorbisInfo.InitVariableBitRate(Channels, SampleRate, Quality);
        var ogg = new OggStream(serial);
        var comments = new Comments();
        comments.AddTag("TITLE", "Prehistoric Tribes");
        ogg.PacketIn(HeaderPacketBuilder.BuildInfoPacket(info));
        ogg.PacketIn(HeaderPacketBuilder.BuildCommentsPacket(comments));
        ogg.PacketIn(HeaderPacketBuilder.BuildBooksPacket(info));
        FlushPages(ogg, output, true);
        var state = ProcessingState.Create(info);
        float[][] samples = [left, right];
        const int EncodeBlock = 1024;
        for (int start = 0; start < left.Length; start += EncodeBlock)
        {
            state.WriteData(samples, Math.Min(EncodeBlock, left.Length - start), start);
            DrainPackets(ogg, state, output);
        }
        state.WriteEndOfStream();
        DrainPackets(ogg, state, output);
        FlushPages(ogg, output, true);
        return output.ToArray();
    }

    private static void DrainPackets(OggStream ogg, ProcessingState state, Stream output)
    {
        while (!ogg.Finished && state.PacketOut(out OggPacket packet))
        {
            ogg.PacketIn(packet);
            FlushPages(ogg, output, false);
        }
    }

    private static void FlushPages(OggStream ogg, Stream output, bool force)
    {
        while (ogg.PageOut(out OggPage page, force))
        {
            output.Write(page.Header, 0, page.Header.Length);
            output.Write(page.Body, 0, page.Body.Length);
        }
    }
}
