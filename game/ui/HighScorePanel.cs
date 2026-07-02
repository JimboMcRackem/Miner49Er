using Godot;

namespace Miner49er;

/// <summary>Overlay showing the top-10 high scores. Toggle via Open/Close.</summary>
public partial class HighScorePanel : CanvasLayer
{
	private VBoxContainer _rows = null!;

	public bool IsOpen { get; private set; }

	public override void _Ready()
	{
		Layer = 30;
		Visible = false;

		var bg = new ColorRect
		{
			Color = new Color(0, 0, 0, 0.85f),
			AnchorLeft = 0f, AnchorRight = 1f, AnchorTop = 0f, AnchorBottom = 1f,
		};
		AddChild(bg);

		var center = new CenterContainer();
		center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		AddChild(center);

		var box = new VBoxContainer { CustomMinimumSize = new Vector2(620, 0) };
		center.AddChild(box);

		var title = new Label { Text = "HIGH SCORES", HorizontalAlignment = HorizontalAlignment.Center };
		title.AddThemeFontSizeOverride("font_size", 36);
		box.AddChild(title);

		box.AddChild(new HSeparator());

		_rows = new VBoxContainer();
		box.AddChild(_rows);

		box.AddChild(new HSeparator());

		var closeBtn = new Button { Text = "Close" };
		closeBtn.Pressed += Close;
		box.AddChild(closeBtn);
	}

	public void Open()
	{
		Rebuild();
		Visible = true;
		IsOpen  = true;
	}

	public void Close()
	{
		Visible = false;
		IsOpen  = false;
	}

	private void Rebuild()
	{
		foreach (Node child in _rows.GetChildren()) child.QueueFree();

		// Header
		var headerColor = new Color(1f, 0.85f, 0.4f);
		_rows.AddChild(MakeRow("#  Name", "Score", "Gold", "Floor", "Date", 16, headerColor));
		_rows.AddChild(new HSeparator());

		var entries = ScoreStore.Load();
		if (entries.Count == 0)
		{
			var none = new Label { Text = "(no scores yet)", HorizontalAlignment = HorizontalAlignment.Center };
			_rows.AddChild(none);
			return;
		}

		for (int i = 0; i < entries.Count; i++)
		{
			var e = entries[i];
			string goldStr = e.Gold > 0 ? $"{e.Gold}g" : "-";
			_rows.AddChild(MakeRow($"{i + 1,2}. {e.Name}", e.Score.ToString(), goldStr, $"Fl.{e.Floor}", e.Date, 18, Colors.White));
		}
	}

	private static HBoxContainer MakeRow(string name, string score, string gold, string floor, string date, int fontSize, Color color)
	{
		var row = new HBoxContainer();

		var nameLbl = new Label
		{
			Text = name,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			HorizontalAlignment = HorizontalAlignment.Left,
		};
		nameLbl.AddThemeFontSizeOverride("font_size", fontSize);
		nameLbl.AddThemeColorOverride("font_color", color);

		var scoreLbl = new Label
		{
			Text = score,
			CustomMinimumSize = new Vector2(72, 0),
			HorizontalAlignment = HorizontalAlignment.Right,
		};
		scoreLbl.AddThemeFontSizeOverride("font_size", fontSize);
		scoreLbl.AddThemeColorOverride("font_color", color);

		var goldLbl = new Label
		{
			Text = gold,
			CustomMinimumSize = new Vector2(64, 0),
			HorizontalAlignment = HorizontalAlignment.Right,
		};
		goldLbl.AddThemeFontSizeOverride("font_size", fontSize);
		goldLbl.AddThemeColorOverride("font_color", color);

		var floorLbl = new Label
		{
			Text = floor,
			CustomMinimumSize = new Vector2(52, 0),
			HorizontalAlignment = HorizontalAlignment.Right,
		};
		floorLbl.AddThemeFontSizeOverride("font_size", fontSize);
		floorLbl.AddThemeColorOverride("font_color", color);

		var dateLbl = new Label
		{
			Text = date,
			CustomMinimumSize = new Vector2(100, 0),
			HorizontalAlignment = HorizontalAlignment.Right,
		};
		dateLbl.AddThemeFontSizeOverride("font_size", fontSize);
		dateLbl.AddThemeColorOverride("font_color", color);

		row.AddChild(nameLbl);
		row.AddChild(scoreLbl);
		row.AddChild(goldLbl);
		row.AddChild(floorLbl);
		row.AddChild(dateLbl);
		return row;
	}
}
