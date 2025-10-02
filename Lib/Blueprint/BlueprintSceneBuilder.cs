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
using CUE4Parse.UE4.Assets.Exports.Material;
using CUE4Parse.UE4.Objects.Engine;
using CUE4Parse.UE4.Objects.UObject;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Assets;

namespace FModelHeadless.Lib.Blueprint;

public static class BlueprintSceneBuilder
{
    public record BlueprintComponent(string Name, UObject Asset, FTransform Transform, IReadOnlyList<UMaterialInterface>? Overrides, IReadOnlyList<string>? Tags);

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
        // Include extra components coming from ICH and TemplateComponent exports
        components.AddRange(BuildExtras(provider, blueprintClass, verbose));
        return components;
    }

    public static IReadOnlyList<BlueprintComponent> BuildExtras(DefaultFileProvider provider, UBlueprintGeneratedClass blueprintClass, bool verbose)
    {
        var list = new List<BlueprintComponent>();

        // 1) InheritableComponentHandler Records
        try
        {
            if (blueprintClass.TryGetValue(out FPackageIndex ichIdx, "InheritableComponentHandler") &&
                ichIdx.TryLoad(out UObject ich) &&
                ich.TryGetValue(out IPropertyHolder[] records, "Records") &&
                records is { Length: > 0 })
            {
                foreach (var record in records)
                {
                    if (!record.TryGetValue(out FPackageIndex compIdx, "ComponentTemplate") || compIdx.IsNull)
                        continue;
                    var comp = compIdx.Load<UObject>();
                    if (comp == null) continue;

                    var world = ExtractRelativeTransform(comp);
                    switch (comp)
                    {
                        case UStaticMeshComponent smc:
                            if (verbose) Console.WriteLine($"[blueprint][ich] static '{smc.Name}'");
                            AddStaticMesh(smc, comp.Name, world, list, verbose);
                            break;
                        case USkeletalMeshComponent skc:
                            if (verbose) Console.WriteLine($"[blueprint][ich] skeletal '{skc.Name}'");
                            AddSkeletalMesh(provider, skc, comp.Name, world, list, verbose);
                            break;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            if (verbose) Console.Error.WriteLine($"[blueprint] ICH parse failed: {ex.Message}");
        }

        // 2) TemplateComponent exports with TemplateActor
        try
        {
            var classPath = blueprintClass.GetPathName();
            var norm = BlueprintResolver.NormalizeObjectPath(classPath);
            var dot = norm.LastIndexOf('.');
            var packagePath = dot > 0 ? norm[..dot] : norm;

            if (provider.TryLoadPackage(packagePath, out var pkg) && pkg != null)
            {
                foreach (var lazy in pkg.ExportsLazy)
                {
                    var exp = lazy?.Value; if (exp == null) continue;
                    if (!string.Equals(exp.ExportType, "TemplateComponent", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var compWorld = ExtractRelativeTransform(exp);
                    if (!exp.TryGetValue(out FPackageIndex childIdx, "TemplateActor") || childIdx.IsNull)
                        continue;
                    if (!childIdx.TryLoad(out UBlueprintGeneratedClass childBp))
                        continue;

                    if (verbose) Console.WriteLine($"[blueprint][template] {exp.Name} -> {childBp.Name}");
                    var childComps = Build(provider, childBp, verbose);
                    foreach (var c in childComps)
                    {
                        var combined = c.Transform * compWorld;
                        if (verbose)
                        {
                            try
                            {
                                var ap = c.Asset.GetPathName();
                                Console.WriteLine($"  [blueprint][template] + {ap}");
                            }
                            catch { }
                        }
                        list.Add(new BlueprintComponent($"{exp.Name}/{c.Name}", c.Asset, combined, c.Overrides, c.Tags));
                    }
                }
            }
        }
        catch (Exception ex)
        {
            if (verbose) Console.Error.WriteLine($"[blueprint] TemplateComponent parse failed: {ex.Message}");
        }

        return list;
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
            case UChildActorComponent childActor:
                AddChildActor(provider, childActor, GetNodeName(node), worldTransform, components, verbose);
                break;
        }

        foreach (var child in node.GetChildNodes())
        {
            TraverseNode(provider, child, worldTransform, components, verbose);
        }
    }

    private static void AddChildActor(DefaultFileProvider provider, UChildActorComponent component, string name, FTransform worldTransform, List<BlueprintComponent> components, bool verbose)
    {
        try
        {
            // Prefer explicit class
            if (component.TryGetValue(out FPackageIndex classIdx, "ChildActorClass") && !classIdx.IsNull)
            {
                if (classIdx.Load<UBlueprintGeneratedClass>() is { } childBlueprint)
                {
                    var childComps = Build(provider, childBlueprint, verbose);
                    foreach (var c in childComps)
                    {
                        var combined = c.Transform * worldTransform;
                        components.Add(new BlueprintComponent($"{name}/{c.Name}", c.Asset, combined, c.Overrides, c.Tags));
                    }
                    return;
                }
            }

            // Fallback to template: walk its root component like general traversal
            if (component.TryGetValue(out FPackageIndex tmplIdx, "ChildActorTemplate") && !tmplIdx.IsNull)
            {
                if (tmplIdx.Load<UObject>() is { } tmpl)
                {
                    // Try common mesh-bearing components on the template
                    if (tmpl.TryGetValue(out FPackageIndex rootIdx, "RootComponent") && !rootIdx.IsNull)
                    {
                        if (rootIdx.Load<UObject>() is { } rootObj)
                        {
                            // Reuse logic: wrap the root component in a fake SCS node sequence by checking its type
                            switch (rootObj)
                            {
                                case UStaticMeshComponent smc:
                                    AddStaticMesh(smc, name, worldTransform, components, verbose);
                                    break;
                                case USkeletalMeshComponent skc:
                                    AddSkeletalMesh(provider, skc, name, worldTransform, components, verbose);
                                    break;
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            if (verbose)
                Console.Error.WriteLine($"[blueprint] child actor '{name}' traversal failed: {ex.Message}");
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
        var overrides = LoadOverrides(component, verbose);
        var tags = LoadTags(component);
        components.Add(new BlueprintComponent(name, mesh, worldTransform, overrides, tags));
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
            var overridesEmpty = LoadOverrides(component, verbose);
            var tagsEmpty = LoadTags(component);
            components.Add(new BlueprintComponent(name, mesh, worldTransform, overridesEmpty, tagsEmpty));
            return;
        }

        for (var i = 0; i < instances.Length; i++)
        {
            var instanceTransform = instances[i].TransformData;
            NormalizeTransform(ref instanceTransform);
            var combined = instanceTransform * worldTransform;
            NormalizeTransform(ref combined);
            var overrides = LoadOverrides(component, verbose);
            var tags = LoadTags(component);
            components.Add(new BlueprintComponent($"{name}[{i}]", mesh, combined, overrides, tags));
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
        var overrides = LoadOverrides(component, verbose);
        var tags = LoadTags(component);
        components.Add(new BlueprintComponent(name, mesh, worldTransform, overrides, tags));
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

    private static IReadOnlyList<UMaterialInterface>? LoadOverrides(UObject component, bool verbose)
    {
        try
        {
            if (component.TryGetValue(out FPackageIndex[] materialIdxs, "OverrideMaterials") && materialIdxs is { Length: > 0 })
            {
                var list = new List<UMaterialInterface>(materialIdxs.Length);
                foreach (var idx in materialIdxs)
                {
                    if (idx.IsNull) { list.Add(null); continue; }
                    if (idx.Load<UMaterialInterface>() is { } mat)
                        list.Add(mat);
                    else list.Add(null);
                }
                return list;
            }

            // Fallback to 'Materials' array which sometimes holds the active MIs
            if (component.TryGetValue(out FPackageIndex[] materials, "Materials") && materials is { Length: > 0 })
            {
                var list = new List<UMaterialInterface>(materials.Length);
                foreach (var idx in materials)
                {
                    if (idx.IsNull) { list.Add(null); continue; }
                    if (idx.Load<UMaterialInterface>() is { } mat)
                        list.Add(mat);
                    else list.Add(null);
                }
                return list;
            }
        }
        catch (Exception ex)
        {
            if (verbose)
                Console.Error.WriteLine($"[blueprint] failed to read OverrideMaterials: {ex.Message}");
        }
        return null;
    }

    private static IReadOnlyList<string>? LoadTags(UObject component)
    {
        try
        {
            if (component.TryGetValue(out string[] tags, "ComponentTags") && tags is { Length: > 0 })
                return tags;
        }
        catch { }
        return null;
    }

    private static FTransform ExtractRelativeTransform(UObject obj)
    {
        try
        {
            var loc = obj.GetOrDefault("RelativeLocation", FVector.ZeroVector);
            var rot = obj.GetOrDefault("RelativeRotation", FRotator.ZeroRotator);
            var sca = obj.GetOrDefault("RelativeScale3D", FVector.OneVector);
            return new FTransform(rot.Quaternion(), loc, sca);
        }
        catch
        {
            return FTransform.Identity;
        }
    }
}
