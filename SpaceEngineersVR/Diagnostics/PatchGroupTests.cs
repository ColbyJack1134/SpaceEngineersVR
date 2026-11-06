#if SEVR_DIAGNOSTICS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using SpaceEngineersVR.Plugin;

namespace SpaceEngineersVR.Diagnostics
{
    // A failed group and its dependents leave nothing patched and the rest stay on; a Core failure removes everything.
    internal static class PatchGroupTests
    {
        internal const string Category="SelfTest";
        private static void Require(bool condition,string reason) { if(!condition) throw new Exception(reason); }
        private static bool Owned(string id) => Harmony.GetAllPatchedMethods().Any(m=>Harmony.GetPatchInfo(m)?.Owners.Contains(id)==true);
        private static bool AnyOwned() => Harmony.GetAllPatchedMethods().Any(m=>Harmony.GetPatchInfo(m)?.Owners.Any(o=>o==Common.Name || o.StartsWith(Common.Name+"."))==true);

        internal static void Run(Action<string> log)
        {
            var assembly=typeof(Main).Assembly;
            var classes=PatchGroups.Classes(assembly);
            var all=PatchGroups.All;
            for(int i=0;i<all.Length;i++)
                foreach(var required in all[i].Requires)
                    Require(Array.IndexOf(all,required)>=0 && Array.IndexOf(all,required)<i,all[i].Name+" is listed before its requirement "+required.Name);
            Require(classes.TryGetValue("",out var core) && core.Count>10 && classes.Count>30,"Patch classes are not spread over the groups");
            Require(!AnyOwned(),"Patch group tests need an unpatched game");
            var lines=new List<string>();
            PatchGroups.Sink=lines.Add;
            try
            {
                PatchGroups.Broken.Add(PatchGroups.CockpitBuilding.Name);
                PatchGroups.Apply(assembly);
                Require(!PatchGroups.CockpitBuilding.Enabled && PatchGroups.CockpitBuilding.Failure.Contains("fault injection"),"Broken group stayed on");
                Require(!Owned(PatchGroups.CockpitBuilding.Harmony.Id),"Broken group left patches behind");
                Require(all.Count(g=>!g.Enabled)==1,"Other groups went off with the broken one: "+string.Join(", ",PatchGroups.Off.Select(g=>g.Name)));
                Require(lines.Any(l=>l.StartsWith("SEVR patch group off: Cockpit building: ")) && lines.Last().StartsWith("SEVR patch groups: "+(all.Length-1)+" on, 1 off: Cockpit building; "),
                    "Log lines missing: "+string.Join(" | ",lines));
                PatchGroups.Remove();
                PatchGroups.Broken.Clear();
                Require(!AnyOwned(),"Remove left patches behind");
                foreach(var line in lines) log("  "+line);

                PatchGroups.Broken.Add(PatchGroups.HudRouting.Name);
                PatchGroups.Apply(assembly);
                var expected=new[] {PatchGroups.HudRouting,PatchGroups.HudMarkers,PatchGroups.CameraFeeds,PatchGroups.CameraDisplays,PatchGroups.BlockInspection};
                Require(PatchGroups.Off.SequenceEqual(expected),"Dependents of HUD routing: "+string.Join(", ",PatchGroups.Off.Select(g=>g.Name)));
                Require(PatchGroups.HudMarkers.Failure=="requires HUD routing" && PatchGroups.CameraDisplays.Failure=="requires Camera feeds","Dependency reasons");
                Require(expected.All(g=>!Owned(g.Harmony.Id)),"A dependent group left patches behind");
                PatchGroups.Remove();
                PatchGroups.Broken.Clear();

                PatchGroups.Broken.Add(PatchGroups.Core.Name);
                bool thrown=false;
                try { PatchGroups.Apply(assembly); }
                catch(InvalidOperationException ex) when(ex.Message.StartsWith("Core patches failed")) { thrown=true; }
                Require(thrown && !PatchGroups.Core.Enabled && !AnyOwned(),"Core failure did not take everything out");
                PatchGroups.Broken.Clear();

                Require(classes[Category].Count==2,"Self-test patch classes are not collected under their category");
                var broken=new PatchGroup("Self-test broken",Category);
                Require(!broken.Apply(new[] {typeof(MissingTargetPatch)}) && !Owned(broken.Harmony.Id),"Missing target left patches behind: "+broken.Failure);
                Require(broken.Failure.Contains(typeof(MissingTargetPatch).FullName),"Failure names the patch class: "+broken.Failure);
                var transpiler=new PatchGroup("Self-test transpiler",Category);
                Require(!transpiler.Apply(new[] {typeof(BulkTranspilerPatch)}) && transpiler.Failure.Contains("pattern not found"),"Transpiler failure reason: "+transpiler.Failure);
                Require(!Owned(transpiler.Harmony.Id) && (Harmony.GetPatchInfo(AccessTools.Method(typeof(PatchGroupTests),nameof(First)))?.Transpilers.Count ?? 0)==0,"Bulk patch kept its first target after the second failed");
                log("PASS patch groups: "+all.Length+" groups in dependency order; an injected failure turns off one group with its patches removed and its log line while the others stay on;"+
                    " HUD routing takes its 4 dependents with it; a core failure removes every patch; a missing target and a transpiler failing on the second bulk target roll back their group");
            }
            finally { PatchGroups.Broken.Clear(); PatchGroups.Sink=null; PatchGroups.Remove(); }
        }

        internal static int First(int value) => value+1;
        internal static int Second(int value) => value+2;

        [HarmonyPatch]
        [HarmonyPatchCategory(Category)]
        private static class MissingTargetPatch
        {
            private static MethodBase TargetMethod() => AccessTools.Method(typeof(PatchGroupTests),"NoSuchMethod");
            private static void Prefix() { }
        }

        [HarmonyPatch]
        [HarmonyPatchCategory(Category)]
        private static class BulkTranspilerPatch
        {
            private static IEnumerable<MethodBase> TargetMethods()
            {
                yield return AccessTools.Method(typeof(PatchGroupTests),nameof(First));
                yield return AccessTools.Method(typeof(PatchGroupTests),nameof(Second));
            }
            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions,MethodBase __originalMethod)
            {
                if(__originalMethod.Name==nameof(Second)) throw new InvalidOperationException("pattern not found");
                return instructions;
            }
        }
    }
}
#endif
