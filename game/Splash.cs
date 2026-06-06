using Godot;

namespace Miner49er;

public partial class Splash : Control
{
	[Export] public float DwellSeconds = 1.5f;
	private double _elapsed;
	private bool _advancing;

	public override void _Ready()
	{
		var center = new CenterContainer();
		center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		AddChild(center);

		var title = new Label { Text = "MINER 49ER" };
		title.AddThemeFontSizeOverride("font_size", 64);
		center.AddChild(title);
	}

	public override void _Process(double delta)
	{
		_elapsed += delta;
		if (_elapsed >= DwellSeconds) Advance();
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event.IsPressed()) Advance();
	}

	private void Advance()
	{
		if (_advancing) return;
		_advancing = true;
		GetTree().ChangeSceneToFile("res://game/ui/MainMenu.tscn");
	}
}
