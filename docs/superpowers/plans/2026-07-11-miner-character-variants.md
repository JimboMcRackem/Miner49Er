# Miner Character Variants Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let each player pick one of 5 playable miner characters (default + Burly Prospector, Gaunt Digger, Grizzled Veteran, Stout Matron) in the menus, cosmetic and client-side, synced through the lobby like team colour.

**Architecture:** Variant is a presentation-only index on `PlayerInfo`, synced alongside `ColorIndex`. Each variant is a full parallel sprite folder under `res://assets/miners/<slug>/`. `MatchClient` builds tinted textures lazily, one set per `(variant, colour)` pair actually in the match, with raw source images cached per variant and per-frame fallback to the base (variant 0) art when a variant's file is missing. Menu adds a character picker + live preview; the lobby shows each player's character name.

**Tech Stack:** Godot 4.6 / C# / .NET 10 (game assembly `Miner49er`), PixelLab MCP for art. No `Miner49er.Core` changes.

## Global Constraints

- **No `Miner49er.Core` changes.** Variant is presentation-only, like team colour. No Core tests.
- **Keep team-colour tint.** New art is drawn neutral/grey so `TintMiner` recolours the clothing; skin + helmet preserved by the existing rule `px.S > 0.3 && px.H < 0.25 && lum > 0.25`.
- **Wire safety.** Append the new synced field/array; never reorder existing lobby-RPC fields. A missing variant defaults to 0.
- **Run `godot` via PowerShell only** (Bash shim breaks headless import with a false "assemblies not found"). Import art with `godot --headless --import --quit`.
- **Never `git add -A`.** Stage feature files by explicit path. Do not stage `*.uid`, `.superpowers/`, `Temp/`, `_preview_*`, or unrelated pre-existing edits.
- **PixelLab API key** must never be written to a memory file or committed.
- End every commit message with `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`.
- Verification for code tasks is `dotnet build` (compile gate) + the stated manual Godot check — this is Godot-assembly presentation code with no xUnit seam, exactly like the existing colour system.

## Setup (before Task 1)

Create the feature branch (do not commit to `main` directly):

```bash
git checkout -b feat/miner-variants
```

## File Structure

- **Create `game/net/MinerVariants.cs`** — static roster: names, slugs, `Count`, `Clamp`, `Prefix`. Single responsibility: variant metadata + asset-path prefix. Pure C#, no Godot types.
- **Modify `game/net/NetworkManager.cs`** — add `VariantIndex` to `PlayerInfo`; thread it through `HostGame`/`JoinGame`/`JoinByCode`/`SubmitPlayerInfo`/`OnConnectedToServer`; add `variants[]` to `BroadcastLobby`/`ReceiveLobby`; random variant in `AddBot`.
- **Modify `game/net/MatchClient.cs`** — replace the seven `_miner*Tex` array fields (incl. `_minerThrowTex` from the throw-stone feature) + their seven `BuildMiner*` builders with a lazy `(variant,colour)` texture-set cache (`MinerTextureSet` + `MinerSourceImages`, each carrying a `Throw[4,4]` array), a per-variant source cache, `GetOrBuildSet`, `MinerVariantIndex`; rewrite the draw block to index the set (keeping the throw-priority branch and the `_throwUntil` dictionary).
- **Modify `game/audio/SettingsStore.cs`** — persist/restore `variant` in the `player` section.
- **Modify `game/ui/MainMenu.cs`** — add solo + multi character `OptionButton` and a live preview `TextureRect`; pass the selected variant into Host/Join/Solo calls; persist via SettingsStore.
- **Modify `game/ui/Lobby.cs`** — append the character name to each player row.
- **Create `assets/miners/{burly,gaunt,veteran,matron}/…`** — full ~91-frame sprite sets from PixelLab (Tasks 5–6).

---

### Task 1: `MinerVariants` roster + `PlayerInfo.VariantIndex` net sync

**Files:**
- Create: `game/net/MinerVariants.cs`
- Modify: `game/net/NetworkManager.cs` (PlayerInfo ~13-18; HostGame ~65-89; JoinGame ~106-117; JoinByCode ~121-135; OnConnectedToServer ~173-176; SubmitPlayerInfo ~181-188; AddBot ~216-224; BroadcastLobby ~282-295; ReceiveLobby ~297-304)

**Interfaces:**
- Produces:
  - `MinerVariants.Names : string[]`, `MinerVariants.Slugs : string[]`, `MinerVariants.Count : int`, `MinerVariants.Clamp(int) : int`, `MinerVariants.Prefix(int) : string`
  - `PlayerInfo.VariantIndex : int`
  - `NetworkManager.HostGame(string, int, bool, int, int)` — trailing `int variantIndex = 0`
  - `NetworkManager.JoinGame(string, string, int, int, int)` — trailing `int variantIndex = 0`
  - `NetworkManager.JoinByCode(string, string, int, int)` — trailing `int variantIndex = 0`

- [ ] **Step 1: Create `game/net/MinerVariants.cs`**

