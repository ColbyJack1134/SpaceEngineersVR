using System;
using System.Linq;
using System.Reflection;
using Sandbox.Game.World;
using SpaceEngineersVR.Plugin;
using VRageMath;

namespace SpaceEngineersVR.Player
{
    // Sends the mod's own move requests through its client network object; worlds without the mod have no grab.
    internal static class ModGrabBridge
    {
        private const string Prefix="mz_00956.ImprovisedEngineering.";
        private static MySession session;
        private static object networking;
        private static MethodInfo send;
        private static ConstructorInfo packet;
        private static PropertyInfo maxSize,reach,lockUse;
        private static bool missing;
        internal static bool Available => Bind();
        internal static double MaxSize => Read(maxSize,20);
        internal static double Reach => Read(reach,8);
        private static double Read(PropertyInfo property,double fallback)
        {
            try { return Bind() ? Convert.ToDouble(property.GetValue(null)):fallback; }
            catch { return fallback; }
        }
        private static bool Bind()
        {
            var current=MySession.Static;
            if(session!=current) { session=current; networking=null; missing=false; }
            if(networking!=null) return true;
            if(missing || current==null) return false;
            try
            {
                var handler=current.GetComponentByTypeName(Prefix+"ClientGridHandler");
                if(handler==null) { missing=true; Logger.Info("Grab: the Improvised Engineering mod is not loaded in this world; the hand grab is off"); return false; }
                var type=handler.GetType();
                var network=type.GetField("Networking")?.GetValue(handler);
                var move=type.Assembly.GetType(Prefix+"GridMovePacket");
                var config=type.Assembly.GetType(Prefix+"Config");
                send=network?.GetType().GetMethod("SendToServer");
                packet=move?.GetConstructors().FirstOrDefault(c=>c.GetParameters().Length==14);
                maxSize=config?.GetProperty("MaxSize"); reach=config?.GetProperty("Reach"); lockUse=config?.GetProperty("LockUse");
                if(network==null || send==null || packet==null || maxSize==null || reach==null || lockUse==null)
                { missing=true; Logger.Warning("Grab: the Improvised Engineering mod's grab interface is not the expected one; the hand grab is off"); return false; }
                networking=network;
                return true;
            }
            catch(Exception ex) { missing=true; Logger.Warning(ex,"Grab: the Improvised Engineering mod could not be bound; the hand grab is off"); return false; }
        }
        // One move request, as the mod's own client sends every update while holding.
        internal static bool Send(long gridId,Vector3 hitPoint,double distance,bool grabCenter,Vector3 torque,bool rotating,bool holding,long groundId,Vector3D offset,float spinDamping,bool lookingAtGrid)
        {
            if(!Bind()) return false;
            try
            {
                bool lock_=(bool)lockUse.GetValue(null);
                var request=packet.Invoke(new object[] {gridId,(Vector3D)hitPoint,distance,grabCenter,(Vector3D)torque,rotating,holding,MySession.Static.LocalPlayerId,groundId,offset,spinDamping,lock_,false,lookingAtGrid});
                send.Invoke(networking,new[] {request});
                return true;
            }
            catch(Exception ex) { missing=true; networking=null; Logger.Warning(ex,"Grab: the Improvised Engineering mod rejected a request; the hand grab is off"); return false; }
        }
    }
}
