using System;

namespace Miner49er.Core;

/// <summary>One sweeping arm of an Octopus. Oscillates ±45° around its rest angle
/// at 30°/sec, pausing 1 second at each end.</summary>
public sealed class OctopusArm
{
    public double RestAngle;       // 0=N, 90=E, 180=S, 270=W (degrees clockwise from +Y-down)
    public double CurrentAngle;
    public int    SwingDir = 1;    // +1 or -1
    public double PauseRemaining;

    public const double ArcHalfWidth = 45.0;
    public const double AngularSpeed = 30.0;   // degrees / second
    public const double PauseSeconds = 1.0;
    public const int    Length       = 5;      // tiles from octopus center

    public OctopusArm(double restAngle, int startDir = 1)
    {
        RestAngle    = restAngle;
        CurrentAngle = restAngle + (-ArcHalfWidth * startDir);  // start at one arc end
        SwingDir     = startDir;
    }

    public void Advance(double dt)
    {
        if (PauseRemaining > 0)
        {
            PauseRemaining = Math.Max(0.0, PauseRemaining - dt);
            return;
        }
        CurrentAngle += AngularSpeed * SwingDir * dt;
        double lo = RestAngle - ArcHalfWidth;
        double hi = RestAngle + ArcHalfWidth;
        if (CurrentAngle >= hi)
        {
            CurrentAngle   = hi;
            SwingDir       = -1;
            PauseRemaining = PauseSeconds;
        }
        else if (CurrentAngle <= lo)
        {
            CurrentAngle   = lo;
            SwingDir       = 1;
            PauseRemaining = PauseSeconds;
        }
    }
}
