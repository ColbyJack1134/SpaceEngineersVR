using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using SpaceEngineersVR.Wrappers;

namespace SpaceEngineersVR.Player
{
    internal sealed class RemoteScene : IDisposable
    {
        private readonly List<Action> restore=new List<Action>();
        public static bool Active { get; private set; }
        private static object Read(object owner,string name) => owner is Type type ? AccessTools.Field(type,name)?.GetValue(null) ?? AccessTools.Property(type,name)?.GetValue(null) : CockpitRender.Member(owner,name);
        private static void Write(object owner,string name,object value)
        {
            var type=owner as Type ?? owner.GetType(); var target=owner is Type ? null:owner;
            var field=AccessTools.Field(type,name);
            if(field!=null) field.SetValue(target,value); else AccessTools.Property(type,name).SetValue(target,value);
        }
        private void Set(object owner,string name,object value)
        {
            object saved=Read(owner,name); restore.Add(()=>Write(owner,name,saved)); Write(owner,name,value);
        }
        public RemoteScene()
        {
            try
            {
                var settings=MyRender11.Settings;
                var saved=settings; restore.Add(()=>MyRender11.Settings=saved);
                settings.ShadowCameraFrozen=true; MyRender11.Settings=settings;
                var common=AccessTools.TypeByName("VRageRender.MyCommon");
                var lodding=Read(common,"LoddingSettings");
                var global=Read(lodding,"Global");
                Write(global,"IsUpdateEnabled",false);
                Set(lodding,"Global",global);
                var managers=AccessTools.TypeByName("VRage.Render11.Common.MyManagers");
                var geometry=Read(managers,"GeometryRenderer");
                Set(geometry,"IsLodUpdateEnabled",false);
                Set(geometry,"m_globalLoddingSettings",global);
                var factory=Read(managers,"ModelFactory");
                var changed=AccessTools.Method(factory.GetType(),"OnLoddingSettingChanged");
                restore.Insert(0,()=>changed.Invoke(factory,null)); changed.Invoke(factory,null);
                Active=true;
            }
            catch { Dispose(); throw; }
        }
        public void Dispose()
        {
            Active=false;
            for(int i=restore.Count-1;i>=0;i--) restore[i]();
            restore.Clear();
        }
    }
}
