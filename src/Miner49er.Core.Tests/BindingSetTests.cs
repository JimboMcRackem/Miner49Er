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
