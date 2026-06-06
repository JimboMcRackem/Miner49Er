using Godot;

namespace Miner49er;

public partial class Hud : CanvasLayer
{
	private Label _label = null!;

	public override void _Ready()
	{
		_label = new Label
		{
			Position = new Vector2(16, 12),
		};
		_label.AddThemeFontSizeOverride("font_size", 20);
		AddChild(_label);
	}

	public void SetText(string text) => _label.Text = text;
}