```csharp
namespace Miner49er;

/// <summary>Playable miner character variants. Cosmetic only, synced like team colour.
/// Variant 0 is the default sprite at res://assets/miners/; others live in a slug subfolder.</summary>
public static class MinerVariants
{
    public static readonly string[] Names =
        { "Classic Miner", "Burly Prospector", "Gaunt Digger", "Grizzled Veteran", "Stout Matron" };

    // Asset subfolder for each variant; "" means the base res://assets/miners/ path.
    public static readonly string[] Slugs = { "", "burly", "gaunt", "veteran", "matron" };

    public static int Count => Names.Length;

    public static int Clamp(int i) => (i % Count + Count) % Count;

    /// <summary>Path prefix under res://assets/miners/ for a variant, with trailing slash for slugs.</summary>
    public static string Prefix(int variant)
    {
        string slug = Slugs[Clamp(variant)];
        return slug == "" ? "" : slug + "/";
    }
}
```

- [ ] **Step 2: Add `VariantIndex` to `PlayerInfo`** (`game/net/NetworkManager.cs` ~13-18)

```csharp
public struct PlayerInfo
{
    public string Name;
    public int ColorIndex;
    public int VariantIndex;
    public bool Ready;
}
```

- [ ] **Step 3: Thread variant through host/join entry points**

`HostGame` signature (append trailing optional param) and the `Players[LocalId]` line:

```csharp
public Error HostGame(string playerName, int colorIndex, bool overInternet = false, int port = DefaultPort, int variantIndex = 0)
{
```
```csharp
        Players[LocalId] = new PlayerInfo { Name = playerName, ColorIndex = colorIndex, VariantIndex = variantIndex, Ready = false };
```

`JoinGame` (append trailing param; store a pending field):

```csharp
public Error JoinGame(string address, string playerName, int colorIndex, int port = DefaultPort, int variantIndex = 0)
{
    ResetPeer();
    var peer = new ENetMultiplayerPeer();
    var err = peer.CreateClient(address, port);
    if (err != Error.Ok) return err;
    Multiplayer.MultiplayerPeer = peer;
    IsHost = false;
    _pendingName = playerName;
    _pendingColor = colorIndex;
    _pendingVariant = variantIndex;
    return Error.Ok;
}
```

Add the field next to `_pendingColor` (near line 42):

```csharp
	private int _pendingColor;
	private int _pendingVariant;
```

`JoinByCode` (append trailing param; forward it in both `JoinGame` calls):

```csharp
public Error JoinByCode(string input, string playerName, int colorIndex, int variantIndex = 0)
{
    var trimmed = (input ?? "").Trim();
    if (Miner49er.Core.Net.ConnectCode.TryDecode(trimmed, out var ip, out var port))
        return JoinGame(new IPAddress(ip).ToString(), playerName, colorIndex, port, variantIndex);

    int p = DefaultPort;
    int idx = trimmed.LastIndexOf(':');
    if (idx > 0 && int.TryParse(trimmed[(idx + 1)..], out var parsed))
    {
        p = parsed;
        trimmed = trimmed[..idx];
    }
    return JoinGame(trimmed, playerName, colorIndex, p, variantIndex);
}
```

- [ ] **Step 4: Send + receive variant in the join handshake** (`OnConnectedToServer` ~173, `SubmitPlayerInfo` ~181)

```csharp
	private void OnConnectedToServer()
	{
		RpcId(1, nameof(SubmitPlayerInfo), _pendingName, _pendingColor, _pendingVariant);
	}
```
```csharp
	[Rpc(MultiplayerApi.RpcMode.AnyPeer)]
	public void SubmitPlayerInfo(string name, int colorIndex, int variantIndex)
	{
		if (!IsHost) return;
		long sender = Multiplayer.GetRemoteSenderId();
		Players[sender] = new PlayerInfo { Name = name, ColorIndex = colorIndex, VariantIndex = variantIndex, Ready = false };
		BroadcastLobby();
	}
```

- [ ] **Step 5: Random variant for bots** (`AddBot` ~216, plus a helper)

```csharp
	public void AddBot(BotSkill skill)
	{
		if (!IsHost || Players.Count >= 8) return;
		long fakeId = _nextBotFakeId--;
		string name = $"{PickBotName()} ({BotSkillDisplayName(skill)})";
		Players[fakeId] = new PlayerInfo { Name = name, ColorIndex = PickBotColor(), VariantIndex = PickBotVariant(), Ready = true };
		_botSkills[fakeId] = skill;
		BroadcastLobby();
	}

	private int PickBotVariant() => (int)(GD.Randi() % (uint)MinerVariants.Count);
```

- [ ] **Step 6: Serialise variant in the lobby broadcast** (`BroadcastLobby` ~282, `ReceiveLobby` ~297)

```csharp
	private void BroadcastLobby()
	{
		var ids = new long[Players.Count];
		var names = new string[Players.Count];
		var colors = new int[Players.Count];
		var variants = new int[Players.Count];
		var readys = new int[Players.Count]; // bool[] is not a Godot Variant; use int (0/1) instead
		int i = 0;
		foreach (var (id, info) in Players)
		{
			ids[i] = id; names[i] = info.Name; colors[i] = info.ColorIndex;
			variants[i] = info.VariantIndex; readys[i] = info.Ready ? 1 : 0; i++;
		}
		Rpc(nameof(ReceiveLobby), ids, names, colors, variants, readys);
		ReceiveLobby(ids, names, colors, variants, readys); // apply locally on host too
	}

	[Rpc(MultiplayerApi.RpcMode.Authority)]
	public void ReceiveLobby(long[] ids, string[] names, int[] colors, int[] variants, int[] readys)
	{
		Players.Clear();
		for (int i = 0; i < ids.Length; i++)
			Players[ids[i]] = new PlayerInfo
			{
				Name = names[i],
				ColorIndex = colors[i],
				VariantIndex = variants != null && i < variants.Length ? variants[i] : 0,
				Ready = readys[i] != 0,
			};
		LobbyChanged?.Invoke();
	}
```

