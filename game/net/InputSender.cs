using Godot;
using Miner49er.Core;

namespace Miner49er;

/// <summary>Reads local input each frame and forwards it to the host:
/// the current desired direction (or -1) every frame, and edge-triggered
/// mine/plant actions on key-down.</summary>
public partial class InputSender : Node
{
	public bool Enabled = true; // disabled when the local miner is dead (spectating)

	public override void _PhysicsProcess(double delta)
	{
		if (!Enabled) return;

		int dir = ReadDir();
		NetworkManager.Instance.SendDir(dir);

		bool mine = Input.IsActionJustPressed(InputBindings.Pickaxe);
		bool plant = Input.IsActionJustPressed(InputBindings.Plant);
		if (mine || plant) NetworkManager.Instance.SendAction(mine, plant);
	}

	private static int ReadDir()
	{
		if (Input.IsActionPressed(InputBindings.MoveUp)) return (int)Direction.North;
		if (Input.IsActionPressed(InputBindings.MoveDown)) return (int)Direction.South;
		if (Input.IsActionPressed(InputBindings.MoveLeft)) return (int)Direction.West;
		if (Input.IsActionPressed(InputBindings.MoveRight)) return (int)Direction.East;
		return -1;
	}
}
