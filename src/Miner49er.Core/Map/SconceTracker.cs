namespace Miner49er.Core;

/// <summary>Which wall sconces this client's miner has walked near. They catch as you approach
/// and stay lit. Cosmetic, so each client keeps its own and nothing is networked.</summary>
public sealed class SconceTracker
{
    private const int LightRange = 5;
    private readonly List<GridPos> _dark;
    private readonly List<GridPos> _lit = new();

    public SconceTracker(IEnumerable<GridPos> sconces) => _dark = sconces.ToList();

    public IReadOnlyList<GridPos> Lit => _lit;
    public IReadOnlyList<GridPos> Dark => _dark;

    public bool IsLit(GridPos sconce) => _lit.Contains(sconce);

    /// <summary>Lights anything in range of the miner. True if a sconce caught this time.</summary>
    public bool Update(GridPos miner)
    {
        int before = _lit.Count;
        _lit.AddRange(_dark.Where(s => s.ChebyshevTo(miner) <= LightRange));
        _dark.RemoveAll(s => s.ChebyshevTo(miner) <= LightRange);
        return _lit.Count > before;
    }
}
