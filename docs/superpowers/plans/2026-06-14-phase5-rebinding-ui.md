# Input Rebinding UI Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let the local player remap keyboard and gamepad controls from a Controls tab in a combined Settings panel, persisted to `user://settings.cfg` and applied on startup; everything rebindable except Exit (ESC).

**Architecture:** A pure-C# `Miner49er.Core.Input.BindingSet` owns the testable logic (per-action keyboard/gamepad codes, reject-and-tell conflict detection, config round-trip). The Godot `game/` layer bridges `BindingSet` ↔ Godot `InputMap` ↔ `SettingsStore`, renames `AudioSettingsPanel` to a tabbed `SettingsPanel`, and adds the Controls tab with input capture. `InputMap` stays the runtime source of truth.

**Tech Stack:** C# / .NET 8, xUnit (`Miner49er.Core.Tests`), Godot 4.6.3 (.NET/Mono).

**Conventions (from CLAUDE.md / memory):**
- `Miner49er.Core` uses **4-space** indent; `game/` uses **TAB** indent.
- Build: `dotnet build Miner49er.sln`. Test: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj`.
- Run `godot` **via PowerShell ONLY** (the Bash shim breaks headless with a false "assemblies not found"). Headless smoke: `& godot --headless --quit-after 2` from the project root, expect exit 0.
- Commit message footer (verbatim): `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`
- Stage only the exact files named per task — **never** `git add -A`. Do NOT stage the pre-existing untracked `assets/Splash.png*`, `.superpowers/`, `*.uid`, or the CRLF-only working-tree changes to `project.godot` / `game/Splash.tscn`.

---

### Task 1: `BindingSet` — the pure, testable core

**Files:**
- Create: `src/Miner49er.Core/Input/BindingSet.cs`
- Test: `src/Miner49er.Core.Tests/BindingSetTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `src/Miner49er.Core.Tests/BindingSetTests.cs` (4-space indent):

```csharp
using System.Collections.Generic;
using System.Linq;
using Miner49er.Core.Input;
using Xunit;

public class BindingSetTests
{
    // Codes are arbitrary ints in tests; in-game they are Godot Key/JoyButton values.
    private const int KbW = 87, KbA = 65, KbEsc = 4194305, PadX = 2, PadA = 0;

    private static BindingSet Seeded()
    {
        var s = new BindingSet();
        s.Set("move_up", BindDevice.Keyboard, KbW);
        s.Set("move_up", BindDevice.Gamepad, PadX);
        s.Set("plant", BindDevice.Keyboard, KbA);
        s.Set("plant", BindDevice.Gamepad, PadA);
        s.Set("settings", BindDevice.Keyboard, 79); // keyboard-only: no gamepad slot
        s.Set("exit", BindDevice.Keyboard, KbEsc);   // present but never shown in UI
        return s;
    }

    [Fact]
    public void Get_returns_seeded_codes_and_minus_one_for_absent_slot()
    {
        var s = Seeded();
        Assert.Equal(KbW, s.Get("move_up", BindDevice.Keyboard));
        Assert.Equal(PadX, s.Get("move_up", BindDevice.Gamepad));
        Assert.Equal(-1, s.Get("settings", BindDevice.Gamepad)); // no gamepad slot
        Assert.Equal(-1, s.Get("nope", BindDevice.Keyboard));    // unknown action
    }

    [Fact]
    public void TryRebind_to_free_code_succeeds_and_updates()
    {
        var s = Seeded();
        Assert.True(s.TryRebind("move_up", BindDevice.Keyboard, 70 /*F*/, out var conflict));
        Assert.Null(conflict);
        Assert.Equal(70, s.Get("move_up", BindDevice.Keyboard));
    }

    [Fact]
    public void TryRebind_to_code_held_by_another_action_is_rejected_and_names_it()
    {
        var s = Seeded();
        Assert.False(s.TryRebind("move_up", BindDevice.Keyboard, KbA, out var conflict));
        Assert.Equal("plant", conflict);
        Assert.Equal(KbW, s.Get("move_up", BindDevice.Keyboard)); // unchanged
        Assert.Equal(KbA, s.Get("plant", BindDevice.Keyboard));   // unchanged
    }

    [Fact]
    public void TryRebind_to_same_actions_own_code_is_a_noop_success()
    {
        var s = Seeded();
        Assert.True(s.TryRebind("move_up", BindDevice.Keyboard, KbW, out var conflict));
        Assert.Null(conflict);
        Assert.Equal(KbW, s.Get("move_up", BindDevice.Keyboard));
    }

    [Fact]
    public void Same_code_on_the_other_device_is_not_a_conflict()
    {
        var s = Seeded();
        // PadA is held by plant on the gamepad; binding it on the keyboard is fine.
        Assert.True(s.TryRebind("move_up", BindDevice.Keyboard, PadA, out var conflict));
        Assert.Null(conflict);
    }

    [Fact]
    public void Exit_esc_code_is_reported_as_the_conflict_when_another_action_grabs_it()
    {
        var s = Seeded();
        Assert.False(s.TryRebind("move_up", BindDevice.Keyboard, KbEsc, out var conflict));
        Assert.Equal("exit", conflict);
    }

    [Fact]
    public void ToConfig_then_FromConfig_round_trips_an_edited_set()
    {
        var s = Seeded();
        s.TryRebind("move_up", BindDevice.Keyboard, 70, out _);
        var saved = s.ToConfig();

        var fresh = Seeded();
        fresh.FromConfig(saved);
        Assert.Equal(70, fresh.Get("move_up", BindDevice.Keyboard));
        Assert.Equal(PadX, fresh.Get("move_up", BindDevice.Gamepad));
    }

    [Fact]
    public void ToConfig_omits_absent_slots()
    {
        var keys = Seeded().ToConfig().Keys;
        Assert.Contains("settings.kb", keys);
        Assert.DoesNotContain("settings.pad", keys); // keyboard-only action
    }

    [Fact]
    public void FromConfig_ignores_unknown_actions_and_absent_gamepad_slots()
    {
        var s = Seeded();
        s.FromConfig(new Dictionary<string, long>
        {
            ["ghost.kb"] = 999,     // unknown action -> ignored
            ["settings.pad"] = 5,   // settings has no gamepad slot -> ignored
        });
        Assert.Equal(-1, s.Get("settings", BindDevice.Gamepad));
        Assert.DoesNotContain("ghost", s.Actions);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj`
