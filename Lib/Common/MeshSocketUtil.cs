using System;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.SkeletalMesh;
using CUE4Parse.UE4.Assets.Exports.StaticMesh;
using CUE4Parse.UE4.Objects.Core.Math;

namespace FModelHeadless.Lib.Common;

internal static class MeshSocketUtil
{
    public static bool TryGetParentSocketTransform(DefaultFileProvider provider, string parentPath, string socketName, bool verbose, out FTransform transform)
    {
        transform = FTransform.Identity;
        try
        {
            // Prefer skeletal sockets
            var skLoaded = MeshLoadUtil.TryLoadSkeletalMesh(provider, parentPath, verbose, out var skel) ? skel : null;
            if (skLoaded != null && skLoaded.Sockets is { Length: > 0 })
            {
                foreach (var socketRef in skLoaded.Sockets)
                {
                    if (!socketRef.TryLoad(out USkeletalMeshSocket socket) || socket == null) continue;
                    if (string.Equals(socket.SocketName.Text, socketName, StringComparison.OrdinalIgnoreCase))
                    {
                        var rot = socket.RelativeRotation;
                        transform = new FTransform(rot.Quaternion(), socket.RelativeLocation, FVector.OneVector);
                        return true;
                    }
                }
            }

            var stLoaded = MeshLoadUtil.TryLoadStaticMesh(provider, parentPath, verbose, out var stat) ? stat : null;
            if (stLoaded != null && stLoaded.Sockets is { Length: > 0 })
            {
                foreach (var socketRef in stLoaded.Sockets)
                {
                    if (!socketRef.TryLoad(out UStaticMeshSocket socket) || socket == null) continue;
                    if (string.Equals(socket.SocketName.Text, socketName, StringComparison.OrdinalIgnoreCase))
                    {
                        var rot = socket.RelativeRotation;
                        transform = new FTransform(rot.Quaternion(), socket.RelativeLocation, FVector.OneVector);
                        return true;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            if (verbose) Console.Error.WriteLine($"[resolver] Socket transform read failed on '{parentPath}': {ex.Message}");
        }
        return false;
    }

    public static void LogSocketSummary(UObject asset)
    {
        switch (asset)
        {
            case USkeletalMesh skeletal when skeletal.Sockets is { Length: > 0 }:
                Console.WriteLine($"[render] Skeletal mesh sockets ({skeletal.Sockets.Length}):");
                foreach (var socketRef in skeletal.Sockets)
                {
                    if (!socketRef.TryLoad(out USkeletalMeshSocket socket) || socket == null) continue;
                    var name = socket.SocketName.Text;
                    var bone = socket.BoneName.Text;
                    var location = socket.RelativeLocation;
                    Console.WriteLine($"  socket {name} bone={bone} loc=({location.X:F1},{location.Y:F1},{location.Z:F1})");
                }
                break;
            case UStaticMesh staticMesh when staticMesh.Sockets is { Length: > 0 }:
                Console.WriteLine($"[render] Static mesh sockets ({staticMesh.Sockets.Length}):");
                foreach (var socketRef in staticMesh.Sockets)
                {
                    if (!socketRef.TryLoad(out UStaticMeshSocket socket) || socket == null) continue;
                    var name = socket.SocketName.Text;
                    var location = socket.RelativeLocation;
                    Console.WriteLine($"  socket {name} loc=({location.X:F1},{location.Y:F1},{location.Z:F1})");
                }
                break;
        }
    }

    // Loading helpers moved to MeshLoadUtil
}
