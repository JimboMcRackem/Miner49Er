using Godot;
using Miner49er.Core;

namespace Miner49er;

/// <summary>Reads local input each frame and forwards it to the host:
/// the current desired direction (or -1) every frame, and edge-triggered
/// mine/plant/use actions on key-down.</summary>
public partial class InputSender : Node
{
	public bool Enabled = true; // disabled when the local miner is dead (spectating)
	public bool Listening = false; // true while the listen key is held

	public override void _PhysicsProcess(double delta)
	{
		if (!Enabled) return;
		if (Listening)
		{
			NetworkManager.Instance.SendDir(-1); // actively stand still; no actions
			return;
		}

		int dir = ReadDir();
		NetworkManager.Instance.SendDir(dir);

		bool mine  = Input.IsActionJustPressed(InputBindings.Pickaxe);
		bool plant = Input.IsActionJustPressed(InputBindings.Plant);
		bool use   = Input.IsActionJustPressed(InputBindings.UseItem);
		bool throwStone = Input.IsActionJustPressed(InputBindings.Throw);
		if (mine || plant || use || throwStone)
			NetworkManager.Instance.SendAction(mine, plant, use, throwStone);
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