Expected: FAIL — `BindingSet` / `BindDevice` do not exist (compile error).

- [ ] **Step 3: Write the implementation**

Create `src/Miner49er.Core/Input/BindingSet.cs` (4-space indent):

```csharp
using System.Collections.Generic;

namespace Miner49er.Core.Input;

public enum BindDevice { Keyboard, Gamepad }

/// <summary>The editable model of the player's rebindable controls. Pure C#:
/// codes are plain ints (the caller maps Godot Key/JoyButton enum values to/from
/// int). -1 means an action has no slot for that device. Owns conflict detection
/// (reject-and-tell) and the flat config round-trip used for persistence.</summary>
public sealed class BindingSet
{
    private readonly List<string> _actions = new();          // stable display order
    private readonly Dictionary<string, int> _kb = new();
    private readonly Dictionary<string, int> _pad = new();

    public IEnumerable<string> Actions => _actions;

    /// <summary>Unchecked set — used to seed defaults and overlay saved values.
    /// Creates the action (both slots -1) the first time it is seen.</summary>
    public void Set(string action, BindDevice device, int code)
    {
        if (!_kb.ContainsKey(action))
        {
            _actions.Add(action);
            _kb[action] = -1;
            _pad[action] = -1;
        }
        if (device == BindDevice.Keyboard) _kb[action] = code;
        else _pad[action] = code;
    }

    public int Get(string action, BindDevice device)
    {
        var map = device == BindDevice.Keyboard ? _kb : _pad;
        return map.TryGetValue(action, out var c) ? c : -1;
    }

    /// <summary>Reject-and-tell. Fails (and names the holder) if `code` is already
    /// bound to a DIFFERENT action on the same device; binding to the action's own
    /// current code is a no-op success. On success the slot is updated.</summary>
    public bool TryRebind(string action, BindDevice device, int code, out string? conflictingAction)
    {
        conflictingAction = null;
        var map = device == BindDevice.Keyboard ? _kb : _pad;

        if (map.TryGetValue(action, out var current) && current == code)
            return true; // unchanged

        foreach (var other in _actions)
        {
            if (other == action) continue;
            if (map[other] == code)
            {
                conflictingAction = other;
                return false;
            }
        }

        Set(action, device, code);
        return true;
    }

    /// <summary>Flat (key -> code) map for ConfigFile; keys are "&lt;action&gt;.kb" /
    /// "&lt;action&gt;.pad". Absent slots (-1) are omitted.</summary>
    public IReadOnlyDictionary<string, long> ToConfig()
    {
        var d = new Dictionary<string, long>();
        foreach (var a in _actions)
        {
            if (_kb[a] >= 0) d[a + ".kb"] = _kb[a];
            if (_pad[a] >= 0) d[a + ".pad"] = _pad[a];
        }
        return d;
    }

    /// <summary>Overlays saved values onto existing slots. Ignores entries whose
    /// action is unknown or whose device slot the action does not have.</summary>
    public void FromConfig(IReadOnlyDictionary<string, long> values)
    {
        foreach (var kv in values)
        {
            BindDevice device;
            string action;
            if (kv.Key.EndsWith(".kb")) { device = BindDevice.Keyboard; action = kv.Key[..^3]; }
            else if (kv.Key.EndsWith(".pad")) { device = BindDevice.Gamepad; action = kv.Key[..^4]; }
            else continue;

            if (!_kb.ContainsKey(action)) continue;                       // unknown action
            if (device == BindDevice.Gamepad && _pad[action] < 0) continue; // no gamepad slot
            Set(action, device, (int)kv.Value);
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj`
Expected: PASS — all `BindingSetTests` green, prior Core tests still green.

