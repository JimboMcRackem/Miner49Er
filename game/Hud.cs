using Godot;

namespace Miner49er;

public partial class Hud : CanvasLayer
{
	private Label _leftLabel    = null!;
	private Label _centerLabel  = null!;
	private Label _centerLabel2 = null!;
	private Label _rightLabel   = null!;
	private Label _hintLabel    = null!;

	public bool TimerUrgent { get; set; }

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

		_leftLabel = MakeLabel(bar, font, 22, new Color(1f, 0.18f, 0.18f), HorizontalAlignment.Left);

		// Center slot: inner HBox with two equal columns so treasure hunt idol names
		// always align regardless of name length.
		var centerBox = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		bar.AddChild(centerBox);
		_centerLabel  = MakeLabel(centerBox, font, 20, Colors.White, HorizontalAlignment.Center);
		_centerLabel2 = MakeLabel(centerBox, font, 20, Colors.White, HorizontalAlignment.Center);
		_centerLabel2.Visible = false;

		_rightLabel = MakeLabel(bar, font, 20, Colors.White, HorizontalAlignment.Right);

		// Contextual hint — bottom-centre, shown when near an interactable.
		_hintLabel = new Label
		{
			Text = "",
			HorizontalAlignment = HorizontalAlignment.Center,
			AnchorLeft = 0f,  AnchorRight  = 1f,
			AnchorTop  = 1f,  AnchorBottom = 1f,
			OffsetTop  = -38f, OffsetBottom = -6f,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		_hintLabel.AddThemeFontOverride("font", font);
		_hintLabel.AddThemeFontSizeOverride("font_size", 17);
		_hintLabel.AddThemeColorOverride("font_color", new Color(1f, 1f, 0.6f, 0.92f));
		AddChild(_hintLabel);
	}

	public override void _Process(double delta)
	{
		if (TimerUrgent)
		{
			float flash = (Mathf.Sin((float)Time.GetTicksMsec() * 0.006f) + 1f) * 0.5f;
			_rightLabel.AddThemeColorOverride("font_color", new Color(1f, flash * 0.15f + 0.05f, 0.05f));
		}
		else
		{
			_rightLabel.AddThemeColorOverride("font_color", Colors.White);
		}
	}

	public void SetHint(string hint) => _hintLabel.Text = hint;

	// lives: ♥ count (0 hides them); center: mode/objective; right: status, held items, timer…
	public void SetHud(int lives, string center, string right)
	{
		_leftLabel.Text       = lives > 0 ? new string('♥', lives) : "";
		_centerLabel.Text     = center;
		_centerLabel2.Visible = false;
		_rightLabel.Text      = right.TrimStart();
	}

	// Treasure Hunt: two idol columns each get equal width so ○/✓ always aligns.
	public void SetTreasureHud(string idol1, bool found1, string idol2, bool found2, string right)
	{
		_leftLabel.Text       = "";
		_centerLabel.Text     = $"{(found1 ? "✓" : "○")} {idol1}";
		_centerLabel2.Visible = true;
		_centerLabel2.Text    = $"{(found2 ? "✓" : "○")} {idol2}";
		_rightLabel.Text      = right.TrimStart();
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
}
