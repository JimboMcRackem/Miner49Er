using System.Text;
using Godot;
using Miner49er.Core.Net;

namespace Miner49er;

/// <summary>F3 debug overlay: frame rate, frame timings, current floor/mode,
/// the player roster, and a few Godot engine counters. Purely client-side —
/// no data crosses the wire. Toggled on/off by <see cref="InputBindings.Stats"/>.</summary>
public partial class StatsOverlay : CanvasLayer
{
	private MatchClient _client = null!;
	private RichTextLabel _label = null!;

	public void Init(MatchClient client) => _client = client;

	public void Toggle() => Visible = !Visible;

	public override void _Ready()
	{
		Layer = 11; // just above the minimap (10)
		Visible = false;

		var panel = new PanelContainer
		{
			MouseFilter = Control.MouseFilterEnum.Ignore,
			OffsetLeft = 8f,
			OffsetTop  = 44f,
		};
		var bg = new StyleBoxFlat { BgColor = new Color(0f, 0f, 0f, 0.72f) };
		bg.SetContentMarginAll(8f);
		bg.SetCornerRadiusAll(4);
		panel.AddThemeStyleboxOverride("panel", bg);
		AddChild(panel);

		_label = new RichTextLabel
		{
			BbcodeEnabled = true,
			FitContent    = true,
			ScrollActive  = false,
			AutowrapMode  = TextServer.AutowrapMode.Off,
			MouseFilter   = Control.MouseFilterEnum.Ignore,
			CustomMinimumSize = new Vector2(240f, 0f),
		};
		_label.AddThemeFontOverride("normal_font", ThemeDB.FallbackFont);
		_label.AddThemeFontSizeOverride("normal_font_size", 15);
		panel.AddChild(_label);
	}

	public override void _Process(double delta)
	{
		if (!Visible) return;
		_label.Text = BuildText();
	}

	private string BuildText()
	{
		var sb = new StringBuilder();

		// Performance
		double fps       = Engine.GetFramesPerSecond();
		double procMs    = (double)Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000.0;
		double physMs    = (double)Performance.GetMonitor(Performance.Monitor.TimePhysicsProcess) * 1000.0;
		sb.Append($"[b]FPS[/b] {fps:0}   [b]frame[/b] {procMs:0.0}ms   [b]phys[/b] {physMs:0.0}ms\n");

		// Match
		var nm = NetworkManager.Instance;
		sb.Append($"[b]Floor[/b] {nm.MatchFloor}   [b]Mode[/b] {nm.MatchMode}\n");

		// Players
		var miners = _client.Miners;
		sb.Append($"\n[b]Players ({miners.Count})[/b]\n");
		foreach (var m in miners)
		{
			Color col = PlayerColors.At(m.Id);
			string name = PlayerColors.Names[((m.Id % PlayerColors.Names.Length) + PlayerColors.Names.Length) % PlayerColors.Names.Length];
			string state = m.Alive ? "✓" : "✗";
			string you = m.Id == _client.LocalMinerId ? "  (you)" : "";
			sb.Append($"[color=#{col.ToHtml(false)}]P{m.Id} {name}[/color]  {state}  {m.Gold}g{you}\n");
		}

		// Engine
		double memMb     = (double)Performance.GetMonitor(Performance.Monitor.MemoryStatic) / (1024.0 * 1024.0);
		double drawCalls = (double)Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame);
		double objects   = (double)Performance.GetMonitor(Performance.Monitor.ObjectCount);
		double nodes     = (double)Performance.GetMonitor(Performance.Monitor.ObjectNodeCount);
		sb.Append($"\n[b]Mem[/b] {memMb:0.0}MB   [b]Draw[/b] {drawCalls:0}\n");
		sb.Append($"[b]Objects[/b] {objects:0}   [b]Nodes[/b] {nodes:0}");

		return sb.ToString();
	}
}
