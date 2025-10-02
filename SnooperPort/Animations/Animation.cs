using System;
using System.Collections.Generic;
using CUE4Parse_Conversion.Animations.PSA;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Objects.Core.Misc;
using FModel.Views.Snooper.Models;

namespace FModel.Views.Snooper.Animations;

public class Animation : IDisposable
{
    private readonly UObject _export;

    public readonly CAnimSet UnrealAnim;
    public readonly string Path;
    public readonly string Name;
    public readonly Sequence[] Sequences;
    public readonly float StartTime;                // Animation Start Time
    public readonly float EndTime;                  // Animation End Time
    public readonly float TotalElapsedTime;         // Animation Max Time
    public readonly Dictionary<int, float> Framing;

    public bool IsActive;
    public bool IsSelected;

    public readonly List<FGuid> AttachedModels;

    public Animation(UObject export)
    {
        _export = export;
        Path = _export.GetPathName();
        Name = _export.Name;
        Sequences = [];
        Framing = new Dictionary<int, float>();
        AttachedModels = [];
    }

    public Animation(UObject export, CAnimSet animSet) : this(export)
    {
        UnrealAnim = animSet;

        Sequences = new Sequence[UnrealAnim.Sequences.Count];
        for (int i = 0; i < Sequences.Length; i++)
        {
            Sequences[i] = new Sequence(UnrealAnim.Sequences[i]);
            EndTime = Sequences[i].EndTime;
        }

        TotalElapsedTime = animSet.TotalAnimTime;
        if (Sequences.Length > 0)
            StartTime = Sequences[0].StartTime;
    }

    public Animation(UObject export, CAnimSet animSet, params FGuid[] animatedModels) : this(export, animSet)
    {
        AttachedModels.AddRange(animatedModels);
    }

    public void TimeCalculation(float elapsedTime)
    {
        for (int i = 0; i < Sequences.Length; i++)
        {
            var sequence = Sequences[i];
            if (elapsedTime <= sequence.EndTime && elapsedTime >= sequence.StartTime)
            {
                Framing[i] = (elapsedTime - sequence.StartTime) / sequence.TimePerFrame;
            }
            else Framing.Remove(i);
        }

        if (elapsedTime >= TotalElapsedTime)
            Framing.Clear();
    }

    public void Dispose()
    {
        AttachedModels.Clear();
    }

}