- [ ] **Step 5: Commit**

```bash
git add src/Miner49er.Core/Input/BindingSet.cs src/Miner49er.Core.Tests/BindingSetTests.cs
git commit -m "feat(core): BindingSet — rebindable controls model with conflict detection

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 2: `SettingsStore` — persist the `[input]` section

**Files:**
- Modify: `game/audio/SettingsStore.cs`

- [ ] **Step 1: Add `LoadInput` / `SaveInput`**

In `game/audio/SettingsStore.cs` (TAB indent), add a section constant beside the existing `Section` field:

```csharp
	private const string InputSection = "input";
```

Add these two methods after `SaveAudio` (note the `using System.Collections.Generic;` at the top of the file — add it if absent):

```csharp
	// Returns saved input overrides as a flat (key -> code) map; empty if none.
	public static Dictionary<string, long> LoadInput()
	{
		var result = new Dictionary<string, long>();
		var cfg = new ConfigFile();
		if (cfg.Load(Path) != Error.Ok) return result;
		if (!cfg.HasSection(InputSection)) return result;
		foreach (var key in cfg.GetSectionKeys(InputSection))
			result[key] = cfg.GetValue(InputSection, key).AsInt64();
		return result;
	}

	// Persists a BindingSet.ToConfig() map under [input], preserving [audio].
	public static void SaveInput(IReadOnlyDictionary<string, long> values)
	{
		var cfg = new ConfigFile();
		cfg.Load(Path); // keep any existing sections (e.g. audio)
		if (cfg.HasSection(InputSection)) cfg.EraseSection(InputSection);
		foreach (var kv in values)
			cfg.SetValue(InputSection, kv.Key, kv.Value);
		cfg.Save(Path);
	}
```

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build Miner49er.sln`
Expected: Build succeeded, 0 errors. (`IReadOnlyDictionary` is in `System.Collections.Generic`.)

- [ ] **Step 3: Commit**

```bash
git add game/audio/SettingsStore.cs
git commit -m "feat(game): persist input rebindings to settings.cfg [input] section

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 3: `InputBindings` — InputMap ↔ BindingSet bridge + startup overlay

**Files:**
- Modify: `game/InputBindings.cs`

- [ ] **Step 1: Add the action list, bridge methods, and overlay**

In `game/InputBindings.cs` (TAB indent), add `using Miner49er.Core.Input;` under the existing `using Godot;`.

Add the rebindable-action arrays after the action `const` block (before `EnsureDefaults`):

```csharp
	// Everything except Exit is user-rebindable. AllActions also carries Exit so
	// its ESC code occupies a slot in the BindingSet (so nothing can steal ESC).
	public static readonly string[] RebindableActions =
	{
		MoveUp, MoveDown, MoveLeft, MoveRight,
		Pickaxe, Plant, Listen, UseItem, Restart, Mute, Settings,
	};

	private static readonly string[] AllActions =
	{
		MoveUp, MoveDown, MoveLeft, MoveRight,
		Pickaxe, Plant, Listen, UseItem, Restart, Mute, Settings, Exit,
	};
