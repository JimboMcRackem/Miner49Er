using System.Collections.Generic;
using Godot;

namespace Miner49er;

public partial class Hud : CanvasLayer
{
	private Label _leftLabel    = null!;
	private Label _centerLabel  = null!;
	private Label _centerLabel2 = null!;
	private Label _rightLabel   = null!;
	private Label _hintLabel    = null!;

	public bool TimerUrgent { get; set; }

	public override void _Ready()
	{
		var bar = new HBoxContainer { Name = "HudBar" };
		bar.AnchorLeft   = 0f;
		bar.AnchorRight  = 1f;
		bar.AnchorTop    = 0f;
		bar.AnchorBottom = 0f;
		bar.OffsetLeft   = 8f;
		bar.OffsetRight  = -8f;
		bar.OffsetTop    = 6f;
		bar.OffsetBottom = 38f;
		AddChild(bar);

		var font = ThemeDB.FallbackFont;

		_leftLabel = MakeLabel(bar, font, 22, new Color(1f, 0.18f, 0.18f), HorizontalAlignment.Left);

		// Center slot: inner HBox with two equal columns so treasure hunt idol names
		// always align regardless of name length.
		var centerBox = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		bar.AddChild(centerBox);
		_centerLabel  = MakeLabel(centerBox, font, 20, Colors.White, HorizontalAlignment.Center);
		_centerLabel2 = MakeLabel(centerBox, font, 20, Colors.White, HorizontalAlignment.Center);
		_centerLabel2.Visible = false;

		_rightLabel = MakeLabel(bar, font, 20, Colors.White, HorizontalAlignment.Right);

		// Contextual hint — bottom-centre, shown when near an interactable.
		_hintLabel = new Label
		{
			Text = "",
			HorizontalAlignment = HorizontalAlignment.Center,
			AnchorLeft = 0f,  AnchorRight  = 1f,
			AnchorTop  = 1f,  AnchorBottom = 1f,
			OffsetTop  = -38f, OffsetBottom = -6f,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		_hintLabel.AddThemeFontOverride("font", font);
		_hintLabel.AddThemeFontSizeOverride("font_size", 17);
		_hintLabel.AddThemeColorOverride("font_color", new Color(1f, 1f, 0.6f, 0.92f));
		AddChild(_hintLabel);

		_coinLayer = new CoinLayer { HudRef = this, MouseFilter = Control.MouseFilterEnum.Ignore };
		_coinLayer.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		AddChild(_coinLayer);
	}

	// Gold nuggets that fly from where gold was picked up to the objective text, which pops as each lands.
	private sealed class Coin { public Vector2 From, Ctrl; public float T, Delay, Spin; public Vector2[] Shape = null!; }
	private readonly List<Coin> _coins = new();
	private CoinLayer _coinLayer = null!;
	private float _pop; // 1 when a nugget just landed, decays to 0
	private const float CoinFlightSeconds = 0.6f;
	private static readonly Color NuggetCol = new(1.5f, 1.15f, 0.3f);
	private static readonly Color NuggetDarkCol = new(0.55f, 0.33f, 0.06f);
	private static readonly Color NuggetShineCol = new(2.2f, 2.0f, 1.3f);

	public void CollectGold(Vector2 screenFrom, int nuggets)
	{
		for (int i = 0; i < nuggets; i++)
		{
			var side = new Vector2((float)GD.RandRange(-140.0, 140.0), (float)GD.RandRange(-60.0, 20.0));
			_coins.Add(new Coin
			{
				From = screenFrom, Ctrl = screenFrom + side + new Vector2(0, -80), Delay = i * 0.07f,
				Spin = (float)GD.RandRange(-9.0, 9.0), Shape = NuggetShape(),
			});
		}
	}

	// A lumpy 7-sided blob, each one a little different.
	private static Vector2[] NuggetShape()
	{
		var pts = new Vector2[7];
		for (int k = 0; k < pts.Length; k++)
		{
			float ang = Mathf.Tau * k / pts.Length;
			float r = (float)GD.RandRange(7.0, 11.0);
			pts[k] = new Vector2(Mathf.Cos(ang) * r * 1.15f, Mathf.Sin(ang) * r * 0.9f);
		}
		return pts;
	}

	private Vector2 CoinTarget() => _centerLabel.GlobalPosition + _centerLabel.Size * 0.5f;

	private partial class CoinLayer : Control
	{
		public Hud HudRef = null!;
		public override void _Draw() => HudRef.DrawCoins(this);
	}

	private void DrawCoins(Control c)
	{
		var to = CoinTarget();
		foreach (var coin in _coins)
		{
			if (coin.Delay > 0f) continue;
			float t = Mathf.Min(1f, coin.T / CoinFlightSeconds);
			float e = t * t * (3f - 2f * t);
			var p = coin.From.Lerp(coin.Ctrl, e).Lerp(coin.Ctrl.Lerp(to, e), e);
			c.DrawSetTransform(p, coin.T * coin.Spin, Vector2.One);
			c.DrawColoredPolygon(coin.Shape, NuggetDarkCol);
			c.DrawSetTransform(p + new Vector2(-0.8f, -0.8f), coin.T * coin.Spin, Vector2.One * 0.78f);
			c.DrawColoredPolygon(coin.Shape, NuggetCol);
			c.DrawSetTransform(p, 0f, Vector2.One);
			c.DrawRect(new Rect2(-4f, -4f, 3f, 3f), NuggetShineCol); // fixed glint, top-left, so it reads as light
			c.DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
		}
	}

	private void UpdateCoins(float dt)
	{
		for (int i = _coins.Count - 1; i >= 0; i--)
		{
			var coin = _coins[i];
			if (coin.Delay > 0f) { coin.Delay -= dt; continue; }
			coin.T += dt;
			if (coin.T >= CoinFlightSeconds) { _coins.RemoveAt(i); _pop = 1f; }
		}
		_pop = Mathf.Max(0f, _pop - dt * 3.5f);
		_centerLabel.PivotOffset = _centerLabel.Size * 0.5f;
		_centerLabel.Scale = Vector2.One * (1f + 0.22f * _pop);
		_centerLabel.AddThemeColorOverride("font_color", Colors.White.Lerp(new Color(1.5f, 1.2f, 0.4f), _pop));
		_coinLayer.QueueRedraw();
	}

	public override void _Process(double delta)
	{
		UpdateCoins((float)delta);
		if (TimerUrgent)
		{
			float flash = (Mathf.Sin((float)Time.GetTicksMsec() * 0.006f) + 1f) * 0.5f;
			_rightLabel.AddThemeColorOverride("font_color", new Color(1f, flash * 0.15f + 0.05f, 0.05f));
		}
		else
		{
			_rightLabel.AddThemeColorOverride("font_color", Colors.White);
		}
	}

	public void SetHint(string hint) => _hintLabel.Text = hint;

	// lives: ♥ count (0 hides them); center: mode/objective; right: status, held items, timer…
	public void SetHud(int lives, string center, string right)
	{
		_leftLabel.Text       = lives > 0 ? new string('♥', lives) : "";
		_centerLabel.Text     = center;
		_centerLabel2.Visible = false;
		_rightLabel.Text      = right.TrimStart();
	}

	// Treasure Hunt: two idol columns each get equal width so ○/✓ always aligns.
	public void SetTreasureHud(string idol1, bool found1, string idol2, bool found2, string right)
	{
		_leftLabel.Text       = "";
		_centerLabel.Text     = $"{(found1 ? "✓" : "○")} {idol1}";
		_centerLabel2.Visible = true;
		_centerLabel2.Text    = $"{(found2 ? "✓" : "○")} {idol2}";
		_rightLabel.Text      = right.TrimStart();
	}

	private static Label MakeLabel(HBoxContainer parent, Font font, int size, Color color, HorizontalAlignment align)
	{
		var lbl = new Label
		{
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			HorizontalAlignment = align,
			VerticalAlignment   = VerticalAlignment.Center,
		};
		lbl.AddThemeFontOverride("font", font);
		lbl.AddThemeFontSizeOverride("font_size", size);
		lbl.AddThemeColorOverride("font_color", color);
		parent.AddChild(lbl);
		return lbl;
	}
}
