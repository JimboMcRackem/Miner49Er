using Godot;
using Miner49er.Core;

namespace Miner49er;

/// <summary>Eight preset miner colours, indexed by lobby selection.
///
/// Each team is a three-step C64 ramp rather than a single colour: sprite pixels are
/// banded into the ramp (see <see cref="MinerTint"/>) so tinted miners stay inside the
/// 16-colour palette. <see cref="At"/> returns the ramp's identity colour — the middle
/// band, which paints trousers and torso and is what a player actually reads a team
/// from — and that is what menus, the scoreboard and the minimap show.</summary>
public static class PlayerColors
{
	public static readonly string[] Names = C64Ramps.TeamNames;

	/// <summary>Identity colour per team, for UI that wants a single swatch.</summary>
	public static readonly Color[] Palette = BuildPalette();

	private static Color[] BuildPalette()
	{
		var colors = new Color[C64Ramps.Teams.Length];
		for (int i = 0; i < colors.Length; i++)
			colors[i] = C64Colors.From(C64Ramps.Teams[i].Identity);
		return colors;
	}

	private static int Wrap(int index) =>
		((index % C64Ramps.Teams.Length) + C64Ramps.Teams.Length) % C64Ramps.Teams.Length;

	public static Color At(int index) => Palette[Wrap(index)];

	/// <summary>The full ramp for a team, for sprite tinting.</summary>
	public static ColorRamp RampAt(int index) => C64Ramps.Teams[Wrap(index)];
}