```

Append the overlay to the END of `EnsureDefaults()` (after the last `Bind(...)` call):

```csharp
		// Overlay any saved user rebindings onto the freshly-registered defaults.
		// Idempotent: re-applying the same set is a harmless InputMap rewrite.
		var set = BuildBindingSet();
		set.FromConfig(SettingsStore.LoadInput());
		Apply(set);
```

Add the three bridge methods after `EnsureDefaults()`:

```csharp
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
```

Note: the initial `set.Set(action, BindDevice.Keyboard, -1)` is overwritten by the real keyboard event in the loop below it; it only guarantees the action exists in the set if (defensively) it had no keyboard event.

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build Miner49er.sln`
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Headless smoke — startup overlay runs without error**

In PowerShell (NOT Bash): `& godot --headless --quit-after 2`
Expected: exit code 0 (the app boots; `EnsureDefaults` runs the overlay with no save file → defaults stand).

- [ ] **Step 4: Commit**

```bash
git add game/InputBindings.cs
git commit -m "feat(game): bridge BindingSet to InputMap and apply saved rebindings on startup

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 4: Rename `AudioSettingsPanel` → tabbed `SettingsPanel` (Audio tab preserved)

**Files:**
- Rename: `game/ui/AudioSettingsPanel.cs` → `game/ui/SettingsPanel.cs`
- Delete: `game/ui/AudioSettingsPanel.cs.uid` (Godot regenerates the `.uid` for the new file)
- Modify: `game/ui/MainMenu.cs`
- Modify: `game/Main.cs`

- [ ] **Step 1: Create `game/ui/SettingsPanel.cs`**

Create `game/ui/SettingsPanel.cs` (TAB indent) — same overlay, but the body lives in a `TabContainer` with an Audio tab holding today's controls. The Controls tab is added empty here and filled in Task 5.

```csharp
using Godot;

namespace Miner49er;

/// <summary>Reusable settings overlay with Audio and Controls tabs. The Audio tab
/// holds Music/SFX volume + Music on/off (editing AudioManager live, persisting on
/// change). Controls is built in BuildControlsTab(). Hidden until Open(); used by
/// both the Main Menu and the in-match controller. Open/Close/Toggle/IsOpen are
/// unchanged from the former AudioSettingsPanel so callers need no logic changes.</summary>
public partial class SettingsPanel : CanvasLayer
{
	private HSlider _music = null!;
	private HSlider _sfx = null!;
	private CheckBox _musicOn = null!;

	public bool IsOpen => Visible;

	public override void _Ready()
	{
		Layer = 100; // above other UI

		var center = new CenterContainer();
		center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		AddChild(center);

		var panel = new PanelContainer();
		var bg = new StyleBoxFlat
		{
			BgColor = new Color(0.10f, 0.10f, 0.13f, 1f),
			BorderColor = new Color(0.35f, 0.35f, 0.42f, 1f),
			BorderWidthTop = 2, BorderWidthBottom = 2, BorderWidthLeft = 2, BorderWidthRight = 2,
			CornerRadiusTopLeft = 8, CornerRadiusTopRight = 8,
			CornerRadiusBottomLeft = 8, CornerRadiusBottomRight = 8,
			ContentMarginTop = 28, ContentMarginBottom = 28, ContentMarginLeft = 32, ContentMarginRight = 32,
		};
		panel.AddThemeStyleboxOverride("panel", bg);
		center.AddChild(panel);

		var outer = new VBoxContainer { CustomMinimumSize = new Vector2(480, 0) };
		outer.AddThemeConstantOverride("separation", 14);
		panel.AddChild(outer);

		var tabs = new TabContainer { CustomMinimumSize = new Vector2(460, 360) };
		outer.AddChild(tabs);

		var audioTab = BuildAudioTab();
		audioTab.Name = "Audio";
		tabs.AddChild(audioTab);

		var controlsTab = BuildControlsTab();
		controlsTab.Name = "Controls";
		tabs.AddChild(controlsTab);

		var close = new Button { Text = "Close" };
		close.Pressed += Close;
		outer.AddChild(close);

		SyncFromManager();

		_music.ValueChanged += v => AudioManager.Instance.SetMusicVolume((float)v);
		_sfx.ValueChanged += v => AudioManager.Instance.SetSfxVolume((float)v);
		_musicOn.Toggled += on => AudioManager.Instance.SetMusicEnabled(on);

		Visible = false;
	}