- [ ] **Step 7: Compile**

Run: `dotnet build`
Expected: `Build succeeded. 0 Error(s)` (existing MainMenu callers still compile — the new params are trailing + optional).

- [ ] **Step 8: Commit**

```bash
git add game/net/MinerVariants.cs game/net/NetworkManager.cs
git commit -m "feat(variants): add MinerVariants roster and sync VariantIndex through lobby"
```

---

### Task 2: Lazy per-(variant,colour) texture cache + draw path in `MatchClient`

**Files:**
- Modify: `game/net/MatchClient.cs` (fields ~75-83; builder calls ~133-139; idle-fidget gate ~333; draw block ~426-465; `MinerColorIndex` ~478; delete builders ~488-589; keep `TintMiner` ~591-607). The throw-stone feature already added a seventh `_minerThrowTex` field, its `BuildMinerActivityTextures("throw", 4)` call, and a throw-priority branch in the draw block — all folded into the set cache here.

**Interfaces:**
- Consumes: `MinerVariants.Prefix(int)`, `MinerVariants.Clamp(int)` (Task 1); `PlayerInfo.VariantIndex` (Task 1).
- Produces: nested `MinerTextureSet`, `MinerSourceImages`; `GetOrBuildSet(int variant, int color) : MinerTextureSet`; `MinerVariantIndex(int) : int`.

- [ ] **Step 1: Replace the seven texture fields** (`game/net/MatchClient.cs` ~75-83)

Delete these seven lines:

```csharp
	private Texture2D[,]?  _minerTex;       // [colorIndex 0-7, facing 0=N 1=E 2=S 3=W]
	private Texture2D[,]?  _minerListenTex; // [colorIndex, facing] — null if sprites not yet placed
	private Texture2D[,]?  _minerIdleTex;   // [colorIndex, idleIdx] south-facing only; null until art placed
	private Texture2D[,,]? _minerWalkTex;  // [colorIndex, facing, frame 0-3]
	private Texture2D[,,]? _minerMineTex;  // [colorIndex, facing, frame 0-6]
	private Texture2D[,,]? _minerPlantTex; // [colorIndex, facing, frame 0-4]
	private Texture2D[,,]? _minerThrowTex; // [colorIndex, facing, frame 0-3]
```

(The `_throwUntil` dictionary field added by the throw-stone feature stays — the draw block still reads it; only the texture arrays move into the set cache.)

Replace with the two caches:

```csharp
	// Tinted miner textures, built lazily one set per (variant, colour) pair actually in the match.
	private readonly System.Collections.Generic.Dictionary<(int variant, int color), MinerTextureSet> _minerSets = new();
	// Raw source images per variant, loaded once (with base fallback) then re-tinted per colour.
	private readonly System.Collections.Generic.Dictionary<int, MinerSourceImages> _variantSources = new();
```

- [ ] **Step 2: Add the two set classes** (place just above `BuildMinerTextures`, ~line 488, before deleting it)

```csharp
	private sealed class MinerTextureSet
	{
		public readonly Texture2D?[]  Facing = new Texture2D?[4];
		public readonly Texture2D?[]  Listen = new Texture2D?[4];
		public readonly Texture2D?[]  Idle   = new Texture2D?[IdleVariantCount];
		public readonly Texture2D?[,] Walk   = new Texture2D?[4, 4];
		public readonly Texture2D?[,] Mine   = new Texture2D?[4, 7];
		public readonly Texture2D?[,] Plant  = new Texture2D?[4, 5];
		public readonly Texture2D?[,] Throw  = new Texture2D?[4, 4];
	}

	private sealed class MinerSourceImages
	{
		public readonly Image?[]  Facing = new Image?[4];
		public readonly Image?[]  Listen = new Image?[4];
		public readonly Image?[]  Idle   = new Image?[IdleVariantCount];
		public readonly Image?[,] Walk   = new Image?[4, 4];
		public readonly Image?[,] Mine   = new Image?[4, 7];
		public readonly Image?[,] Plant  = new Image?[4, 5];
		public readonly Image?[,] Throw  = new Image?[4, 4];
	}
```

- [ ] **Step 3: Add loaders + set builder** (place next to the set classes)

