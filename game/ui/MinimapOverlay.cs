using Godot;
using Miner49er.Core;

namespace Miner49er;

/// <summary>Small corner minimap showing explored terrain, current position, and key landmarks.</summary>
public partial class MinimapOverlay : CanvasLayer
{
	private MapCanvas _canvas = null!;

	public void Init(MatchClient client)
	{
		_canvas.Client = client;
	}

	public override void _Ready()
	{
		Layer = 10;
		_canvas = new MapCanvas();
		_canvas.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		_canvas.MouseFilter = Control.MouseFilterEnum.Ignore;
		AddChild(_canvas);
	}

	public override void _Process(double delta) => _canvas.QueueRedraw();

	private partial class MapCanvas : Control
	{
		public MatchClient? Client;

		private const int MaxPx = 160; // max minimap width or height in screen pixels

		public override void _Draw()
		{
			if (Client?.Grid == null) return;
			var fog  = Client.Fog;
			int gw   = Client.Grid.Width;
			int gh   = Client.Grid.Height;
			if (gw == 0 || gh == 0) return;

			// One tile = px screen pixels (uniform, capped at 4).
			int px = Mathf.Clamp(MaxPx / Mathf.Max(gw, 1), 1, 4);

			int mapW = gw * px;
			int mapH = gh * px;

			// Top-right corner with a small inset.
			float ox = Size.X - mapW - 6;
			float oy = 44f;

			// Background.
			DrawRect(new Rect2(ox - 2, oy - 2, mapW + 4, mapH + 4), new Color(0f, 0f, 0f, 0.72f));

			// Explored tiles.
			foreach (var pos in fog.Explored)
			{
				if (!Client.Grid.InBounds(pos)) continue;
				var tile = Client.Grid.Get(pos);
				bool vis = fog.IsVisible(pos);

				Color col;
				if (tile == TileType.Floor || tile == TileType.ShallowWater || tile == TileType.DeepWater
			        || tile == TileType.Cracked || tile == TileType.Crumbling || tile == TileType.Plank)
					col = vis ? new Color(0.55f, 0.55f, 0.55f) : new Color(0.28f, 0.28f, 0.28f);
				else if (tile == TileType.Lava)
					col = vis ? new Color(0.9f, 0.4f, 0.1f) : new Color(0.5f, 0.2f, 0.05f);
				else
					col = vis ? new Color(0.32f, 0.22f, 0.12f) : new Color(0.17f, 0.12f, 0.07f);

				DrawRect(new Rect2(ox + pos.X * px, oy + pos.Y * px, px, px), col);
			}

			// Escape tile landmark (expedition).
			if (Client.EscapeTile is GridPos esc && fog.IsExplored(esc))
			{
				var c = Client.EscapeOpen ? Colors.LimeGreen : new Color(0f, 0.5f, 0f);
				DrawRect(new Rect2(ox + esc.X * px, oy + esc.Y * px, Mathf.Max(px, 2), Mathf.Max(px, 2)), c);
			}

			// Local player dot (always white, drawn last so it's on top).
			foreach (var m in Client.Miners)
			{
				if (m.Id != Client.LocalMinerId || !m.Alive) continue;
				int dot = Mathf.Max(px, 2);
				DrawRect(new Rect2(ox + m.X * px, oy + m.Y * px, dot, dot), Colors.White);
			}
		}
	}
}
