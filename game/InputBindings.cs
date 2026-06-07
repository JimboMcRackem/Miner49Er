using Godot;

namespace Miner49er;

/// <summary>
/// Registers default keyboard + gamepad actions at startup. Phase 5 replaces
/// this with a persisted, user-editable rebinding system; until then this keeps
/// bindings in code so they always exist.
/// </summary>
public static class InputBindings
{
	public const string MoveUp = "move_up";
	public const string MoveDown = "move_down";
	public const string MoveLeft = "move_left";
	public const string MoveRight = "move_right";
	public const string Pickaxe = "pickaxe";
	public const string Plant = "plant_explosive";
	public const string Listen = "listen";       // defined now, used in Phase 3
	public const string UseItem = "use_item";     // defined now, used in Phase 4
	public const string Restart = "restart";
	public const string Mute = "mute";          // master mute (Phase 3)

	public static void EnsureDefaults()
	{
		Bind(MoveUp, Key.W, JoyButton.DpadUp);
		Bind(MoveDown, Key.S, JoyButton.DpadDown);
		Bind(MoveLeft, Key.A, JoyButton.DpadLeft);
		Bind(MoveRight, Key.D, JoyButton.DpadRight);
		Bind(Pickaxe, Key.J, JoyButton.X);
		Bind(Plant, Key.K, JoyButton.A);
		Bind(Listen, Key.L, JoyButton.B);
		Bind(UseItem, Key.Space, JoyButton.Y);
		Bind(Restart, Key.R, JoyButton.Start);
		Bind(Mute, Key.M, JoyButton.Back);
	}

	private static void Bind(string action, Key key, JoyButton button)
	{
		if (!InputMap.HasAction(action)) InputMap.AddAction(action);
		InputMap.ActionAddEvent(action, new InputEventKey { PhysicalKeycode = key });
		InputMap.ActionAddEvent(action, new InputEventJoypadButton { ButtonIndex = button });
	}
}