```csharp
	// Loads a miner frame for a variant, falling back to the base (variant 0) art if the
	// variant's own file is missing, so a not-yet-drawn variant still renders.
	private static Image? LoadMinerImage(int variant, string relative)
	{
		string prefix = MinerVariants.Prefix(variant);
		var img = GD.Load<CompressedTexture2D>($"res://assets/miners/{prefix}{relative}")?.GetImage();
		if (img == null && prefix != "")
			img = GD.Load<CompressedTexture2D>($"res://assets/miners/{relative}")?.GetImage();
		if (img != null) img.Convert(Image.Format.Rgba8);
		return img;
	}

	private MinerSourceImages GetVariantSources(int variant)
	{
		variant = MinerVariants.Clamp(variant);
		if (_variantSources.TryGetValue(variant, out var cached)) return cached;
		var s = new MinerSourceImages();
		var dir = new[] { "n", "e", "s", "w" };
		for (int d = 0; d < 4; d++)
		{
			s.Facing[d] = LoadMinerImage(variant, $"miner_{dir[d]}.png");
			s.Listen[d] = LoadMinerImage(variant, $"listen/{dir[d]}.png");
			for (int f = 0; f < 4; f++) s.Walk[d, f]  = LoadMinerImage(variant, $"walk/{dir[d]}{f}.png");
			for (int f = 0; f < 7; f++) s.Mine[d, f]  = LoadMinerImage(variant, $"mine/{dir[d]}{f}.png");
			for (int f = 0; f < 5; f++) s.Plant[d, f] = LoadMinerImage(variant, $"plant/{dir[d]}{f}.png");
			for (int f = 0; f < 4; f++) s.Throw[d, f] = LoadMinerImage(variant, $"throw/{dir[d]}{f}.png");
		}
		for (int i = 0; i < IdleVariantCount; i++) s.Idle[i] = LoadMinerImage(variant, $"idle/idle{i}.png");
		_variantSources[variant] = s;
		return s;
	}

	private MinerTextureSet GetOrBuildSet(int variant, int color)
	{
		variant = MinerVariants.Clamp(variant);
		if (_minerSets.TryGetValue((variant, color), out var set)) return set;
		var src = GetVariantSources(variant);
		var tint = PlayerColors.At(color);
		set = new MinerTextureSet();
		for (int d = 0; d < 4; d++)
		{
			if (src.Facing[d] != null) set.Facing[d] = ImageTexture.CreateFromImage(TintMiner(src.Facing[d]!, tint));
			if (src.Listen[d] != null) set.Listen[d] = ImageTexture.CreateFromImage(TintMiner(src.Listen[d]!, tint));
			for (int f = 0; f < 4; f++) if (src.Walk[d, f]  != null) set.Walk[d, f]  = ImageTexture.CreateFromImage(TintMiner(src.Walk[d, f]!,  tint));
			for (int f = 0; f < 7; f++) if (src.Mine[d, f]  != null) set.Mine[d, f]  = ImageTexture.CreateFromImage(TintMiner(src.Mine[d, f]!,  tint));
			for (int f = 0; f < 5; f++) if (src.Plant[d, f] != null) set.Plant[d, f] = ImageTexture.CreateFromImage(TintMiner(src.Plant[d, f]!, tint));
			for (int f = 0; f < 4; f++) if (src.Throw[d, f] != null) set.Throw[d, f] = ImageTexture.CreateFromImage(TintMiner(src.Throw[d, f]!, tint));
		}
		for (int i = 0; i < IdleVariantCount; i++) if (src.Idle[i] != null) set.Idle[i] = ImageTexture.CreateFromImage(TintMiner(src.Idle[i]!, tint));
		_minerSets[(variant, color)] = set;
		return set;
	}

	private static int MinerVariantIndex(int minerId)
	{
		var nm = NetworkManager.Instance;
		int idx = minerId - 1;
		if (idx >= 0 && idx < nm.PeerOrder.Length &&
			nm.Players.TryGetValue(nm.PeerOrder[idx], out var info))
			return MinerVariants.Clamp(info.VariantIndex);
		return 0;
	}
```

- [ ] **Step 4: Delete the six old builders** (`game/net/MatchClient.cs` — the methods `BuildMinerTextures`, `BuildMinerListenTextures`, `BuildMinerIdleTextures`, `BuildMinerWalkTextures`, `BuildMinerActivityTextures`, ~488-589). **Keep** `TintMiner` (~591-607) — the set builder uses it.

- [ ] **Step 5: Replace the seven builder calls with cache warm-up** (~133-139)

Delete:

```csharp
		_minerTex       = BuildMinerTextures();
		_minerListenTex = BuildMinerListenTextures();
		_minerIdleTex   = BuildMinerIdleTextures();
		_minerWalkTex   = BuildMinerWalkTextures();
		_minerMineTex   = BuildMinerActivityTextures("mine",  7);
		_minerPlantTex  = BuildMinerActivityTextures("plant", 5);
		_minerThrowTex  = BuildMinerActivityTextures("throw", 4);
```

Replace with:

```csharp
		// Warm the tint cache for every (variant, colour) pair present at match start.
		// Any pair not covered here (edge cases) is built lazily on first draw.
		_minerSets.Clear();
		_variantSources.Clear();
		foreach (var info in NetworkManager.Instance.Players.Values)
			GetOrBuildSet(MinerVariants.Clamp(info.VariantIndex), info.ColorIndex);
```

- [ ] **Step 6: Update the idle-fidget gate** (~333)

Change:

```csharp
					&& !Listening && _minerIdleTex != null;
```
to:

```csharp
					&& !Listening;
```

- [ ] **Step 7: Rewrite the draw block** (~426-465) — replace everything from `int colorIdx = MinerColorIndex(m.Id);` down to the end of the `else` that assigns `tex = _minerTex?[colorIdx, facing];` with:

