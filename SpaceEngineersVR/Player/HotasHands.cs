using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using VRageMath;

namespace SpaceEngineersVR.Player
{
    // Analog Grid Control's HOTAS axes put a resting hand on the matching cockpit stick; the controller moving takes it back.
    internal static class HotasHands
    {
        private const float MoveRelease=.05f,TurnRelease=.97f,RotationScale=20,StillSeconds=.5f;
        private static bool resolved;
        private static FieldInfo instance;
        private static PropertyInfo active,movement,rotation;
        internal sealed class Hand
        {
            private Matrix anchor;
            private DateTime still;
            internal bool Active { get; private set; }
            internal bool Update(bool available,bool input,Matrix controller,DateTime now)
            {
                if(!available) { anchor=controller; still=now; return Active=false; }
                if(Moved(controller)) { anchor=controller; still=now; Active=false; }
                else if(!Active && input && (now-still).TotalSeconds>=StillSeconds) Active=true;
                return Active;
            }
            private bool Moved(Matrix controller) => Vector3.Distance(anchor.Translation,controller.Translation)>MoveRelease ||
                Math.Abs(Quaternion.Dot(Quaternion.CreateFromRotationMatrix(anchor.GetOrientation()),Quaternion.CreateFromRotationMatrix(controller.GetOrientation())))<TurnRelease;
            internal void Reset() => Active=false;
        }
        // Movement and rotation in the native indicator convention; rotation is normalized to -1..1.
        internal static bool TryRead(out Vector3 move,out Vector3 turn)
        {
            move=turn=Vector3.Zero;
            if(!resolved) Resolve();
            if(instance==null) return false;
            try
            {
                var session=instance.GetValue(null);
                if(session==null || !(bool)active.GetValue(session)) return false;
                move=Vector3.Clamp((Vector3)movement.GetValue(session),-Vector3.One,Vector3.One);
                turn=Vector3.Clamp((Vector3)rotation.GetValue(session)/RotationScale,-Vector3.One,Vector3.One);
                return move.IsValid() && turn.IsValid();
            }
            catch(Exception ex) { instance=null; Plugin.Logger.Warning(ex,"Analog Grid Control HOTAS hands disabled"); return false; }
        }
        private static void Resolve()
        {
            resolved=true; instance=null;
            try
            {
                var type=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("AnanaceDev.AnalogGridControl.AnalogGridControlSession",false)).FirstOrDefault(t=>t!=null);
                if(type==null) return;
                active=AccessTools.Property(type,"IsAnalogInputActive"); movement=AccessTools.Property(type,"MovementVector"); rotation=AccessTools.Property(type,"RotationVector");
                var field=AccessTools.Field(type,"Instance");
                if(active?.PropertyType==typeof(bool) && movement?.PropertyType==typeof(Vector3) && rotation?.PropertyType==typeof(Vector3) && field!=null && field.IsStatic && field.FieldType==type) instance=field;
            }
            catch(Exception ex) { Plugin.Logger.Warning(ex,"Analog Grid Control HOTAS hands disabled"); }
        }
        // Inverse of CockpitStickMath.Translation and Rotation: the stick lean that commands these axes.
        internal static Vector3 LeftAngles(Vector3 move) => new Vector3(-move.Z,move.Y,move.X);
        internal static Vector3 RightAngles(Vector3 turn) => turn;
    }
}
