using Godot;

namespace Miner49er;

public partial class Hud : CanvasLayer
{
	private Label _livesLabel = null!;
	private Label _textLabel  = null!;

	public override void _Ready()
	{
		var font = ThemeDB.FallbackFont;

		_livesLabel = new Label { Position = new Vector2(16, 12) };
		_livesLabel.AddThemeFontOverride("font", font);
		_livesLabel.AddThemeFontSizeOverride("font_size", 22);
		_livesLabel.AddThemeColorOverride("font_color", new Color(1f, 0.18f, 0.18f));
		AddChild(_livesLabel);

		_textLabel = new Label { Position = new Vector2(16, 12) };
		_textLabel.AddThemeFontOverride("font", font);
		_textLabel.AddThemeFontSizeOverride("font_size", 20);
		_textLabel.AddThemeColorOverride("font_color", Colors.White);
		AddChild(_textLabel);
	}

	// lives: number of ♥ to show (0 hides them); text: everything else (objective, status…)
	public void SetHud(int lives, string text)
	{
		_livesLabel.Text = lives > 0 ? new string('♥', lives) : "";
		// Offset text right to sit after the hearts (each ♥ ~16px wide at size 22)
		float heartsWidth = lives > 0 ? lives * 16f + 8f : 0f;
		_textLabel.Position = new Vector2(16 + heartsWidth, 12);
		_textLabel.Text = text;
	}

	// Legacy single-string path (non-expedition modes)
	public void SetText(string text)
	{
		_livesLabel.Text = "";
		_textLabel.Position = new Vector2(16, 12);
		_textLabel.Text = text;
	}
}
