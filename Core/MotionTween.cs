using System.Numerics;

namespace BiliPlay.Core;

public readonly record struct MotionTween(Vector4 Initial, Vector4 Target, Vector4 InitialVelocity, double Seconds)
{
    public (Vector4 Position,Vector4 Velocity) At(double elapsed)
    {
        if (elapsed <= 0) return (Initial,InitialVelocity);
        if (elapsed >= Seconds) return (Target,Vector4.Zero);
        var t=(float)(elapsed/Seconds);var duration=(float)Seconds;
        var t2=t*t;var t3=t2*t;
        return ((2*t3-3*t2+1)*Initial+(t3-2*t2+t)*duration*InitialVelocity+(-2*t3+3*t2)*Target,
            ((6*t2-6*t)*Initial+(3*t2-4*t+1)*duration*InitialVelocity+(-6*t2+6*t)*Target)/duration);
    }
}
