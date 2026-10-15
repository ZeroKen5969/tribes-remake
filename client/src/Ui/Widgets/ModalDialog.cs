using System;
using Godot;
using Tribes.Client.Ui.Art;

namespace Tribes.Client.Ui.Widgets;

/// <summary>
/// A message on a parchment sheet over a dark veil, with one row of buttons; works with touch,
/// mouse and keyboard (the first button takes the focus, Esc and Android back close it).
/// </summary>
public partial class ModalDialog : Control
{
    private const string GroupName = "tribes_modal_dialog";
    private const int MaxWidth = 300;

    private VBoxContainer body = null!;
    private Label message = null!;
    private HBoxContainer buttons = null!;
    private Action? cancel;

    /// <summary>The message (plain text or a key).</summary>
    public string Message
    {
        get => message.Text;
        set => message.Text = value;
    }

    /// <summary>
    /// Shows <paramref name="text"/> on <paramref name="layer"/> with buttons (text key, action);
    /// any button closes the dialog. The last button is also what Esc and back do.
    /// </summary>
    public static ModalDialog Show(Control layer, string text, params (string Key, Action? Action)[] choices)
    {
        var dialog = new ModalDialog();
        layer.AddChild(dialog);
        dialog.Message = text;
        foreach ((string key, Action? action) in choices)
        {
            dialog.AddButton(key, action);
        }
        if (choices.Length > 0)
        {
            dialog.cancel = choices[^1].Action;
        }
        dialog.FocusFirst();
        return dialog;
    }

    /// <summary>Whether a dialog is open on <paramref name="layer"/>.</summary>
    public static bool IsOpen(Control layer)
    {
        foreach (Node child in layer.GetChildren())
        {
            if (child is ModalDialog)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Closes the newest dialog on <paramref name="layer"/> as if cancelled.</summary>
    public static void CloseTop(Control layer)
    {
        for (int i = layer.GetChildCount() - 1; i >= 0; i--)
        {
            if (layer.GetChild(i) is ModalDialog dialog)
            {
                dialog.Close(dialog.cancel);
                return;
            }
        }
    }

    /// <inheritdoc/>
    public override void _Ready()
    {
        AddToGroup(GroupName);
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        var veil = new PanelContainer { ThemeTypeVariation = PixelTheme.Variations.Veil, MouseFilter = MouseFilterEnum.Stop };
        AddChild(veil);
        veil.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var center = new CenterContainer();
        AddChild(center);
        center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var sheet = new PanelContainer { ThemeTypeVariation = PixelTheme.Variations.Sheet };
        center.AddChild(sheet);
        body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", 10);
        sheet.AddChild(body);
        message = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            HorizontalAlignment = HorizontalAlignment.Center,
            CustomMinimumSize = new Vector2(Math.Min(MaxWidth, 220), 0),
        };
        body.AddChild(message);
        buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        buttons.AddThemeConstantOverride("separation", 8);
        body.AddChild(buttons);
        Resized += FitWidth;
        CallDeferred(MethodName.FitWidth);
    }

    private void FitWidth()
    {
        float width = Math.Min(MaxWidth, Math.Max(160, Size.X - 48));
        message.CustomMinimumSize = new Vector2(width, 0);
    }

    /// <summary>Adds a button that runs <paramref name="action"/> and closes the dialog.</summary>
    public Button AddButton(string key, Action? action)
    {
        Button button = UiFactory.Button(key, () => Close(action));
        button.CustomMinimumSize = new Vector2(72, PixelTheme.RowHeight);
        buttons.AddChild(button);
        return button;
    }

    /// <summary>Removes all buttons (for dialogs that change into another state).</summary>
    public void ClearButtons()
    {
        foreach (Node child in buttons.GetChildren())
        {
            child.QueueFree();
        }
    }

    private void FocusFirst()
    {
        if (buttons.GetChildCount() > 0 && buttons.GetChild(0) is Control first)
        {
            first.CallDeferred(Control.MethodName.GrabFocus);
        }
    }

    /// <summary>Closes the dialog, then runs <paramref name="then"/>.</summary>
    public void Close(Action? then = null)
    {
        QueueFree();
        then?.Invoke();
    }

    /// <inheritdoc/>
    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed("ui_cancel"))
        {
            GetViewport().SetInputAsHandled();
            Close(cancel);
        }
    }
}
