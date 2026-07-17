using System;
using System.Collections.Generic;

namespace Miner49er.Core.Fx;

/// <summary>One lightweight particle. Colours are plain floats so the pure Core has no
/// Godot dependency; the renderer maps R/G/B/A to a Color and fades by Life/MaxLife.</summary>
public struct Particle
{
    public float X, Y;        // position (world px)
    public float Vx, Vy;      // velocity (px/sec)
    public float Gravity;     // added to Vy each second (px/sec^2)
    public float Life;        // remaining seconds; dead at <= 0
    public float MaxLife;     // initial life, for the fade ratio
    public float Size;        // half-extent in px
    public float R, G, B, A;  // base colour; A is the peak alpha
}

/// <summary>Fixed-capacity particle pool. Never allocates after construction: Emit past
/// capacity replaces the particle closest to expiring; Update integrates + swap-removes
/// the dead. Pure C#; client-side cosmetic only.</summary>
public sealed class ParticleSystem
{
    private readonly Particle[] _pool;
    private int _count;

    public ParticleSystem(int capacity) => _pool = new Particle[capacity];

    public int Count => _count;
    public int Capacity => _pool.Length;

    public void Emit(in Particle p)
    {
        if (_count < _pool.Length)
        {
            _pool[_count++] = p;
            return;
        }
        // At capacity: overwrite whichever particle is nearest death.
        int min = 0;
        for (int i = 1; i < _count; i++)
            if (_pool[i].Life < _pool[min].Life) min = i;
        _pool[min] = p;
    }

    public void Update(float dt)
    {
        for (int i = _count - 1; i >= 0; i--)
        {
            ref var p = ref _pool[i];
            p.X += p.Vx * dt;
            p.Y += p.Vy * dt;
            p.Vy += p.Gravity * dt;
            p.Life -= dt;
            if (p.Life <= 0f)
            {
                _pool[i] = _pool[_count - 1];
                _count--;
            }
        }
    }

    /// <summary>Live particles, valid indices [0, Count). Backed by the internal array.</summary>
    public IReadOnlyList<Particle> Live => new ArraySegment<Particle>(_pool, 0, _count);
}
