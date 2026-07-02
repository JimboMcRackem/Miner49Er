using Godot;

namespace Miner49er;

public partial class CreditsPanel : CanvasLayer
{
	public bool IsOpen { get; private set; }

	public override void _Ready()
	{
		Layer = 30;
		Visible = false;

		var bg = new ColorRect
		{
			Color = new Color(0, 0, 0, 0.88f),
			AnchorLeft = 0f, AnchorRight = 1f, AnchorTop = 0f, AnchorBottom = 1f,
		};
		AddChild(bg);

		var outer = new CenterContainer();
		outer.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		AddChild(outer);

		var frame = new VBoxContainer { CustomMinimumSize = new Vector2(560, 0) };
		outer.AddChild(frame);

		var titleLabel = new Label { Text = "CREDITS", HorizontalAlignment = HorizontalAlignment.Center };
		titleLabel.AddThemeFontSizeOverride("font_size", 36);
		frame.AddChild(titleLabel);

		frame.AddChild(new HSeparator());

		var scroll = new ScrollContainer
		{
			CustomMinimumSize = new Vector2(560, 520),
			HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
		};
		frame.AddChild(scroll);

		var box = new VBoxContainer { CustomMinimumSize = new Vector2(540, 0) };
		scroll.AddChild(box);

		AddSection(box, "GAME");
		AddEntry(box, "Design, code & concept by JimboMcRackem");

		AddSection(box, "ART");
		AddEntry(box, "All sprites generated with PixelLab AI  —  pixellab.ai");

		AddSection(box, "MUSIC  (via Pixabay)");
		AddEntry(box, "\"Progressive Ambient Loop with Solfeggio 108 BPM\"  by TwinFishAudio");
		AddEntry(box, "\"Scary\"  by AtlasAudio");
		AddEntry(box, "\"Nature\"  by AtlasAudio");
		AddEntry(box, "\"Ragtime Piano Honky Tonk Uptown Uproar\"  by BackDrop");
		AddEntry(box, "\"Scary Background Music\"  by SoundGalleryByDmitryTaras");
		AddEntry(box, "\"Scary Horror\"  by the_mountain");
		AddEntry(box, "\"Scary Horror Music Horror\"  by VibeHorn");
		AddEntry(box, "\"Time Zone\"  by kond1");
		AddEntry(box, "\"Inspiring Cinematic Ambient\"  by lexin_music");

		AddSection(box, "SOUND EFFECTS");
		AddEntry(box, "Kenney (CC0)  —  kenney.nl");
		AddEntry(box, "  Impact Sounds · RPG Audio · Interface Sounds · Sci-fi Sounds");
		AddEntry(box, "");
		AddEntry(box, "Freesound.org");
		AddEntry(box, "  \"horse-clip-clopping\" by swiftoid  (#182504)");
		AddEntry(box, "  \"two-goats-bleating\" by edufigueres  (#267146)");
		AddEntry(box, "  \"whisper\" by ianstargem  (#341841)");
		AddEntry(box, "  \"broken-egg-squelch\" by dnaudiouk  (#461049)");
		AddEntry(box, "  \"voice-adult-male-paingrunts\" by mrfossy  (#547209)");
		AddEntry(box, "  \"cat-hiss\" by cdonahueucsd  (#620962)");
		AddEntry(box, "  \"zombie-roar\" by arrigd  (#144002)");

		AddSection(box, "BUILT WITH");
		AddEntry(box, "Godot Engine 4  —  godotengine.org");
		AddEntry(box, ".NET / C#");

		frame.AddChild(new HSeparator());

		var closeBtn = new Button { Text = "Close" };
		closeBtn.Pressed += Close;
		frame.AddChild(closeBtn);
	}

	public void Open()
	{
		Visible = true;
		IsOpen  = true;
	}

	public void Close()
	{
		Visible = false;
		IsOpen  = false;
	}

	private static void AddSection(VBoxContainer box, string text)
	{
		box.AddChild(new Label { Text = "" });
		var label = new Label { Text = text };
		label.AddThemeFontSizeOverride("font_size", 18);
		label.AddThemeColorOverride("font_color", new Color(1f, 0.85f, 0.4f));
		box.AddChild(label);
	}

	private static void AddEntry(VBoxContainer box, string text)
	{
		var label = new Label
		{
			Text = text,
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
			CustomMinimumSize = new Vector2(520, 0),
		};
		label.AddThemeFontSizeOverride("font_size", 15);
		box.AddChild(label);
	}
}
