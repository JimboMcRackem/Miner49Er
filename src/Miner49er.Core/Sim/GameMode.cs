namespace Miner49er.Core;

public enum GameMode { LastManStanding, GoldRush, ReachCenter }

public static class GameModeExtensions
{
    public const double GoldRushTimeLimitSeconds = 120.0;

    /// <summary>Per-mode time budget in seconds; null = untimed.</summary>
    public static double? TimeLimitSeconds(this GameMode mode) => mode switch
    {
        GameMode.GoldRush => GoldRushTimeLimitSeconds,
        _ => null,
    };
}
