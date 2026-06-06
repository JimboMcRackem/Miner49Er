using Godot;
using Miner49er.Core;

namespace Miner49er;

public partial class Main : Node2D
{
    public const int TileSize = 32;
    public const float MoveTime = 0.12f; // seconds per tile step
    public const int VisionRadius = 5;
    private const int LocalId = 1;

    private Simulation _sim = null!;
    private Miner _local = null!;
    private readonly FogState _fog = new();

    private WorldRenderer _world = null!;
    private FogRenderer _fogRenderer = null!;
    private Node2D _playerVisual = null!;
    private Camera2D _camera = null!;
    private Hud _hud = null!;

    private bool _isMoving;
    private float _moveT;
    private Vector2 _moveFrom;
    private Vector2 _moveTo;

    public override void _Ready()
    {
        InputBindings.EnsureDefaults();
        StartNewGame(seed: 12345);
    }

    private void StartNewGame(int seed)
    {
        var map = MapGenerator.Generate(new MapConfig { Seed = seed, PlayerCount = 1 });
        _sim = new Simulation(map.Grid, new SimConfig());
        _local = _sim.AddMiner(LocalId, map.Spawns[0]);
        _isMoving = false;

        if (_world == null)
        {
            _world = new WorldRenderer { Name = "WorldRenderer", ZIndex = -10 };
            AddChild(_world);
            _world.Init(this);
        }

        // Persistent nodes are created once; restart only resets sim + position.
        if (_playerVisual == null)
        {
            _playerVisual = BuildPlayerVisual();
            AddChild(_playerVisual);
            _camera = new Camera2D { Zoom = new Vector2(1.5f, 1.5f) };
            _playerVisual.AddChild(_camera);
            _camera.MakeCurrent();
        }
        _playerVisual.Position = ToPixelCenter(_local.Pos);

        if (_fogRenderer == null)
        {
            _fogRenderer = new FogRenderer { Name = "FogRenderer", ZIndex = -5 };
            AddChild(_fogRenderer);
            _fogRenderer.Init(this);
        }

        if (_hud == null)
        {
            _hud = new Hud { Name = "Hud" };
            AddChild(_hud);
        }

        UpdateFog();
    }

    private static Node2D BuildPlayerVisual()
    {
        var root = new Node2D { Name = "PlayerVisual", ZIndex = 10 };
        var body = new Polygon2D
        {
            Color = new Color("e8c34a"),
            Polygon = new[]
            {
                new Vector2(-10, -10), new Vector2(10, -10),
                new Vector2(10, 10), new Vector2(-10, 10),
            },
        };
        root.AddChild(body);
        return root;
    }

    private static Vector2 ToPixelCenter(GridPos p) =>
        new(p.X * TileSize + TileSize / 2f, p.Y * TileSize + TileSize / 2f);

    public override void _PhysicsProcess(double delta)
    {
        HandleActions();
        HandleMovement(delta);
        _sim.Tick(delta);
        ConsumeEvents();
        _hud.SetText(BuildHudText());
    }

    private string BuildHudText()
    {
        string status = _local.Alive
            ? _local.Activity switch
            {
                ActivityKind.Mining => $"Mining… {_local.ActivitySecondsRemaining:0.0}s",
                ActivityKind.Planting => $"Planting… {_local.ActivitySecondsRemaining:0.0}s",
                _ => "Ready",
            }
            : "Dead — press R to restart";
        return $"Gold: {_local.GoldCollected}    {status}";
    }

    private void ConsumeEvents()
    {
        foreach (var e in _sim.DrainEvents())
        {
            switch (e)
            {
                case Explosion ex:
                    _world.AddExplosionFlash(ex.WallPos);
                    foreach (var d in ex.DestroyedRock) _world.AddExplosionFlash(d);
                    UpdateFog(); // blasting reveals new space
                    break;
                case RockMined:
                    UpdateFog(); // digging reveals new space
                    break;
                case MinerKilled k when k.MinerId == LocalId:
                    // Phase 1: dying just stops input until R restarts.
                    break;
            }
        }
    }

    private void HandleActions()
    {
        if (Input.IsActionJustPressed(InputBindings.Pickaxe)) _sim.TryStartMining(LocalId);
        if (Input.IsActionJustPressed(InputBindings.Plant)) _sim.TryStartPlanting(LocalId);
        if (Input.IsActionJustPressed(InputBindings.Restart)) StartNewGame(12345);
    }

    private void HandleMovement(double delta)
    {
        if (!_isMoving && _local.Alive)
        {
            Direction? dir = ReadDirection();
            if (dir.HasValue)
            {
                var before = _local.Pos;
                if (_sim.TryMove(LocalId, dir.Value))
                {
                    _moveFrom = ToPixelCenter(before);
                    _moveTo = ToPixelCenter(_local.Pos);
                    _moveT = 0f;
                    _isMoving = true;
                    UpdateFog();
                }
            }
        }

        if (_isMoving)
        {
            _moveT += (float)delta / MoveTime;
            if (_moveT >= 1f) { _moveT = 1f; _isMoving = false; }
            _playerVisual.Position = _moveFrom.Lerp(_moveTo, _moveT);
        }
    }

    private static Direction? ReadDirection()
    {
        if (Input.IsActionPressed(InputBindings.MoveUp)) return Direction.North;
        if (Input.IsActionPressed(InputBindings.MoveDown)) return Direction.South;
        if (Input.IsActionPressed(InputBindings.MoveLeft)) return Direction.West;
        if (Input.IsActionPressed(InputBindings.MoveRight)) return Direction.East;
        return null;
    }

    private void UpdateFog()
    {
        _fog.Update(Visibility.Compute(_sim.Grid, _local.Pos, VisionRadius));
    }

    public Simulation Sim => _sim;
    public Miner Local => _local;
    public FogState Fog => _fog;
}
