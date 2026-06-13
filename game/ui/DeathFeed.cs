using Godot;
using System.Collections.Generic;
using Miner49er.Core;
using Miner49er.Core.Net;

namespace Miner49er;

/// <summary>Watches each miner's Alive flag flip true->false and announces the
/// death using the authoritative MinerSnapshot.Cause: a center banner for the
/// local miner, a short-lived stacking toast (top-right) for everyone else.</summary>
public partial class DeathFeed : CanvasLayer
{
	private MatchClient _client = null!;
	private readonly Dictionary<int, bool> _prevAlive = new();

	private Label _banner = null!;
	private float _bannerLife;
	private const float BannerSeconds = 3f;

	private VBoxContainer _feed = null!;
	private readonly List<(Label label, float life)> _toasts = new();
	private const float ToastSeconds = 4f;
	private const int MaxToasts = 4;

	public void Init(MatchClient client) => _client = client;

	public override void _Ready()
	{
		_banner = new Label
		{
			HorizontalAlignment = HorizontalAlignment.Center,
			AnchorLeft = 0f, AnchorRight = 1f, AnchorTop = 0.4f, AnchorBottom = 0.4f,
			Modulate = new Color(1, 1, 1, 0f),
		};
		_banner.AddThemeFontSizeOverride("font_size", 48);
		AddChild(_banner);

		_feed = new VBoxContainer
		{
			AnchorLeft = 1f, AnchorRight = 1f, AnchorTop = 0f, AnchorBottom = 0f,
			OffsetLeft = -320, OffsetTop = 16, OffsetRight = -16,
			GrowHorizontal = Control.GrowDirection.Begin,
		};
		AddChild(_feed);
	}

	public override void _Process(double delta)
	{
		if (_client == null) return;
		DetectDeaths();
		TickBanner((float)delta);
		TickToasts((float)delta);
	}

	private void DetectDeaths()
	{
		foreach (var m in _client.Miners)
		{
			bool prev = !_prevAlive.TryGetValue(m.Id, out var a) || a; // assume alive until first seen
			if (prev && !m.Alive && m.Cause != DeathCause.None)
			{
				if (m.Id == _client.LocalMinerId) ShowBanner(m.Cause);
				else PushToast(m.Id, m.Cause);
			}
			_prevAlive[m.Id] = m.Alive;
		}
	}

	private void ShowBanner(DeathCause cause)
	{
		_banner.Text = cause switch
		{
			DeathCause.Drowned => "YOU HAVE DROWNED",
			DeathCause.Exploded => "YOU WERE BLOWN UP",
			DeathCause.Fell => "YOU FELL INTO A PIT",
			_ => "YOU DIED",
		};
		_bannerLife = BannerSeconds;
	}

	private void TickBanner(float delta)
	{
		if (_bannerLife <= 0f) { _banner.Modulate = new Color(1, 1, 1, 0f); return; }
		_bannerLife -= delta;
		_banner.Modulate = new Color(1, 1, 1, Mathf.Min(1f, _bannerLife)); // fade over the last second
	}

	private void PushToast(int minerId, DeathCause cause)
	{
		string name = NameOf(minerId);
		string text = cause switch
		{
			DeathCause.Drowned => $"{name} drowned",
			DeathCause.Exploded => $"{name} was blown up",
			DeathCause.Left => $"{name} left",
			DeathCause.Fell => $"{name} fell into a pit",
			_ => $"{name} died",
		};
		var label = new Label { Text = text };
		label.AddThemeFontSizeOverride("font_size", 18);
		_feed.AddChild(label);
		_feed.MoveChild(label, 0); // newest on top
		_toasts.Add((label, ToastSeconds));

		while (_toasts.Count > MaxToasts)
		{
			var oldest = _toasts[0];
			_toasts.RemoveAt(0);
			if (IsInstanceValid(oldest.label)) oldest.label.QueueFree();
		}
	}

	private void TickToasts(float delta)
	{
		for (int i = _toasts.Count - 1; i >= 0; i--)
		{
			var t = _toasts[i];
			t.life -= delta;
			if (t.life <= 0f)
			{
				if (IsInstanceValid(t.label)) t.label.QueueFree();
				_toasts.RemoveAt(i);
			}
			else
			{
				if (IsInstanceValid(t.label))
					t.label.Modulate = new Color(1, 1, 1, Mathf.Min(1f, t.life));
				_toasts[i] = t;
			}
		}
	}

	private static string NameOf(int minerId)
	{
		var nm = NetworkManager.Instance;
		int idx = minerId - 1;
		if (idx >= 0 && idx < nm.PeerOrder.Length
			&& nm.Players.TryGetValue(nm.PeerOrder[idx], out var info))
			return info.Name;
		return $"Miner {minerId}";
	}
}
