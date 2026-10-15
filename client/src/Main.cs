using Godot;

namespace Tribes.Client;

public partial class Main : Node2D
{
    public override void _Ready()
    {
        GD.Print("TRIBES_BOOT_OK");
    }
}