```csharp
			int colorIdx = MinerColorIndex(m.Id);
			int variant  = MinerVariantIndex(m.Id);
			int facing   = m.Facing;
			var set      = GetOrBuildSet(variant, colorIdx);

			double drawNow = Time.GetTicksMsec() / 1000.0;
			Texture2D? tex;
			bool throwing = _throwUntil.TryGetValue(m.Id, out double throwEnd) && drawNow < throwEnd;
			if (throwing)
			{
				// Throwing a stone: play the 4-frame overhand toss across the flight window.
				double telapsed = MatchClient.StoneFlightSeconds - (throwEnd - drawNow);
				int frame = Mathf.Clamp((int)(telapsed / MatchClient.StoneFlightSeconds * 4), 0, 3);
				tex = set.Throw[facing, frame];
			}
			else if (m.Activity == 1 && m.ActivityRemaining > 0)
			{
				// Mining: loop 6 animated frames (1-6) at 6 fps
				int frame = (int)(drawNow * 6 % 6) + 1;
				tex = set.Mine[facing, frame];
			}
			else if ((m.Activity == 2 || m.Activity == 3) && m.ActivityRemaining > 0)
			{
				// Planting / PlantingDetonator: loop 4 animated frames (1-4) at 4 fps
				int frame = (int)(drawNow * 4 % 4) + 1;
				tex = set.Plant[facing, frame];
			}
			else
			{
				bool walking = _walkUntil.TryGetValue(m.Id, out double until) && drawNow < until;
				if (walking)
				{
					double elapsed = m.MoveSeconds - (until - drawNow);
					int frame = (int)(elapsed / m.MoveSeconds * 4) % 4;
					tex = set.Walk[facing, frame];
				}
				else if ((m.Id == LocalMinerId && Listening) || (m.Id != LocalMinerId && m.Listening))
				{
					tex = set.Listen[facing];
				}
				else if (_currentIdleIdx >= 0 && m.Id == LocalMinerId)
				{
					tex = set.Idle[_currentIdleIdx]; // south-facing fidget pose
				}
				else
				{
					tex = set.Facing[facing];
				}
			}
```

(The following `if (tex != null) DrawTextureRect(...) else DrawRect(...)` block is unchanged and still handles a null frame by drawing the colour square.)

- [ ] **Step 8: Compile**

Run: `dotnet build`
Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 9: Manual verify — default character unchanged**

Run the game (PowerShell): `godot` → start a solo Expedition. The Classic miner must render exactly as before across idle, walking, mining, planting, and listening. (Only variant 0 art exists at this point, so this proves the refactor is behaviour-preserving.)

- [ ] **Step 10: Commit**

```bash
git add game/net/MatchClient.cs
git commit -m "refactor(variants): lazy per-(variant,colour) miner texture cache with base fallback"
```

---

### Task 3: Character picker + live preview in `MainMenu`, persisted

**Files:**
- Modify: `game/audio/SettingsStore.cs` (`LoadPlayer` ~90-100, `SavePlayerIdentity` ~102-109, `SavePlayer` ~111-120)
- Modify: `game/ui/MainMenu.cs` (fields ~13-27; solo panel ~144-149; multi panel ~206-211; `BuildColorPicker` area ~265-276; `OnHost` ~297-303; `OnJoin` ~305-311; `OnSoloExpedition` ~313-324; `OnContinueExpedition` ~326-337; `TintGrayscale` ~356-370)

**Interfaces:**
- Consumes: `MinerVariants.Names`, `MinerVariants.Prefix`, `MinerVariants.Count`, `MinerVariants.Clamp` (Task 1); `NetworkManager.HostGame/JoinByCode` trailing `variantIndex` (Task 1).
- Produces: `SettingsStore.LoadPlayer()` now returns a 5-tuple including `int variant`; `SavePlayer`/`SavePlayerIdentity` take a trailing `int variantIndex`.

- [ ] **Step 1: Persist variant in `SettingsStore`** (`game/audio/SettingsStore.cs`)

`LoadPlayer` — add `variant` to the returned tuple:

```csharp
	public static (string name, int colorIndex, int variant, string address, bool overInternet) LoadPlayer()
	{
		var cfg = new ConfigFile();
		if (cfg.Load(Path) != Error.Ok)
			return ("Miner", 0, 0, "127.0.0.1", true);
		string name    = (string)cfg.GetValue(PlayerSection, "name",          "Miner");
		int    color   = (int)(long)cfg.GetValue(PlayerSection, "color",      0L);
		int    variant = (int)(long)cfg.GetValue(PlayerSection, "variant",    0L);
		string address = (string)cfg.GetValue(PlayerSection, "address",       "127.0.0.1");
		bool   inet    = (bool)cfg.GetValue(PlayerSection, "over_internet",   true);
		return (name, Mathf.Clamp(color, 0, 7), Mathf.Clamp(variant, 0, MinerVariants.Count - 1), address, inet);
	}

	public static void SavePlayerIdentity(string name, int colorIndex, int variantIndex)
	{
		var cfg = new ConfigFile();
		cfg.Load(Path);
		cfg.SetValue(PlayerSection, "name",    name);
		cfg.SetValue(PlayerSection, "color",   (long)colorIndex);
		cfg.SetValue(PlayerSection, "variant", (long)variantIndex);
		cfg.Save(Path);
	}

	public static void SavePlayer(string name, int colorIndex, int variantIndex, string address, bool overInternet)
	{
		var cfg = new ConfigFile();
		cfg.Load(Path);
		cfg.SetValue(PlayerSection, "name",          name);
		cfg.SetValue(PlayerSection, "color",         (long)colorIndex);
		cfg.SetValue(PlayerSection, "variant",       (long)variantIndex);
		cfg.SetValue(PlayerSection, "address",       address);
		cfg.SetValue(PlayerSection, "over_internet", overInternet);
		cfg.Save(Path);
	}
```

