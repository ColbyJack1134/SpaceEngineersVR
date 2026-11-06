using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;

namespace SpaceEngineersVR.Plugin
{
    // One feature's Harmony patches under their own Harmony id, so a failed apply removes exactly that feature.
    internal sealed class PatchGroup
    {
        internal readonly string Name,Category;
        internal readonly Harmony Harmony;
        private readonly PatchGroup[] requires;
        private readonly Action<Harmony> install;
        private readonly Action off;
        private readonly List<PatchGroup> parts=new List<PatchGroup>();
        internal bool Enabled { get; private set; }
        internal bool Attempted { get; private set; }
        internal string Failure { get; private set; }
        internal Exception Error { get; private set; }
        internal IEnumerable<PatchGroup> Requires => requires;
        // Failed, or missing a requirement: nothing attaches or runs for it.
        internal bool Blocked => Failure!=null || requires.Any(r=>!r.Enabled);

        internal PatchGroup(string name,string category,Action<Harmony> install=null,Action off=null,PatchGroup[] requires=null)
        {
            Name=name; Category=category; this.install=install; this.off=off; this.requires=requires ?? new PatchGroup[0];
            Harmony=new Harmony(category==null ? Common.Name:Common.Name+"."+category);
        }

        // Patch classes of the category, then the install action; any failure leaves nothing of this group patched.
        internal bool Apply(IEnumerable<Type> classes=null,Action<Harmony> attach=null)
        {
            if(Enabled) return true;
            Attempted=true;
            var missing=requires.FirstOrDefault(r=>!r.Enabled);
            if(missing!=null) { Disable("requires "+missing.Name,null); return false; }
            Type current=null;
            try
            {
                if(classes!=null) foreach(var type in classes) { current=type; Harmony.CreateClassProcessor(type).Patch(); }
                current=null;
#if SEVR_DIAGNOSTICS
                if(PatchGroups.Broken.Contains(Name)) throw new InvalidOperationException("fault injection");
#endif
                install?.Invoke(Harmony);
                attach?.Invoke(Harmony);
                Enabled=true; Failure=null; Error=null;
                return true;
            }
            catch(Exception ex)
            {
                Harmony.UnpatchAll(Harmony.Id);
                Disable((current==null ? "":current.FullName+": ")+Describe(ex),ex);
                return false;
            }
        }

        private void Disable(string reason,Exception error)
        {
            Enabled=false; Failure=reason; Error=error;
            PatchGroups.Log("SEVR patch group off: "+Name+": "+reason,error);
            try { off?.Invoke(); }
            catch(Exception ex) { PatchGroups.Log("SEVR patch group cleanup failed: "+Name,ex); }
        }

        // A mod adapter attached later under its own id, so it fails alone. It needs no requirement on its owner,
        // whose Integration stays idle while the owner is off.
        internal PatchGroup Part(string name)
        {
            var part=new PatchGroup(Name+"."+name,(Category ?? "Core")+"."+name+"#"+parts.Count);
            parts.Add(part);
            return part;
        }

        internal void Remove()
        {
            RemoveParts();
            Harmony.UnpatchAll(Harmony.Id);
            Enabled=false; Attempted=false; Failure=null; Error=null;
        }

        // World unload: parts attached in the world go, the group's own patches stay.
        internal void RemoveParts()
        {
            foreach(var part in parts) part.Remove();
            parts.Clear();
        }

        internal void Patch(MethodBase original,MethodInfo prefix=null,MethodInfo postfix=null,MethodInfo transpiler=null,MethodInfo finalizer=null)
        {
            if(original==null) throw new MissingMethodException("patch target missing in "+Name);
            Harmony.Patch(original,Hook(prefix),Hook(postfix),Hook(transpiler),Hook(finalizer));
        }
        private static HarmonyMethod Hook(MethodInfo method) => method==null ? null:new HarmonyMethod(method);

        // Lookups that name the missing member in the log line instead of Harmony's "Null method".
        internal static Type Type(string name) => AccessTools.TypeByName(name) ?? throw new TypeLoadException(name);
        internal static MethodInfo Method(Type type,string name,Type[] parameters=null) =>
            AccessTools.Method(type,name,parameters) ?? throw new MissingMethodException(type?.FullName+"."+name);
        internal static MethodInfo Method(Type type,string name,int parameters,BindingFlags flags=BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public) =>
            type?.GetMethods(flags).FirstOrDefault(m=>m.Name==name && m.GetParameters().Length==parameters) ?? throw new MissingMethodException(type?.FullName+"."+name+"/"+parameters);
        internal static MethodInfo Getter(Type type,string name) =>
            AccessTools.PropertyGetter(type,name) ?? throw new MissingMethodException(type?.FullName+".get_"+name);

        internal static string Describe(Exception error)
        {
            var text=new StringBuilder();
            for(var ex=error;ex!=null;ex=ex.InnerException)
            {
                if(text.Length>0) text.Append(": ");
                text.Append(ex.GetType().Name).Append(": ").Append(ex.Message);
            }
            return text.ToString();
        }
    }
}