	private Control BuildAudioTab()
	{
		var box = new VBoxContainer { CustomMinimumSize = new Vector2(440, 0) };
		box.AddThemeConstantOverride("separation", 14);

		box.AddChild(new Label { Text = "Music" });
		_music = new HSlider { MinValue = 0, MaxValue = 1, Step = 0.01, CustomMinimumSize = new Vector2(400, 28) };
		box.AddChild(_music);

		box.AddChild(new Label { Text = "SFX" });
		_sfx = new HSlider { MinValue = 0, MaxValue = 1, Step = 0.01, CustomMinimumSize = new Vector2(400, 28) };
		box.AddChild(_sfx);

		_musicOn = new CheckBox { Text = "Music on" };
		box.AddChild(_musicOn);

		return box;
	}

	// Filled in Task 5. Placeholder keeps the tab present and the file compiling.
	private Control BuildControlsTab()
	{
		var box = new VBoxContainer { CustomMinimumSize = new Vector2(440, 0) };
		box.AddChild(new Label { Text = "Controls" });
		return box;
	}

	private void SyncFromManager()
	{
		var am = AudioManager.Instance;
		_music.SetBlockSignals(true); _music.Value = am.MusicVolume; _music.SetBlockSignals(false);
		_sfx.SetBlockSignals(true); _sfx.Value = am.SfxVolume; _sfx.SetBlockSignals(false);
		_musicOn.SetBlockSignals(true); _musicOn.ButtonPressed = am.MusicEnabled; _musicOn.SetBlockSignals(false);
	}

	public void Open() { SyncFromManager(); Visible = true; }
	public void Close() => Visible = false;
	public void Toggle() { if (Visible) Close(); else Open(); }
}
```

- [ ] **Step 2: Delete the old panel file and its stale `.uid`**

```bash
git rm game/ui/AudioSettingsPanel.cs
rm -f game/ui/AudioSettingsPanel.cs.uid
```

(The `.uid` is untracked; `rm -f` just clears it so Godot regenerates one for `SettingsPanel.cs`.)

- [ ] **Step 3: Update `MainMenu.cs` (type rename only)**

In `game/ui/MainMenu.cs`, change the field type and construction:
- `private AudioSettingsPanel _audioPanel = null!;` → `private SettingsPanel _audioPanel = null!;`
- `_audioPanel = new AudioSettingsPanel { Name = "AudioSettingsPanel" };` → `_audioPanel = new SettingsPanel { Name = "SettingsPanel" };`

(The `settingsBtn.Pressed += () => _audioPanel.Open();` and ESC handling are unchanged.)

- [ ] **Step 4: Update `Main.cs` (type rename only)**

In `game/Main.cs`:
- `private AudioSettingsPanel _audioPanel = null!;` (line ~19) → `private SettingsPanel _audioPanel = null!;`
- `_audioPanel = new AudioSettingsPanel { Name = "AudioSettingsPanel" };` (line ~78) → `_audioPanel = new SettingsPanel { Name = "SettingsPanel" };`

(The `O`-key `_audioPanel.Toggle()` at ~132-133 is unchanged.)

- [ ] **Step 5: Build to verify it compiles**

Run: `dotnet build Miner49er.sln`
Expected: Build succeeded, 0 errors, no lingering reference to `AudioSettingsPanel`.

- [ ] **Step 6: Headless smoke**

In PowerShell: `& godot --headless --quit-after 2`
Expected: exit code 0.

- [ ] **Step 7: Commit**

```bash
git add game/ui/SettingsPanel.cs game/ui/MainMenu.cs game/Main.cs
git rm --cached game/ui/AudioSettingsPanel.cs 2>/dev/null; git add -u game/ui/AudioSettingsPanel.cs
git commit -m "refactor(game): AudioSettingsPanel -> tabbed SettingsPanel (Audio | Controls)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

