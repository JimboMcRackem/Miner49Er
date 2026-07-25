namespace Miner49er.Core;

public static class GameModeExtensions
{
    /// <summary>Modes where the winner is decided by rival kills, so kills —
    /// not gold — are the headline per-player stat on every stats surface
    /// (HUD, Tab scoreboard, F3 overlay, end-of-match results).</summary>
    public static bool IsKillScored(this GameMode mode) =>
        mode is GameMode.LastManStanding
             or GameMode.DemolitionDerby
             or GameMode.GrudgeMatch;
}
