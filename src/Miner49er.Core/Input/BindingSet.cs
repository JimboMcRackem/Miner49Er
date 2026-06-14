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