- [ ] **Step 2: Add picker + preview fields** (`game/ui/MainMenu.cs` ~13-27)

```csharp
	// Solo sub-panel fields
	private LineEdit     _soloName    = null!;
	private OptionButton _soloColor   = null!;
	private OptionButton _soloVariant = null!;
	private TextureRect  _soloPreview = null!;
```
```csharp
	// Multiplayer sub-panel fields
	private LineEdit     _multiName    = null!;
	private OptionButton _multiColor   = null!;
	private OptionButton _multiVariant = null!;
	private TextureRect  _multiPreview = null!;
```

- [ ] **Step 3: Add the variant picker + preview builders** (near `BuildColorPicker` ~265)

```csharp
	private static OptionButton BuildVariantPicker(int selected)
	{
		var picker = new OptionButton { CustomMinimumSize = new Vector2(180, 0) };
		for (int i = 0; i < MinerVariants.Count; i++)
			picker.AddItem(MinerVariants.Names[i], i);
		picker.Select(MinerVariants.Clamp(selected));
		return picker;
	}

	// South-facing still of `variant`, tinted with `color`, for the menu preview.
	private static Texture2D? BuildPreview(int variant, int color)
	{
		string prefix = MinerVariants.Prefix(variant);
		var ctex = GD.Load<CompressedTexture2D>($"res://assets/miners/{prefix}miner_s.png")
		         ?? GD.Load<CompressedTexture2D>("res://assets/miners/miner_s.png");
		var src = ctex?.GetImage();
		if (src == null) return null;
		src.Convert(Image.Format.Rgba8);
		return ImageTexture.CreateFromImage(TintGrayscale(src, PlayerColors.At(color)));
	}
```

- [ ] **Step 4: Wire solo panel** (`game/ui/MainMenu.cs` ~133 and ~144-149)

At the `LoadPlayer` destructure in the solo builder (~133), capture the variant:

```csharp
		var (savedName, savedColor, savedVariant, _, _) = SettingsStore.LoadPlayer();
```

After `box.AddChild(_soloColor);` (~149) add the variant picker + preview and their change handlers:

```csharp
		_soloVariant = BuildVariantPicker(savedVariant);
		box.AddChild(_soloVariant);
		_soloPreview = new TextureRect
		{
			CustomMinimumSize = new Vector2(64, 64),
			StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
			Texture = BuildPreview(savedVariant, savedColor),
		};
		box.AddChild(_soloPreview);
		_soloColor.ItemSelected   += _ => _soloPreview.Texture = BuildPreview(_soloVariant.Selected, _soloColor.Selected);
		_soloVariant.ItemSelected += _ => _soloPreview.Texture = BuildPreview(_soloVariant.Selected, _soloColor.Selected);
```

- [ ] **Step 5: Wire multi panel** (`game/ui/MainMenu.cs` ~196 and ~206-211)

At the multi builder's `LoadPlayer` destructure (~196):

```csharp
		var (savedName, savedColor, savedVariant, savedAddress, savedInternet) = SettingsStore.LoadPlayer();
```

After `box.AddChild(_multiColor);` (~211):

```csharp
		_multiVariant = BuildVariantPicker(savedVariant);
		box.AddChild(_multiVariant);
		_multiPreview = new TextureRect
		{
			CustomMinimumSize = new Vector2(64, 64),
			StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
			Texture = BuildPreview(savedVariant, savedColor),
		};
		box.AddChild(_multiPreview);
		_multiColor.ItemSelected   += _ => _multiPreview.Texture = BuildPreview(_multiVariant.Selected, _multiColor.Selected);
		_multiVariant.ItemSelected += _ => _multiPreview.Texture = BuildPreview(_multiVariant.Selected, _multiColor.Selected);
```

- [ ] **Step 6: Pass variant into the launchers** (`OnHost`, `OnJoin`, `OnSoloExpedition`, `OnContinueExpedition`)

```csharp
	private void OnHost()
	{
		SettingsStore.SavePlayer(_multiName.Text, _multiColor.Selected, _multiVariant.Selected, _address.Text, _internet.ButtonPressed);
		var err = NetworkManager.Instance.HostGame(_multiName.Text, _multiColor.Selected, _internet.ButtonPressed, NetworkManager.DefaultPort, _multiVariant.Selected);
		if (err != Error.Ok) { _status.Text = $"Host failed: {err}"; return; }
		GetTree().ChangeSceneToFile("res://game/ui/Lobby.tscn");
	}

	private void OnJoin()
	{
		SettingsStore.SavePlayer(_multiName.Text, _multiColor.Selected, _multiVariant.Selected, _address.Text, _internet.ButtonPressed);
		var err = NetworkManager.Instance.JoinByCode(_address.Text, _multiName.Text, _multiColor.Selected, _multiVariant.Selected);
		if (err != Error.Ok) { _status.Text = $"Join failed: {err}"; return; }
		GetTree().ChangeSceneToFile("res://game/ui/Lobby.tscn");
	}
```

