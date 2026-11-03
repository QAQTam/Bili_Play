using System.Numerics;

namespace BiliPlay.Core;

// Sample only when a cue changes, never on a rendering timer. This matches the native damped spring
// so a new target preserves both position and velocity of an interrupted lyric animation.
public readonly record struct SpringMotion(Vector3 Initial, Vector3 Target, Vector3 InitialVelocity,
    double Damping, double PeriodSeconds, double DelaySeconds = 0)
{
    public (Vector3 Position, Vector3 Velocity) At(double seconds)
    {
        if (seconds <= DelaySeconds) return (Initial, seconds < DelaySeconds ? Vector3.Zero : InitialVelocity);
        var t = seconds - DelaySeconds;
        var omega = 2 * Math.PI / PeriodSeconds;
        var attenuation = Damping * omega;
        var frequency = omega * Math.Sqrt(1 - Damping * Damping);
        var a = Initial - Target;
        var b = (InitialVelocity + a * (float)attenuation) / (float)frequency;
        var sin = (float)Math.Sin(frequency * t); var cos = (float)Math.Cos(frequency * t);
        var decay = (float)Math.Exp(-attenuation * t);
        var displacement = a * cos + b * sin;
        return (Target + displacement * decay,
            ((-a * sin + b * cos) * (float)frequency - displacement * (float)attenuation) * decay);
    }
}