(If `git rm` in Step 2 already staged the deletion, the second line is a no-op; the important part is that the commit includes the rename and the two caller updates. Do NOT `git add` any `.uid` file.)

---

### Task 5: Controls tab — rows, slot labels, and capture flow

**Files:**
- Modify: `game/ui/SettingsPanel.cs`

- [ ] **Step 1: Add friendly names, the binding model field, and row state**

In `game/ui/SettingsPanel.cs`, add `using Miner49er.Core.Input;` and `using System.Collections.Generic;` at the top.

Add fields beside the audio fields:

```csharp
	private BindingSet _bindings = null!;
	private string? _capturingAction;          // null = not capturing
	private readonly Dictionary<string, Label> _kbLabels = new();
	private readonly Dictionary<string, Label> _padLabels = new();
	private readonly Dictionary<string, Button> _rebindButtons = new();
	private Label _conflictLabel = null!;

	private static readonly Dictionary<string, string> FriendlyNames = new()
	{
		[InputBindings.MoveUp] = "Move Up",
		[InputBindings.MoveDown] = "Move Down",
		[InputBindings.MoveLeft] = "Move Left",
		[InputBindings.MoveRight] = "Move Right",
		[InputBindings.Pickaxe] = "Pickaxe",
		[InputBindings.Plant] = "Plant Explosive",
		[InputBindings.Listen] = "Listen",
		[InputBindings.UseItem] = "Use Item",
		[InputBindings.Restart] = "Restart",
		[InputBindings.Mute] = "Mute",
		[InputBindings.Settings] = "Settings",
	};
```

- [ ] **Step 2: Replace `BuildControlsTab()` with the real grid**

Replace the placeholder `BuildControlsTab()` body with:

```csharp
	private Control BuildControlsTab()
	{
		_bindings = InputBindings.BuildBindingSet();
		_bindings.FromConfig(SettingsStore.LoadInput());

		var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(440, 320) };
		var grid = new GridContainer { Columns = 4 };
		grid.AddThemeConstantOverride("h_separation", 16);
		grid.AddThemeConstantOverride("v_separation", 8);
		scroll.AddChild(grid);

		foreach (var action in InputBindings.RebindableActions)
		{
			grid.AddChild(new Label { Text = FriendlyNames[action] });

			var kb = new Label { Text = KeyName(_bindings.Get(action, BindDevice.Keyboard)) };
			_kbLabels[action] = kb;
			grid.AddChild(kb);

			var pad = new Label { Text = PadName(_bindings.Get(action, BindDevice.Gamepad)) };
			_padLabels[action] = pad;
			grid.AddChild(pad);

			var btn = new Button { Text = "Rebind" };
			string captured = action; // capture loop variable
			btn.Pressed += () => BeginCapture(captured);
			_rebindButtons[action] = btn;
			grid.AddChild(btn);
		}

		var box = new VBoxContainer { CustomMinimumSize = new Vector2(440, 0) };
		box.AddChild(scroll);
		_conflictLabel = new Label { Text = "" };
		_conflictLabel.AddThemeColorOverride("font_color", new Color(0.95f, 0.55f, 0.35f));
		box.AddChild(_conflictLabel);
		return box;
	}

	private static string KeyName(int code) =>
		code < 0 ? "—" : OS.GetKeycodeString((Key)code);

	private static string PadName(int code) =>
		code < 0 ? "—" : ((JoyButton)code).ToString();
```

- [ ] **Step 3: Add the capture state machine**

Add these methods to `SettingsPanel`:

