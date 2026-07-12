using Godot;

namespace Miner49er;

// One-time bootstrap: applies saved display settings and window placement, then hands
// straight off to the main menu. No dwell or title text — the engine boot-splash image
// is the only splash the player sees, and we jump from it to the application.
public partial class Splash : Control
{
	public override void _Ready()
	{
		var (fs, vs, ww, wh) = SettingsStore.LoadDisplay();
		DisplayServer.WindowSetMode(fs ? DisplayServer.WindowMode.Fullscreen : DisplayServer.WindowMode.Windowed);
		DisplayServer.WindowSetVsyncMode(vs ? DisplayServer.VSyncMode.Enabled : DisplayServer.VSyncMode.Disabled);
		if (!fs)
		{
			if (ww > 0 && wh > 0)
				DisplayServer.WindowSetSize(new Vector2I(ww, wh));
			// Reopen where the window was last closed; on first launch there's no saved
			// position, so leave it at the centered default (initial_position_type=1).
			var (has, x, y) = SettingsStore.LoadWindowPosition();
			if (has)
				DisplayServer.WindowSetPosition(new Vector2I(x, y));
		}

		GetTree().ChangeSceneToFile("res://game/ui/MainMenu.tscn");
	}
}
