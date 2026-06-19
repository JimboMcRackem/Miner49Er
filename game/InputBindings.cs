using Godot;
using Miner49er.Core.Input;

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
	public const string Exit = "exit";          // context back-out / quit (Phase 4)
	public const string Settings = "settings";  // open the audio settings panel
	public const string Throw    = "throw_stone";

	// Everything except Exit is user-rebindable. AllActions also carries Exit so
	// its ESC code occupies a slot in the BindingSet (so nothing can steal ESC).
	public static readonly string[] RebindableActions =
	{
		MoveUp, MoveDown, MoveLeft, MoveRight,
		Pickaxe, Plant, Listen, UseItem, Restart, Mute, Settings, Throw,
	};

	private static readonly string[] AllActions =
	{
		MoveUp, MoveDown, MoveLeft, MoveRight,
		Pickaxe, Plant, Listen, UseItem, Restart, Mute, Settings, Exit, Throw,
	};

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
		Bind(Exit, Key.Escape);
		Bind(Settings, Key.O);
		Bind(Throw, Key.T, JoyButton.LeftShoulder);

		// Overlay any saved user rebindings onto the freshly-registered defaults.
		// Idempotent: re-applying the same set is a harmless InputMap rewrite.
		var set = BuildBindingSet();
		set.FromConfig(SettingsStore.LoadInput());
		Apply(set);
	}

	// Snapshot the current InputMap (defaults or already-applied) into a BindingSet.
	public static BindingSet BuildBindingSet()
	{
		var set = new BindingSet();
		foreach (var action in AllActions)
		{
			if (!InputMap.HasAction(action)) continue;
			// Ensure a keyboard slot exists even before we find the event.
			set.Set(action, BindDevice.Keyboard, -1);
			foreach (var ev in InputMap.ActionGetEvents(action))
			{
				if (ev is InputEventKey k)
					set.Set(action, BindDevice.Keyboard, (int)k.PhysicalKeycode);
				else if (ev is InputEventJoypadButton j)
					set.Set(action, BindDevice.Gamepad, (int)j.ButtonIndex);
			}
		}
		return set;
	}

	// Rewrite InputMap for every rebindable action from the set. Exit is untouched.
	public static void Apply(BindingSet set)
	{
		foreach (var action in RebindableActions)
		{
			if (!InputMap.HasAction(action)) continue;
			InputMap.ActionEraseEvents(action);
			int kb = set.Get(action, BindDevice.Keyboard);
			if (kb >= 0)
				InputMap.ActionAddEvent(action, new InputEventKey { PhysicalKeycode = (Key)kb });
			int pad = set.Get(action, BindDevice.Gamepad);
			if (pad >= 0)
				InputMap.ActionAddEvent(action, new InputEventJoypadButton { ButtonIndex = (JoyButton)pad });
		}
	}

	private static void Bind(string action, Key key, JoyButton button)
	{
		if (InputMap.HasAction(action)) return; // idempotent: registered once, app-wide
		InputMap.AddAction(action);
		InputMap.ActionAddEvent(action, new InputEventKey { PhysicalKeycode = key });
		InputMap.ActionAddEvent(action, new InputEventJoypadButton { ButtonIndex = button });
	}

	private static void Bind(string action, Key key)
	{
		if (InputMap.HasAction(action)) return; // keyboard-only (e.g. Exit/ESC)
		InputMap.AddAction(action);
		InputMap.ActionAddEvent(action, new InputEventKey { PhysicalKeycode = key });
	}
}
