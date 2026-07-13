using Godot;
using System;
using System.Collections.Generic;

namespace Miner49er;

public partial class ResultsOverlay : CanvasLayer
{
	private Label  _label      = null!;
	private Label  _scoreLabel = null!;
	private Button _return     = null!;
	private Button _playAgain  = null!;
	private bool   _hostControls;
	private Fireworks _fireworks = null!;

	public override void _Ready()
	{
		Layer = 50;

		// Celebration layer sits behind the text/buttons so they stay readable.
		_fireworks = new Fireworks { Name = "Fireworks", Visible = false };
		AddChild(_fireworks);

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
	                 string scoreText = "", Action? playAgain = null, bool celebrate = false)
	{
		_fireworks.Active  = celebrate;
		_fireworks.Visible = celebrate;
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

	/// <summary>Full-screen drawn fireworks: periodic bursts of gravity-fed, fading
	/// particles in festive colours. Purely cosmetic; ignores mouse so the buttons
	/// underneath stay clickable. Runs only while <see cref="Active"/> is true.</summary>
	private partial class Fireworks : Control
	{
		public bool Active;

		private struct Spark { public Vector2 Pos, Vel; public Color Col; public float Life, MaxLife; }

		private readonly List<Spark> _sparks = new();
		private readonly Random _rng = new();
		private double _nextBurst;

		private static readonly Color[] Palette =
		{
			new(1f, 0.85f, 0.25f), new(1f, 0.35f, 0.35f), new(0.35f, 0.75f, 1f),
			new(0.45f, 1f, 0.5f),  new(1f, 0.55f, 0.9f),  new(0.9f, 0.9f, 1f),
		};

		public override void _Ready()
		{
			SetAnchorsPreset(LayoutPreset.FullRect);
			MouseFilter = MouseFilterEnum.Ignore; // never eat the Return/Play Again clicks
		}

		public override void _Process(double delta)
		{
			if (!Active)
			{
				if (_sparks.Count > 0) { _sparks.Clear(); QueueRedraw(); }
				return;
			}

			_nextBurst -= delta;
			if (_nextBurst <= 0.0) { Burst(); _nextBurst = 0.45 + _rng.NextDouble() * 0.7; }

			float dt = (float)delta;
			for (int i = _sparks.Count - 1; i >= 0; i--)
			{
				var s = _sparks[i];
				s.Life -= dt;
				if (s.Life <= 0f) { _sparks.RemoveAt(i); continue; }
				s.Vel += new Vector2(0f, 160f) * dt; // gravity
				s.Pos += s.Vel * dt;
				_sparks[i] = s;
			}
			QueueRedraw();
		}

		private void Burst()
		{
			var size = Size;
			if (size.X < 1f || size.Y < 1f) size = GetViewportRect().Size;
			var origin = new Vector2(
				(float)(size.X * (0.18 + 0.64 * _rng.NextDouble())),
				(float)(size.Y * (0.12 + 0.38 * _rng.NextDouble())));
			var col = Palette[_rng.Next(Palette.Length)];
			int n = 28 + _rng.Next(18);
			for (int i = 0; i < n; i++)
			{
				float a = (float)(_rng.NextDouble() * Mathf.Tau);
				float speed = 90f + (float)_rng.NextDouble() * 140f;
				float life = 0.8f + (float)_rng.NextDouble() * 0.8f;
				_sparks.Add(new Spark
				{
					Pos = origin,
					Vel = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * speed,
					Col = col,
					Life = life,
					MaxLife = life,
				});
			}
		}

		public override void _Draw()
		{
			foreach (var s in _sparks)
			{
				float t = Mathf.Clamp(s.Life / s.MaxLife, 0f, 1f);
				var c = s.Col with { A = t };
				DrawCircle(s.Pos, 2.5f, c);
			}
		}
	}
}
