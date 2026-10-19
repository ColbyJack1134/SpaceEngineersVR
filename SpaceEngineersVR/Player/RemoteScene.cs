using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using SpaceEngineersVR.Wrappers;
using VRage.Render.Scene;

namespace SpaceEngineersVR.Player
{
    internal sealed class RemoteScene : IDisposable
    {
        private readonly List<Action> restore=new List<Action>();
        public static bool Active { get; private set; }
        private static readonly Type common=AccessTools.TypeByName("VRageRender.MyCommon"),managers=AccessTools.TypeByName("VRage.Render11.Common.MyManagers");
        private static MethodInfo changed;
        // Resolved without throwing so a renderer change only leaves the feed headless.
        private static readonly Type renderableType=AccessTools.TypeByName("VRage.Render11.Scene.Components.MyRenderableComponent");
        internal static readonly MethodInfo RenderableOf=renderableType==null ? null:AccessTools.Method(typeof(MyActor),nameof(MyActor.GetComponent))?.MakeGenericMethod(renderableType);
        internal static readonly FieldInfo ProxyFlags=AccessTools.Field(AccessTools.TypeByName("VRageRender.MyRenderableProxy"),"Flags");
        internal static readonly long FirstPersonHidden=HiddenFlags();
        private static long HiddenFlags()
        {
            try { return ProxyFlags==null ? 0:Convert.ToInt64(Enum.Parse(ProxyFlags.FieldType,"SkipInMainView, SkipInForward")); }
            catch(ArgumentException) { return 0; }
        }
        private static MemberInfo Find(object owner,string name,out object target)
        {
            if(owner==null) throw new InvalidOperationException("Missing renderer member "+name);
            var type=owner as Type ?? owner.GetType(); target=owner is Type ? null:owner;
            return CockpitRender.Find(type,name) ?? throw new MissingMemberException(type.FullName,name);
        }
        private static object Read(object owner,string name)
        {
            var member=Find(owner,name,out object target);
            return member is FieldInfo field ? field.GetValue(target) : ((PropertyInfo)member).GetValue(target);
        }
        private static void Write(object owner,string name,object value)
        {
            var member=Find(owner,name,out object target);
            if(member is FieldInfo field) field.SetValue(target,value); else ((PropertyInfo)member).SetValue(target,value);
        }
        private void Set(object owner,string name,object value)
        {
            object saved=Read(owner,name); restore.Add(()=>Write(owner,name,saved)); Write(owner,name,value);
        }
        public RemoteScene(RemoteView.View view=null)
        {
            try
            {
                if(!FeedShadows.Use(restore))
                {
                    // Without a feed cascade set, keep the eyes' cascades fitted to the eyes.
                    var settings=MyRender11.Settings;
                    var saved=settings; restore.Add(()=>MyRender11.Settings=saved);
                    settings.ShadowCameraFrozen=true; MyRender11.Settings=settings;
                }
                var lodding=Read(common,"LoddingSettings");
                var global=Read(lodding,"Global");
                Write(global,"IsUpdateEnabled",false);
                Set(lodding,"Global",global);
                var geometry=Read(managers,"GeometryRenderer");
                Set(geometry,"IsLodUpdateEnabled",false);
                Set(geometry,"m_globalLoddingSettings",global);
                var factory=Read(managers,"ModelFactory");
                if(changed==null) changed=AccessTools.Method(factory.GetType(),"OnLoddingSettingChanged");
                restore.Insert(0,()=>changed.Invoke(factory,null)); changed.Invoke(factory,null);
                CockpitRender.RemoteVisibility(restore);
                if(view!=null) ShowFirstPerson(view.Character,view.FirstPersonMaterials,restore);
                Active=true;
            }
            catch { Dispose(); throw; }
        }
        // Proxy flags are read when each pass filters its draws, so this needs no proxy rebuild.
        internal static void ShowFirstPerson(uint id,string[] materials,List<Action> restore)
        {
            if(RenderableOf==null || FirstPersonHidden==0) return;
            var actor=id==uint.MaxValue || materials==null ? null:MyIDTracker<MyActor>.FindByID(id);
            var renderable=actor==null ? null:RenderableOf.Invoke(actor,null);
            if(renderable==null || !(CockpitRender.Member(renderable,"Lods") is IEnumerable lods)) return;
            foreach(object lod in lods)
            {
                if(!(CockpitRender.Member(lod,"RenderableProxies") is IEnumerable proxies)) continue;
                foreach(object proxy in proxies)
                {
                    object material=CockpitRender.Member(proxy,"Material");
                    if(Convert.ToInt32(CockpitRender.Member(material,"Index"))<0 ||
                        Array.IndexOf(materials,CockpitRender.Member(CockpitRender.Member(material,"Info"),"Name").ToString())<0) continue;
                    object saved=ProxyFlags.GetValue(proxy);
                    long value=Convert.ToInt64(saved);
                    if((value&FirstPersonHidden)==0) continue;
                    object shown=Enum.ToObject(ProxyFlags.FieldType,value&~FirstPersonHidden);
                    ProxyFlags.SetValue(proxy,shown);
                    // A rebuild during the feed would return this proxy to the engine's pool.
                    restore.Add(()=> { if(Equals(ProxyFlags.GetValue(proxy),shown) && Equals(CockpitRender.Member(proxy,"Material"),material)) ProxyFlags.SetValue(proxy,saved); });
                }
            }
        }
        public void Dispose()
        {
            Active=false;
            for(int i=restore.Count-1;i>=0;i--) restore[i]();
            restore.Clear();
        }
    }
}
