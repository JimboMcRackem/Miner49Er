using Godot;

namespace Miner49er;

/// <summary>Eight preset miner colors, indexed by lobby selection.</summary>
public static class PlayerColors
{
	public static readonly Color[] Palette =
	{
		new("e8c34a"), new("4a9be8"), new("e84a4a"), new("4ae87a"),
		new("c34ae8"), new("e8964a"), new("4ae8e0"), new("e84ab0"),
	};

	public static Color At(int index) => Palette[((index % Palette.Length) + Palette.Length) % Palette.Length];
}