```csharp
	private void BeginCapture(string action)
	{
		if (_capturingAction != null) return; // one at a time
		_capturingAction = action;
		_conflictLabel.Text = "";
		_rebindButtons[action].Text = "Press a key or button…";
	}

	private void EndCapture()
	{
		if (_capturingAction is { } a) _rebindButtons[a].Text = "Rebind";
		_capturingAction = null;
	}

	public override void _Input(InputEvent @event)
	{
		if (!Visible || _capturingAction is not { } action) return;

		if (@event is InputEventKey key && key.Pressed && !key.Echo)
		{
			GetViewport().SetInputAsHandled();
			if (key.PhysicalKeycode == Key.Escape) { EndCapture(); return; } // cancel
			ApplyRebind(action, BindDevice.Keyboard, (int)key.PhysicalKeycode);
		}
		else if (@event is InputEventJoypadButton btn && btn.Pressed)
		{
			GetViewport().SetInputAsHandled();
			// Settings is keyboard-only: ignore gamepad, keep listening.
			if (_bindings.Get(action, BindDevice.Gamepad) < 0) return;
			ApplyRebind(action, BindDevice.Gamepad, (int)btn.ButtonIndex);
		}
	}

	private void ApplyRebind(string action, BindDevice device, int code)
	{
		if (_bindings.TryRebind(action, device, code, out var conflict))
		{
			InputBindings.Apply(_bindings);
			SettingsStore.SaveInput(_bindings.ToConfig());
			RefreshRow(action);
			_conflictLabel.Text = "";
		}
		else
		{
			var name = conflict != null && FriendlyNames.TryGetValue(conflict, out var f) ? f : conflict;
			_conflictLabel.Text = $"Already used by {name}";
		}
		EndCapture();
	}

	private void RefreshRow(string action)
	{
		_kbLabels[action].Text = KeyName(_bindings.Get(action, BindDevice.Keyboard));
		_padLabels[action].Text = PadName(_bindings.Get(action, BindDevice.Gamepad));
	}
```

Note: `Exit` (the action holding ESC) is in `_bindings` but not in `RebindableActions`, so no row exists for it and `FriendlyNames` need not contain it. A conflict naming `exit` would display the raw `"exit"` — acceptable, and unreachable in practice because the only key that conflicts with `exit` is Escape, which is intercepted as "cancel" before `TryRebind` is ever called.

- [ ] **Step 4: Build to verify it compiles**

Run: `dotnet build Miner49er.sln`
Expected: Build succeeded, 0 errors.

- [ ] **Step 5: Headless smoke**

In PowerShell: `& godot --headless --quit-after 2`
Expected: exit code 0.

- [ ] **Step 6: Commit**

```bash
git add game/ui/SettingsPanel.cs
git commit -m "feat(game): Controls tab — keyboard/gamepad rebinding with capture and conflict reject

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Manual verification (after Task 5 — run in PowerShell, needs a display)

These are not automated; do them before merging:

- [ ] Launch the game, Main Menu → Settings → **Controls** tab: every rebindable action shows a Key slot and a Pad slot ("—" only for Settings' pad).
- [ ] Rebind a movement key, Close, reopen → new key shown; start a match and confirm it moves.
- [ ] Rebind a gamepad button for the same action → its Key slot is unchanged.
- [ ] Attempt to bind a key already used (e.g. set Move Up to Pickaxe's key) → rejected with "Already used by Pickaxe", old binding kept.
- [ ] In a match, press `O` → Controls → start a capture and press a movement key → the player does **not** also move from that keypress.
- [ ] During a capture, press ESC → capture cancels, nothing changes; ESC still closes the panel / quits as before.
- [ ] Quit and relaunch → rebindings persisted.

## Self-review notes (author)

- **Spec coverage:** BindingSet + conflict/round-trip (Task 1) ✓; `[input]` persistence (Task 2) ✓; startup overlay + InputMap bridge (Task 3) ✓; combined tabbed panel preserving Audio + reachable in menu and in-match (Task 4) ✓; Controls rows, capture, reject-and-tell, ESC-cancel, Settings keyboard-only, input-not-leaking-to-gameplay (Task 5) ✓; Exit/ESC protected via seeded slot (Task 1 test + Task 3 `AllActions`) ✓.
- **Type consistency:** `BindingSet`, `BindDevice`, `TryRebind(out string?)`, `ToConfig`/`FromConfig`, `BuildBindingSet`/`Apply`, `RebindableActions` names are identical across Tasks 1, 3, 5. `LoadInput`/`SaveInput` identical across Tasks 2, 3, 5.
- **No automated tests for game/ tasks:** all unit-testable logic lives in `BindingSet` (Task 1). Tasks 2–5 are Godot adapter code verified by build + headless boot + the manual checklist, consistent with how prior audio/UI work was verified.
