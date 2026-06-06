using Godot;

namespace Miner49er;

/// <summary>Full-screen overlay shown while the map is generated / match set up.</summary>
public partial class LoadingScreen : CanvasLayer
{
    private Label _label = null!;

    public override void _Ready()
    {
        Layer = 100; // draw above everything

        var bg = new ColorRect { Color = new Color(0.02f, 0.02f, 0.03f) };
        bg.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(bg);

        _label = new Label { Text = "Generating mine…" };
        _label.SetAnchorsPreset(Control.LayoutPreset.Center);
        _label.AddThemeFontSizeOverride("font_size", 28);
        AddChild(_label);
    }

    public void SetStatus(string text) => _label.Text = text;
}
