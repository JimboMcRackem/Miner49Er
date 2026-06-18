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

		var box = new VBoxContainer { CustomMinimumSize = new Vector2(480, 0) };
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
			var row = new Label
			{
				Text = $"{i + 1,2}. {e.Name,-14} {e.Score,8}   Floor {e.Floor,-3}  {e.Date}",
				HorizontalAlignment = HorizontalAlignment.Left,
			};
			row.AddThemeFontSizeOverride("font_size", 18);
			_rows.AddChild(row);
		}
	}
}
