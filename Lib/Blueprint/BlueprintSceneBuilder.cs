using System;
using System.Collections.Generic;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.Component;
using CUE4Parse.UE4.Assets.Exports.Component.SkeletalMesh;
using CUE4Parse.UE4.Assets.Exports.Component.StaticMesh;
using CUE4Parse.UE4.Assets.Exports.Engine;
using CUE4Parse.UE4.Objects.Core.Math;
using CUE4Parse.UE4.Assets.Exports.SkeletalMesh;
using CUE4Parse.UE4.Assets.Exports.StaticMesh;
using CUE4Parse.UE4.Objects.Engine;

namespace FModelHeadless.Lib.Blueprint;

public static class BlueprintSceneBuilder
{
    public record BlueprintComponent(string Name, UObject Asset, FTransform Transform);

    public static IReadOnlyList<BlueprintComponent> Build(DefaultFileProvider provider, UBlueprintGeneratedClass blueprintClass, bool verbose = false)
    {
        var components = new List<BlueprintComponent>();
        var scs = LoadSimpleConstructionScript(blueprintClass, verbose);
        if (scs != null)
        {
            foreach (var root in scs.GetRootNodes())
            {
                TraverseNode(provider, root, FTransform.Identity, components, verbose);
            }
        }
        else if (verbose)
        {
            Console.Error.WriteLine($"[blueprint] {blueprintClass.Name}: no SimpleConstructionScript found.");
        }
        return components;
    }

    private static USimpleConstructionScript? LoadSimpleConstructionScript(UBlueprintGeneratedClass blueprintClass, bool verbose)
    {
        try
        {
            var lazy = blueprintClass.GetOrDefaultLazy<USimpleConstructionScript>("SimpleConstructionScript");
            return lazy?.Value;
        }
        catch (Exception ex)
        {
            if (verbose)
                Console.Error.WriteLine($"[blueprint] {blueprintClass.Name}: failed to load SimpleConstructionScript: {ex.Message}");
            return null;
        }
    }

    private static void TraverseNode(DefaultFileProvider provider, USCS_Node node, FTransform parentWorld, List<BlueprintComponent> components, bool verbose)
    {
        var template = node.GetComponentTemplate();
        var worldTransform = parentWorld;
        if (template != null)
        {
            try
            {
                var local = template.GetRelativeTransform();
                NormalizeTransform(ref local);
                NormalizeTransform(ref worldTransform);
                worldTransform = local * worldTransform;
                NormalizeTransform(ref worldTransform);
            }
            catch (Exception ex)
            {
                if (verbose)
                    Console.Error.WriteLine($"[blueprint] node {node.Name}: transform error: {ex.Message}");
            }
        }

        switch (template)
        {
            case UInstancedStaticMeshComponent instanced:
                AddInstancedStaticMesh(instanced, GetNodeName(node), worldTransform, components, verbose);
                break;
            case UStaticMeshComponent staticComponent:
                AddStaticMesh(staticComponent, GetNodeName(node), worldTransform, components, verbose);
                break;
            case USkeletalMeshComponent skeletalComponent:
                AddSkeletalMesh(provider, skeletalComponent, GetNodeName(node), worldTransform, components, verbose);
                break;
        }

        foreach (var child in node.GetChildNodes())
        {
            TraverseNode(provider, child, worldTransform, components, verbose);
        }
    }

    private static void AddStaticMesh(UStaticMeshComponent component, string name, FTransform worldTransform, List<BlueprintComponent> components, bool verbose)
    {
        var mesh = component.GetLoadedStaticMesh();
        if (mesh == null)
        {
            if (verbose)
                Console.Error.WriteLine($"[blueprint] static mesh '{name}' missing mesh asset.");
            return;
        }

        NormalizeTransform(ref worldTransform);
        components.Add(new BlueprintComponent(name, mesh, worldTransform));
    }

    private static void AddInstancedStaticMesh(UInstancedStaticMeshComponent component, string name, FTransform worldTransform, List<BlueprintComponent> components, bool verbose)
    {
        var mesh = component.GetLoadedStaticMesh();
        if (mesh == null)
        {
            if (verbose)
                Console.Error.WriteLine($"[blueprint] instanced mesh '{name}' missing mesh asset.");
            return;
        }

        NormalizeTransform(ref worldTransform);
        var instances = component.GetInstances();
        if (instances.Length == 0)
        {
            components.Add(new BlueprintComponent(name, mesh, worldTransform));
            return;
        }

        for (var i = 0; i < instances.Length; i++)
        {
            var instanceTransform = instances[i].TransformData;
            NormalizeTransform(ref instanceTransform);
            var combined = instanceTransform * worldTransform;
            NormalizeTransform(ref combined);
            components.Add(new BlueprintComponent($"{name}[{i}]", mesh, combined));
        }
    }

    private static void AddSkeletalMesh(DefaultFileProvider provider, USkeletalMeshComponent component, string name, FTransform worldTransform, List<BlueprintComponent> components, bool verbose)
    {
        var meshIndex = component.GetSkeletalMesh();
        if (meshIndex.IsNull)
        {
            if (verbose)
                Console.Error.WriteLine($"[blueprint] skeletal mesh '{name}' missing mesh index.");
            return;
        }

        var mesh = meshIndex.Load<USkeletalMesh>();
        if (mesh == null)
        {
            if (verbose)
                Console.Error.WriteLine($"[blueprint] skeletal mesh '{name}' failed to load.");
            return;
        }

        NormalizeTransform(ref worldTransform);
        components.Add(new BlueprintComponent(name, mesh, worldTransform));
    }

    private static string GetNodeName(USCS_Node node)
    {
        var internalName = node.GetOrDefault<string>("InternalVariableName");
        if (!string.IsNullOrEmpty(internalName))
            return internalName;

        var variableName = node.GetOrDefault<string>("VariableName");
        if (!string.IsNullOrEmpty(variableName))
            return variableName;

        return node.Name;
    }

    private static void NormalizeTransform(ref FTransform transform)
    {
        if (!transform.Rotation.IsNormalized)
            transform.Rotation.Normalize();
    }
}
