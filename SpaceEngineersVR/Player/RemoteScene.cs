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
        private static readonly Type common=AccessTools.TypeByName("VRageRender.MyCommon"),managers=AccessTools.TypeByName("VRage.Render11.Common.MyManagers");
        private static MethodInfo changed;
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
        public RemoteScene()
        {
            try
            {
                var settings=MyRender11.Settings;
                var saved=settings; restore.Add(()=>MyRender11.Settings=saved);
                settings.ShadowCameraFrozen=true; MyRender11.Settings=settings;
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
