using Godot;

namespace Miner49er;

public partial class Hud : CanvasLayer
{
	private Label _leftLabel   = null!;
	private Label _centerLabel = null!;
	private Label _rightLabel  = null!;

	public override void _Ready()
	{
		var bar = new HBoxContainer { Name = "HudBar" };
		bar.AnchorLeft   = 0f;
		bar.AnchorRight  = 1f;
		bar.AnchorTop    = 0f;
		bar.AnchorBottom = 0f;
		bar.OffsetLeft   = 8f;
		bar.OffsetRight  = -8f;
		bar.OffsetTop    = 6f;
		bar.OffsetBottom = 38f;
		AddChild(bar);

		var font = ThemeDB.FallbackFont;

		_leftLabel   = MakeLabel(bar, font, 22, new Color(1f, 0.18f, 0.18f), HorizontalAlignment.Left);
		_centerLabel = MakeLabel(bar, font, 20, Colors.White,                 HorizontalAlignment.Center);
		_rightLabel  = MakeLabel(bar, font, 20, Colors.White,                 HorizontalAlignment.Right);
	}

	private static Label MakeLabel(HBoxContainer parent, Font font, int size, Color color, HorizontalAlignment align)
	{
		var lbl = new Label
		{
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			HorizontalAlignment = align,
			VerticalAlignment   = VerticalAlignment.Center,
		};
		lbl.AddThemeFontOverride("font", font);
		lbl.AddThemeFontSizeOverride("font_size", size);
		lbl.AddThemeColorOverride("font_color", color);
		parent.AddChild(lbl);
		return lbl;
	}

	// lives: ♥ count (0 hides them); center: mode/objective; right: status, held items, timer…
	public void SetHud(int lives, string center, string right)
	{
		_leftLabel.Text   = lives > 0 ? new string('♥', lives) : "";
		_centerLabel.Text = center;
		_rightLabel.Text  = right.TrimStart();
	}
}
