using Godot;

namespace Miner49er;

public partial class ResultsOverlay : CanvasLayer
{
	private Label _label = null!;
	private Button _return = null!;

	public override void _Ready()
	{
		Layer = 50;
		var center = new CenterContainer();
		center.AnchorLeft = 0f; center.AnchorRight = 1f;
		center.AnchorTop = 0.05f; center.AnchorBottom = 0.35f;
		AddChild(center);

		var box = new VBoxContainer();
		center.AddChild(box);

		_label = new Label();
		_label.AddThemeFontSizeOverride("font_size", 40);
		box.AddChild(_label);

		_return = new Button { Text = "Return to Lobby" };
		_return.Pressed += () => NetworkManager.Instance.ReturnToLobby();
		box.AddChild(_return);
	}

	public void Show(string text, bool hostControls, string buttonText = "Return to Lobby")
	{
		_label.Text = text;
		_return.Text = buttonText;
		_return.Visible = hostControls; // only the host returns everyone to the lobby
	}
}
