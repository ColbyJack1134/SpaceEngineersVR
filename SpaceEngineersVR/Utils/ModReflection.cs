using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using SpaceEngineersVR.Player;
using SpaceEngineersVR.Plugin;

namespace SpaceEngineersVR.Util
{
    // Name-based access to optional mods' runtime objects. A missing member or failed read returns null and is logged once,
    // so a mod update that renames something shows in the log instead of silently disabling a feature.
    internal static class ModReflection
    {
        private static readonly HashSet<(Type,string)> reported=new HashSet<(Type,string)>();

        internal static object Get(object obj,string name)
        {
            if(obj==null) return null;
            var type=obj.GetType();
            try
            {
                switch(CockpitRender.Find(type,name))
                {
                    case FieldInfo field: return field.GetValue(obj);
                    case PropertyInfo property: return property.GetValue(obj);
                }
                Report(type,name,null);
            }
            catch(Exception ex) { Report(type,name,ex); }
            return null;
        }

        internal static object Path(object obj,params string[] names)
        {
            foreach(string name in names) { obj=Get(obj,name); if(obj==null) return null; }
            return obj;
        }

        internal static T Value<T>(object obj,T fallback,params string[] names) => Path(obj,names) is T value ? value : fallback;

        // Calls an instance method by name and signature; false when it is missing or throws.
        internal static bool Call(object obj,string name,Type[] parameters,params object[] arguments)
        {
            if(obj==null) return false;
            var type=obj.GetType();
            try
            {
                var method=AccessTools.Method(type,name,parameters);
                if(method==null) { Report(type,name,null); return false; }
                method.Invoke(obj,arguments);
                return true;
            }
            catch(Exception ex) { Report(type,name,ex); return false; }
        }

        // Mods are recompiled into a new assembly on every world load and the old assemblies stay loaded, so a lookup by
        // name alone can return a previous world's dead copy. Returns the newest copy whose static instance is set.
        internal static Type LiveType(string fullName,string instance)
        {
            foreach(var assembly in Util.NewestAssemblies())
            {
                var type=assembly.GetType(fullName,false);
                if(type!=null && StaticReader(type,instance)?.Invoke()!=null) return type;
            }
            return null;
        }

        // Getter for a static field or property; null when neither exists.
        internal static Func<object> StaticReader(Type type,string name)
        {
            var field=AccessTools.Field(type,name);
            if(field!=null && field.IsStatic) return ()=>field.GetValue(null);
            var getter=AccessTools.Property(type,name)?.GetGetMethod(true);
            return getter!=null && getter.IsStatic ? ()=>getter.Invoke(null,null) : (Func<object>)null;
        }

        private static void Report(Type type,string name,Exception error)
        {
            lock(reported) if(!reported.Add((type,name))) return;
            if(error==null) Logger.Info("Optional mod member not found: "+type.FullName+"."+name);
            else Logger.Warning(error,"Optional mod member failed: "+type.FullName+"."+name);
        }
    }
}
