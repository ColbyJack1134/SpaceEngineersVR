#if !LINUX
using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using SpaceEngineersVR.Patches;

// Pulsar finds this type by name in the global namespace and runs Patch before the game assemblies load.
public static class Preloader
{
    // Only .NET 10 inlines UpdateClip. On Legacy a preloaded copy would load from Pulsar's folder, where .NET Framework cannot find the game's native DLLs.
    public static IEnumerable<string> TargetDLLs { get; } = Environment.Version.Major>=5 ? new[] {"VRage.Platform.Windows.dll"} : new string[0];

    // UpdateClip starts with: if(FreeField) { ClearClip(); return; }. A missing target leaves the game unchanged.
    public static void Patch(AssemblyDefinition assembly)
    {
        var window=assembly.MainModule.GetType("VRage.Platform.Windows.Forms.MyGameWindow");
        var update=window?.Methods.FirstOrDefault(m => m.Name=="UpdateClip" && !m.IsStatic && !m.HasParameters && m.HasBody);
        var clear=window?.Methods.FirstOrDefault(m => m.Name=="ClearClip" && m.IsStatic && !m.HasParameters);
        if(update==null || clear==null || window.Fields.Any(f => f.Name==CursorClipPatch.FreeField)) return;
        var free=new FieldDefinition(CursorClipPatch.FreeField,FieldAttributes.Public | FieldAttributes.Static,assembly.MainModule.TypeSystem.Boolean);
        window.Fields.Add(free);
        var il=update.Body.GetILProcessor();
        var original=update.Body.Instructions[0];
        il.InsertBefore(original,il.Create(OpCodes.Ldsfld,free));
        il.InsertBefore(original,il.Create(OpCodes.Brfalse,original));
        il.InsertBefore(original,il.Create(OpCodes.Call,clear));
        il.InsertBefore(original,il.Create(OpCodes.Ret));
    }
}
#endif
