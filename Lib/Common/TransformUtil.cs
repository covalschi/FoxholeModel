using System;
using CUE4Parse.UE4.Assets.Exports.Component;
using CUE4Parse.UE4.Objects.Core.Math;

namespace FModelHeadless.Lib.Common;

internal static class TransformUtil
{
    public static void Normalize(ref FTransform transform)
    {
        if (!transform.Rotation.IsNormalized)
            transform.Rotation.Normalize();
    }

    // Compose parent -> child (apply parent first, then child)
    public static FTransform Combine(FTransform parent, FTransform child)
    {
        var translation = parent.Translation + child.Translation; // simple additive (we ignore rotation effect for now to match upstream behavior)
        var rotation = parent.Rotation * child.Rotation;
        var scale = child.Scale3D; // keep child's scale (engine scale rarely changes in our targets)
        return new FTransform(rotation, translation, scale);
    }

    public static FTransform ExtractRelativeTransform(object obj)
    {
        try
        {
            if (obj is USceneComponent sc)
            {
                var t = sc.GetRelativeTransform();
                Normalize(ref t);
                return t;
            }
        }
        catch { }

        try
        {
            if (obj is CUE4Parse.UE4.Assets.Exports.UObject uo)
            {
                var loc = uo.GetOrDefault("RelativeLocation", FVector.ZeroVector);
                var rot = uo.GetOrDefault("RelativeRotation", FRotator.ZeroRotator);
                var sca = uo.GetOrDefault("RelativeScale3D", FVector.OneVector);
                var t = new FTransform(rot.Quaternion(), loc, sca);
                Normalize(ref t);
                return t;
            }
        }
        catch { }
        return FTransform.Identity;
    }

    public static FTransform TryGetRelativeTransformFallback(CUE4Parse.UE4.Assets.Exports.UObject obj)
    {
        return ExtractRelativeTransform(obj);
    }
}
