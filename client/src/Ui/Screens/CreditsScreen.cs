using System.Text.Json;
using Godot;
using Tribes.Client.Ui.Art;
using Tribes.Client.Ui.Text;

namespace Tribes.Client.Ui.Screens;

/// <summary>
/// Credits of the game and the licences of the third-party parts, from
/// content/client/credits.json: <c>{"sections": [{"title": {lang: text}, "lines": [{lang: text}]}],
/// "licences": [{"name", "version", "licence", "copyright", "text"}]}</c>. Each licence's full text
/// opens under its entry.
/// </summary>
public partial class CreditsScreen : ScreenBase
{
    private const string CreditsFile = "res://content/client/credits.json";
    private const int PortraitPiece = 1726;

    private VBoxContainer column = null!;

    /// <inheritdoc/>
    protected override void Build()
    {
        VBoxContainer content = BuildSheet("credits.title", out _);
        column = UiFactory.Column(4);
        content.AddChild(UiFactory.Scroll(column));
        Fill();
    }

    /// <inheritdoc/>
    protected override void RefreshTexts()
    {
        foreach (Node child in column.GetChildren())
        {
            child.QueueFree();
        }
        Fill();
    }

    private void Fill()
    {
        string json = Godot.FileAccess.GetFileAsString(CreditsFile);
        if (json.Length == 0)
        {
            column.AddChild(UiFactory.Label("credits.missing", PixelTheme.Variations.Muted));
            return;
        }
        var art = new TextureRect
        {
            Texture = ImageTexture.CreateFromImage(OriginalArt.Shared.Rasterize(OriginalArt.Shared.Composers.Sprites.ComposePiece(PortraitPiece).Parts, out _)),
            StretchMode = TextureRect.StretchModeEnum.KeepCentered,
            SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
        };
        column.AddChild(art);
        using JsonDocument document = JsonDocument.Parse(json);
        foreach (JsonElement section in document.RootElement.GetProperty("sections").EnumerateArray())
        {
            Label heading = UiFactory.Plain(Local(section.GetProperty("title")), PixelTheme.Variations.Heading);
            heading.HorizontalAlignment = HorizontalAlignment.Center;
            heading.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            column.AddChild(heading);
            foreach (JsonElement line in section.GetProperty("lines").EnumerateArray())
            {
                Label text = UiFactory.Plain(Local(line));
                text.HorizontalAlignment = HorizontalAlignment.Center;
                text.AutowrapMode = TextServer.AutowrapMode.WordSmart;
                column.AddChild(text);
            }
            column.AddChild(new Control { CustomMinimumSize = new Vector2(0, 6) });
        }
        column.AddChild(new HSeparator());
        Label licences = UiFactory.Label("credits.licences", PixelTheme.Variations.Heading);
        licences.HorizontalAlignment = HorizontalAlignment.Center;
        column.AddChild(licences);
        foreach (JsonElement licence in document.RootElement.GetProperty("licences").EnumerateArray())
        {
            column.AddChild(LicenceEntry(licence));
        }
    }

    private static Control LicenceEntry(JsonElement licence)
    {
        VBoxContainer entry = UiFactory.Column(2);
        HBoxContainer top = UiFactory.Line(6);
        VBoxContainer names = UiFactory.Column(0);
        names.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        Label title = UiFactory.Plain($"{licence.GetProperty("name").GetString()} {licence.GetProperty("version").GetString()} — {licence.GetProperty("licence").GetString()}");
        title.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        names.AddChild(title);
        Label copyright = UiFactory.Plain(licence.GetProperty("copyright").GetString() ?? "", PixelTheme.Variations.Muted);
        copyright.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        names.AddChild(copyright);
        top.AddChild(names);
        Label text = UiFactory.Plain(licence.GetProperty("text").GetString() ?? "", PixelTheme.Variations.Muted);
        text.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        PanelContainer textBox = UiFactory.Panel(PixelTheme.Variations.Inset, text);
        textBox.Visible = false;
        Button toggle = UiFactory.Button("credits.show_licence");
        toggle.ToggleMode = true;
        toggle.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        toggle.Toggled += on =>
        {
            textBox.Visible = on;
            toggle.Text = on ? "credits.hide_licence" : "credits.show_licence";
        };
        top.AddChild(toggle);
        entry.AddChild(top);
        entry.AddChild(textBox);
        return UiFactory.Panel(PixelTheme.Variations.Slot, entry);
    }

    private static string Local(JsonElement texts)
    {
        if (texts.TryGetProperty(UiText.Current, out JsonElement local))
        {
            return local.GetString() ?? "";
        }
        return texts.TryGetProperty(UiText.DefaultLanguage, out JsonElement fallback) ? fallback.GetString() ?? "" : "";
    }
}
