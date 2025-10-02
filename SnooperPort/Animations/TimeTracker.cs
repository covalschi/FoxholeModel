using System;
using System.Collections.Generic;
using FModel.Views.Snooper.Shading;

namespace FModel.Views.Snooper.Animations;

public enum ETrackerType
{
    Start,
    Frame,
    InBetween,
    End
}

public class TimeTracker : IDisposable
{
    public bool IsPaused;
    public bool IsActive;
    public float ElapsedTime;
    public float MaxElapsedTime;
    public float TimeMultiplier;

    public TimeTracker()
    {
        Reset();
    }

    public void Update(float deltaSeconds)
    {
        if (IsPaused || IsActive) return;
        ElapsedTime += deltaSeconds * TimeMultiplier;
        if (ElapsedTime >= MaxElapsedTime) Reset(false);
    }

    public void SafeSetElapsedTime(float elapsedTime)
    {
        ElapsedTime = Math.Clamp(elapsedTime, 0.0f, MaxElapsedTime);
    }

    public void SafeSetMaxElapsedTime(float maxElapsedTime)
    {
        MaxElapsedTime = MathF.Max(maxElapsedTime, MaxElapsedTime);
    }

    public void SafeSetMaxElapsedTime(IEnumerable<Animation> animations)
    {
        foreach (var animation in animations)
        {
            SafeSetMaxElapsedTime(animation.TotalElapsedTime);
        }
    }

    public void Reset(bool doMet = true)
    {
        IsPaused = false;
        ElapsedTime = 0.0f;
        if (doMet)
        {
            MaxElapsedTime = 0.01f;
            TimeMultiplier = 1f;
        }
    }

    public void Dispose()
    {
        Reset();
    }
}