`OnSoloExpedition` — update the identity save and both host calls:

```csharp
		SettingsStore.SavePlayerIdentity(_soloName.Text, _soloColor.Selected, _soloVariant.Selected);
```
```csharp
		var err = NetworkManager.Instance.HostGame(_soloName.Text, _soloColor.Selected, overInternet: false, variantIndex: _soloVariant.Selected);
```

`OnContinueExpedition` — same two updates:

```csharp
		SettingsStore.SavePlayerIdentity(_soloName.Text, _soloColor.Selected, _soloVariant.Selected);
```
```csharp
		var err = nm.HostGame(_soloName.Text, _soloColor.Selected, overInternet: false, variantIndex: _soloVariant.Selected);
```

- [ ] **Step 7: Compile**

Run: `dotnet build`
Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 8: Manual verify — picker + preview**

Run the game (PowerShell): `godot`. In both Solo and Multiplayer panels: the character `OptionButton` lists all 5 names; changing character or colour updates the preview sprite (still shows the Classic art tinted, since only variant 0 exists); the choice survives a restart (persisted). Launch a solo game to confirm no regressions.

- [ ] **Step 9: Commit**

```bash
git add game/audio/SettingsStore.cs game/ui/MainMenu.cs
git commit -m "feat(variants): character picker + live preview in menus, persisted"
```

---

### Task 4: Lobby shows each player's character name

**Files:**
- Modify: `game/ui/Lobby.cs` (rows ~299-326)

**Interfaces:**
- Consumes: `MinerVariants.Names`, `MinerVariants.Clamp` (Task 1); `PlayerInfo.VariantIndex` (Task 1).

- [ ] **Step 1: Append the character name to both row types** (`game/ui/Lobby.cs` ~304-324)

Bot row `Text`:

```csharp
					Text = $"{info.Name}  [{MinerVariants.Names[MinerVariants.Clamp(info.VariantIndex)]}]  [READY]",
```

Human row `Text`:

```csharp
					Text = $"{info.Name}  [{MinerVariants.Names[MinerVariants.Clamp(info.VariantIndex)]}]  {(info.Ready ? "[READY]" : "[...]")}",
```

- [ ] **Step 2: Compile**

Run: `dotnet build`
Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 3: Manual verify**

Run the game (PowerShell): `godot`. Host a multiplayer lobby and Add Bot a few times; each row shows the character name in brackets. (Bots get random variants, so names will vary.)

- [ ] **Step 4: Commit**

```bash
git add game/ui/Lobby.cs
git commit -m "feat(variants): show character name in lobby rows"
```

---

### Task 5: Generate + wire the first variant (Burly Prospector) end-to-end

This task proves the PixelLab pipeline on ONE variant before spending credits on the rest. Uses the PixelLab MCP tools.

**Files:**
- Create: `assets/miners/burly/miner_{n,e,s,w}.png` (+ `.import`)
- Create: `assets/miners/burly/idle/idle{0,1,2}.png` (+ `.import`)
- Create: `assets/miners/burly/listen/{n,e,s,w}.png` (+ `.import`)
- Create: `assets/miners/burly/walk/{n,e,s,w}{0..3}.png` (+ `.import`)
- Create: `assets/miners/burly/mine/{n,e,s,w}{0..6}.png` (+ `.import`)
- Create: `assets/miners/burly/plant/{n,e,s,w}{0..4}.png` (+ `.import`)
- Create: `assets/miners/burly/throw/{n,e,s,w}{0..3}.png` (+ `.import`)

**Interfaces:**
- Consumes: `MinerVariants.Slugs[1] == "burly"` and the loader path scheme `res://assets/miners/burly/…` (Tasks 1-2). The folder/frame names above MUST match exactly what `GetVariantSources` requests.

- [ ] **Step 1: Inspect the reference miner** so the new character matches scale/framing/style. Read the default frames' size (e.g. `assets/miners/miner_s.png`) and note canvas size + top-down 4-direction style. The new character must match (same pixel dimensions, same 4 facings N/E/S/W).

- [ ] **Step 2: Create the character** with PixelLab `create_character`: a **fat, bearded miner in dungarees and a hard hat**, **neutral grey/brown palette** (so the team tint reads on the clothing), top-down, 4 directions, matching the reference size. Poll `get_character` until complete.

- [ ] **Step 3: Animate** with `animate_character` for each action, matching the default's frame counts: **walk = 4 frames**, **mine = 7 frames** (index 0 = wind-up/idle-mine pose + 1-6 swing loop), **plant = 5 frames** (0 + 1-4), **throw = 4 frames** (overhand stone toss: wind-up → cock → release → follow-through, per direction), plus **listen** (a single alert pose per direction) and **idle** (3 fidget frames, south-facing). Poll each job to completion.

