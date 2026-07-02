using Godot;
using System;

namespace Miner49er;

public partial class ResultsOverlay : CanvasLayer
{
	private Label  _label      = null!;
	private Label  _scoreLabel = null!;
	private Button _return     = null!;
	private Button _playAgain  = null!;
	private bool   _hostControls;

	public override void _Ready()
	{
		Layer = 50;
		var center = new CenterContainer();
		center.AnchorLeft = 0f; center.AnchorRight = 1f;
		center.AnchorTop = 0.05f; center.AnchorBottom = 0.55f;
		AddChild(center);

		var box = new VBoxContainer();
		center.AddChild(box);

		_label = new Label { HorizontalAlignment = HorizontalAlignment.Center };
		_label.AddThemeFontSizeOverride("font_size", 40);
		box.AddChild(_label);

		_scoreLabel = new Label
		{
			HorizontalAlignment = HorizontalAlignment.Center,
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
			CustomMinimumSize = new Vector2(480, 0),
		};
		_scoreLabel.AddThemeFontSizeOverride("font_size", 22);
		box.AddChild(_scoreLabel);

		box.AddChild(new Label { Text = "" }); // spacer

		_playAgain = new Button { Text = "Play Again", Visible = false };
		box.AddChild(_playAgain);

		_return = new Button { Text = "Return to Lobby" };
		_return.Pressed += () => NetworkManager.Instance.ReturnToLobby();
		box.AddChild(_return);
	}

	public void Show(string text, bool hostControls, string buttonText = "Return to Lobby",
	                 string scoreText = "", Action? playAgain = null)
	{
		_label.Text         = text;
		_scoreLabel.Text    = scoreText;
		_scoreLabel.Visible = scoreText.Length > 0;
		_return.Text        = buttonText;
		_hostControls       = hostControls;
		_return.Visible     = hostControls;

		// Disconnect any old handler before wiring a new one.
		foreach (var conn in _playAgain.GetSignalConnectionList("pressed"))
			_playAgain.Disconnect("pressed", (Callable)conn["callable"]);

		if (playAgain != null && hostControls)
		{
			_playAgain.Visible  = true;
			_playAgain.Pressed += playAgain;
		}
		else
		{
			_playAgain.Visible = false;
		}
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (!_hostControls) return;
		if (@event.IsActionPressed("ui_accept"))
		{
			GetViewport().SetInputAsHandled();
			NetworkManager.Instance.ReturnToLobby();
		}
	}
}
