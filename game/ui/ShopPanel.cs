using Godot;
using Miner49er.Core;
using Miner49er.Core.Net;

namespace Miner49er;

/// <summary>Overlay shop UI shown when the local miner stands on the shopkeeper tile.
/// Opened and closed by Main._PhysicsProcess based on miner position.</summary>
public partial class ShopPanel : Control
{
	private static readonly ShopItemKind[] Items =
	{
		ShopItemKind.SpeedUp, ShopItemKind.VisionUp, ShopItemKind.BlastUp,
		ShopItemKind.LifePotion, ShopItemKind.Stones3,
	};

	private static readonly string[] ItemLabels =
	{
		"Speed Up   (+movement speed)",
		"Vision Up  (+fog radius)",
		"Blast Up   (+blast radius)",
		"Life Potion (restore 1 life)",
		"Stones x3  (throw to distract)",
	};

	public bool IsOpen { get; private set; }

	private int _selected;
	private Label[] _rows = null!;
	private MinerSnapshot _localMiner;
	private int _lives;
	private int _livesMax;

	public override void _Ready()
	{
		AnchorLeft = 0.3f; AnchorRight = 0.7f;
		AnchorTop  = 0.2f; AnchorBottom = 0.8f;

		var bg = new ColorRect
		{
			Color = new Color(0.05f, 0.05f, 0.05f, 0.92f),
			AnchorRight = 1f, AnchorBottom = 1f,
		};
		AddChild(bg);

		var vbox = new VBoxContainer
		{
			AnchorRight = 1f, AnchorBottom = 1f,
			OffsetLeft = 12, OffsetRight = -12, OffsetTop = 12, OffsetBottom = -12,
		};
		AddChild(vbox);

		var title = new Label { Text = "=== SHOP ===", HorizontalAlignment = HorizontalAlignment.Center };
		title.AddThemeFontSizeOverride("font_size", 20);
		vbox.AddChild(title);
		vbox.AddChild(new Label { Text = "" });

		_rows = new Label[Items.Length];
		for (int i = 0; i < Items.Length; i++)
		{
			_rows[i] = new Label();
			vbox.AddChild(_rows[i]);
		}

		vbox.AddChild(new Label { Text = "" });
		var footer = new Label { Text = "[↑↓] Navigate   [Space] Buy   [ESC] Close", HorizontalAlignment = HorizontalAlignment.Center };
		vbox.AddChild(footer);

		Visible = false;
	}

	public void Open(MinerSnapshot local, int lives, int livesMax)
	{
		_localMiner = local;
		_lives      = lives;
		_livesMax   = livesMax;
		_selected   = 0;
		IsOpen      = true;
		Visible     = true;
		Refresh();
	}

	public void Close()
	{
		IsOpen  = false;
		Visible = false;
	}

	public void UpdateSnapshot(MinerSnapshot local, int lives, int livesMax)
	{
		_localMiner = local;
		_lives      = lives;
		_livesMax   = livesMax;
		if (IsOpen) Refresh();
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (!IsOpen) return;
		if (@event.IsActionPressed(InputBindings.MoveUp))
		{
			_selected = (_selected - 1 + Items.Length) % Items.Length;
			Refresh();
			GetViewport().SetInputAsHandled();
		}
		else if (@event.IsActionPressed(InputBindings.MoveDown))
		{
			_selected = (_selected + 1) % Items.Length;
			Refresh();
			GetViewport().SetInputAsHandled();
		}
		else if (@event.IsActionJustPressed(InputBindings.UseItem))
		{
			TryBuy();
			GetViewport().SetInputAsHandled();
		}
		else if (@event.IsActionPressed(InputBindings.Exit))
		{
			Close();
			GetViewport().SetInputAsHandled();
		}
	}

	private void TryBuy()
	{
		var kind  = Items[_selected];
		int price = ShopPrices.Price(kind);
		if (_localMiner.Gold < price) return;
		if (IsAtCap(kind)) return;
		NetworkManager.Instance.BuyShopItem(kind);
	}

	private bool IsAtCap(ShopItemKind kind) => kind switch
	{
		ShopItemKind.LifePotion => _lives >= _livesMax,
		ShopItemKind.Stones3    => _localMiner.StoneCount >= 9,
		_ => false,  // perm upgrades: host enforces the cap
	};

	private void Refresh()
	{
		for (int i = 0; i < Items.Length; i++)
		{
			var kind  = Items[i];
			int price = ShopPrices.Price(kind);
			bool canBuy = _localMiner.Gold >= price && !IsAtCap(kind);
			string status = IsAtCap(kind) ? "MAX"
				: _localMiner.Gold < price ? "Can't afford"
				: "BUY";
			string prefix = i == _selected ? "▶ " : "  ";
			_rows[i].Text = $"{prefix}{ItemLabels[i]}   {price}g   [{status}]";
			_rows[i].Modulate = canBuy ? new Color(1, 1, 1) : new Color(0.5f, 0.5f, 0.5f);
		}
	}
}