- [ ] **Step 4: Download** every generated frame (curl the PixelLab image URLs) into the exact paths listed under **Files** above. Direction letters are `n,e,s,w`; frame indices are zero-based per the reference layout.

- [ ] **Step 5: Import into Godot** (PowerShell only):

Run: `godot --headless --import --quit`
Expected: completes with no import errors for the new `assets/miners/burly/**` files; each `.png` gets a sibling `.import`.

- [ ] **Step 6: Manual verify in-game** — pick **Burly Prospector** in the menu (preview should now show the burly south still), launch a solo game, and confirm the burly silhouette renders correctly across idle, walk (all 4 directions), mine, plant, throw, and listen, with the team colour tinting the clothing. Add a bot and confirm remote miners with a variant render too.

- [ ] **Step 7: Commit** (stage only the `.png` + `.import` files — never `*.uid`):

```bash
git add assets/miners/burly
git commit -m "art(variants): Burly Prospector full animation set (PixelLab)"
```

---

### Task 6: Generate the remaining three variants

Repeat Task 5's pipeline for each remaining character. Only start this once Burly (Task 5) is verified in-game.

**Files (per variant):** same tree as Task 5 under the variant's slug folder:
- `gaunt` — Gaunt Digger: emaciated, sallow, tied trousers + t-shirt, cloth cap
- `veteran` — Grizzled Veteran: wiry, stubble, eyepatch, headlamp helmet
- `matron` — Stout Matron: stocky woman miner, headscarf, rolled sleeves

**Interfaces:**
- Consumes: `MinerVariants.Slugs[2..4] == { "gaunt", "veteran", "matron" }` (Task 1). Folder/frame names MUST match `GetVariantSources`.

- [ ] **Step 1: `gaunt`** — create_character (neutral palette, matching reference) → animate (walk 4 / mine 7 / plant 5 / throw 4 / listen / idle 3) → download to `assets/miners/gaunt/…` → `godot --headless --import --quit` (PowerShell) → verify in-game (select Gaunt Digger, all animations) → `git add assets/miners/gaunt` → commit `art(variants): Gaunt Digger full animation set (PixelLab)`.

- [ ] **Step 2: `veteran`** — same pipeline → `assets/miners/veteran/…` → import → verify (select Grizzled Veteran) → `git add assets/miners/veteran` → commit `art(variants): Grizzled Veteran full animation set (PixelLab)`.

- [ ] **Step 3: `matron`** — same pipeline → `assets/miners/matron/…` → import → verify (select Stout Matron) → `git add assets/miners/matron` → commit `art(variants): Stout Matron full animation set (PixelLab)`.

- [ ] **Step 4: Final verify** — in one session, cycle all 5 characters in the menu (preview correct each time), and in a match with several bots confirm a mix of variants render with correct per-player tint across every animation (idle, walk, mine, plant, throw, listen). Confirm a wire-safety sanity check: host + join locally, both see each other's chosen character.

---

## Self-Review

**Spec coverage:**
- No Core changes → all tasks are game-assembly only. ✅
- Keep team tint / neutral art → `TintMiner` reused unchanged (Task 2); art brief says neutral palette (Tasks 5-6). ✅
- Wire safety (append field, default 0) → `VariantIndex` appended to struct; `variants[]` appended to RPC; `ReceiveLobby` defaults missing/short to 0 (Task 1). ✅
- Roster + slugs → `MinerVariants` (Task 1); folders (Tasks 5-6). ✅
- `PlayerInfo.VariantIndex` synced like colour → Tasks 1. ✅
- Lazy per-(variant,colour) cache + base fallback → Task 2. ✅
- `MinerVariantIndex` mirrors `MinerColorIndex` → Task 2. ✅
- Draw indexes the set → Task 2 Step 7. ✅
- Selection UI (OptionButton + live preview, both panels) → Task 3. ✅
- Lobby shows character name → Task 4. ✅
- Bots random variant → Task 1 Step 5. ✅
- Build one PixelLab variant first, then the rest → Tasks 5 then 6. ✅
- Testing = manual Godot (no Core tests) → every code task ends with a `dotnet build` + manual check. ✅

- Throw pose folded in → `MinerTextureSet`/`MinerSourceImages` carry `Throw[4,4]`; `GetVariantSources` loads `throw/{dir}{0..3}.png`; `GetOrBuildSet` tints them; draw block keeps the throw-priority branch (indexing `set.Throw`) + `_throwUntil` from the throw-stone feature; art briefs add throw = 4 frames (Tasks 5-6). ✅

**Placeholder scan:** No TBD/TODO; every code step shows complete code. PixelLab art steps (5-6) are inherently generative but specify exact output paths, frame counts, and per-character briefs.

**Type consistency:** `GetOrBuildSet(int,int)`, `MinerVariantIndex(int)`, `MinerVariants.{Names,Slugs,Count,Clamp,Prefix}`, `PlayerInfo.VariantIndex`, `LoadPlayer()` 5-tuple, `SavePlayer(...,int,...)`, `SavePlayerIdentity(...,int)`, `HostGame(...,int variantIndex=0)`, `JoinByCode(...,int variantIndex=0)` — used consistently across Tasks 1-4. Frame-count constants (walk 4, mine 7, plant 5, throw 4, idle `IdleVariantCount`=3, listen 1/dir) match the existing arrays and the art briefs.
