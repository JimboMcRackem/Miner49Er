using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Miner49er.Core;
using Miner49er.Core.AI;

namespace Miner49er;

// Dev-only play-through driver: boots straight into a real match on a fixed seed, then runs
// a list of steps - held keys go through the same input actions as a player, and screenshots
// come off the real viewport. Launched with tools/playtest.ps1; nothing in the game references it.
public partial class Harness : Node
{
	private const int Port = 17849; // not DefaultPort, so a run can sit beside a real game

	private bool _isDriver;
	private Dictionary<string, string> _opts = new();
	private string _outDir = "";

	public override void _Ready()
	{
		if (!_isDriver)
		{
			// The match replaces the current scene, so the driver has to live outside it.
			GetTree().Root.CallDeferred(Node.MethodName.AddChild, new Harness { _isDriver = true });
			return;
		}
		ProcessMode = ProcessModeEnum.Always; // keep stepping through the pause menu and shop
		_ = Run();
	}

	private async Task Run()
	{
		try
		{
			_opts = ParseArgs(OS.GetCmdlineUserArgs());
			_outDir = Opt("out", ProjectSettings.GlobalizePath("user://playtest"));
			Directory.CreateDirectory(_outDir);

			StartMatch();
			foreach (var step in Opt("steps", "wait 3; shot start").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
			{
				GD.Print($"[harness] {step}");
				await RunStep(step.Split(' ', StringSplitOptions.RemoveEmptyEntries));
			}
			GD.Print("[harness] done");
			GetTree().Quit(0);
		}
		catch (Exception e)
		{
			GD.PrintErr($"[harness] FAILED: {e.Message}");
			GetTree().Quit(1);
		}
	}

	private void StartMatch()
	{
		InputBindings.EnsureDefaults();
		var nm = NetworkManager.Instance;
		var err = nm.HostGame("Harness", 0, overInternet: false, port: Port);
		if (err != Error.Ok) throw new InvalidOperationException($"HostGame: {err}");
		for (int i = 0; i < OptInt("bots", 0); i++)
			nm.AddBot(Enum.Parse<BotSkill>(Opt("botskill", "Foreman"), ignoreCase: true));

		void OnStarting()
		{
			nm.MatchStarting -= OnStarting;
			GetTree().ChangeSceneToFile("res://game/Main.tscn");
		}
		nm.MatchStarting += OnStarting;

		if (_opts.TryGetValue("eventdelay", out var eventDelay))
			nm.DevEventDelay = double.Parse(eventDelay, System.Globalization.CultureInfo.InvariantCulture);
		if (_opts.TryGetValue("event", out var eventKind))
			nm.DevEventKind = Enum.Parse<Miner49er.Core.MineEventKind>(eventKind, ignoreCase: true);
		nm.StartMatch(Enum.Parse<GameMode>(Opt("mode", "Expedition"), ignoreCase: true),
			OptInt("time", 0), OptBool("flood"), OptBool("pits"), OptBool("caveins"), OptBool("lava"),
			0.12f, OptInt("scale", 1), startFloor: OptInt("floor", 1), visionRadius: OptInt("vision", 5),
			seed: OptInt("seed", 4949));
	}

	private async Task RunStep(string[] s)
	{
		switch (s[0])
		{
			case "wait":
				await Seconds(float.Parse(s[1]));
				break;
			case "hold": // hold <action> <seconds>
				Input.ActionPress(s[1]);
				await Seconds(float.Parse(s[2]));
				Input.ActionRelease(s[1]);
				break;
			case "press": // press <action> - stays down, so frames can be captured mid-move
				Input.ActionPress(s[1]);
				break;
			case "release": // release <action>
				Input.ActionRelease(s[1]);
				break;
			case "timescale": // timescale <factor> - slow motion; later wait/hold/burst times scale too
				Engine.TimeScale = float.Parse(s[1]);
				break;
			case "tap": // tap <action> - pressed inside a physics tick, or IsActionJustPressed misses it
				await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
				Input.ActionPress(s[1]);
				await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
				Input.ActionRelease(s[1]);
				break;
			case "shot": // shot <name>
				await Shot(s[1]);
				break;
			case "burst": // burst <name> <count> <seconds between>
				for (int i = 0, n = int.Parse(s[2]); i < n; i++)
				{
					await Shot($"{s[1]}_{i:D2}");
					await Seconds(float.Parse(s[3]));
				}
				break;
			default:
				throw new ArgumentException($"unknown step '{s[0]}'");
		}
	}

	private async Task Shot(string name)
	{
		await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
		var img = GetViewport().GetTexture().GetImage();
		if (img.GetFormat() == Image.Format.Rgbh) img = EncodeSrgb(img);
		string path = Path.Combine(_outDir, name + ".png");
		var err = img.SavePng(path);
		if (err != Error.Ok) throw new IOException($"save {path}: {err}");
		GD.Print($"[harness] wrote {path}");
	}

	// hdr_2d hands back linear half-floats; the window shows them sRGB-encoded. Image.LinearToSrgb
	// only takes 8-bit input, and quantising linear values first would crush the dark caves.
	private static Image EncodeSrgb(Image linear)
	{
		byte[] src = linear.GetData();
		byte[] dst = new byte[src.Length / 2];
		for (int i = 0; i < dst.Length; i++)
		{
			float c = Math.Clamp((float)BitConverter.ToHalf(src, i * 2), 0f, 1f);
			float s = c <= 0.0031308f ? c * 12.92f : 1.055f * MathF.Pow(c, 1f / 2.4f) - 0.055f;
			dst[i] = (byte)(s * 255f + 0.5f);
		}
		return Image.CreateFromData(linear.GetWidth(), linear.GetHeight(), false, Image.Format.Rgb8, dst);
	}

	private async Task Seconds(float s) =>
		await ToSignal(GetTree().CreateTimer(s, processAlways: true), SceneTreeTimer.SignalName.Timeout);

	private string Opt(string key, string fallback) => _opts.GetValueOrDefault(key, fallback);
	private int OptInt(string key, int fallback) => _opts.TryGetValue(key, out var v) ? int.Parse(v) : fallback;
	private bool OptBool(string key) => _opts.TryGetValue(key, out var v) && v != "false";

	private static Dictionary<string, string> ParseArgs(IEnumerable<string> args) =>
		args.Select(a => a.TrimStart('-').Split('=', 2))
			.ToDictionary(kv => kv[0], kv => kv.Length > 1 ? kv[1] : "true");
}
