using Godot;

namespace Miner49er;

/// <summary>The colour the cave's darkness takes on each floor. Shadows and remembered areas fade
/// toward it rather than to plain black, and it follows the terrain tint bands in
/// <see cref="TerrainMap.SetFloor"/> so the dark gets colder, then stranger, then hotter on the
/// way down. Unexplored tiles still fade to pure black.</summary>
public static class CaveMood
{
	public static Color Shadow(int floor) => floor switch
	{
		<= 5  => new Color(0.18f, 0.12f, 0.06f),   // warm umber
		<= 10 => new Color(0.06f, 0.12f, 0.20f),   // cold blue-grey
		<= 15 => new Color(0.14f, 0.06f, 0.22f),   // deep violet
		_     => new Color(0.17f, 0.08f, 0.06f),   // ember
	};

	public static Color Shadow() => Shadow(NetworkManager.Instance?.MatchFloor ?? 1);
}
