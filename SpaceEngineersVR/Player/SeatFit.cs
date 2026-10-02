using System;
using System.Linq;
using Sandbox.Game.Entities;
using Sandbox.Game.World;
using SpaceEngineersVR.Config;
using SpaceEngineersVR.Plugin;
using VRageMath;

namespace SpaceEngineersVR.Player
{
    internal static class SeatFit
    {
        public static MyCockpit Seat { get; private set; }
        public static Vector3 Offset { get; private set; }
        private static DateTime lastUpdate;
        private static double elapsed;
        public static bool Eligible(MyCockpit seat) => Main.VrActive && seat!=null && !seat.Closed && !seat.MarkedForClose &&
            seat.Pilot!=null && seat.Pilot==MySession.Static?.LocalCharacter && !seat.Pilot.IsDead &&
            ((MySession.Static.ControlledEntity==seat && MySession.Static.CameraController==seat) || RemoteView.UsesSeat(seat)) &&
            (seat.IsInFirstPersonView || seat.ForceFirstPersonCamera);
        internal static Vector3 Limit(Vector3 value) => value.IsValid() ? Vector3.Clamp(value,new Vector3(-0.35f,-0.45f,-0.45f),new Vector3(0.35f,0.3f,0.45f)) : Vector3.Zero;
        internal static Vector3 Step(Vector3 value,Vector3 direction,double seconds) => Limit(value+direction*(float)Math.Min(0.05,Math.Max(0,seconds))*0.12f);
        public static void Update()
        {
            var next=RemoteView.HomeSeat ?? MySession.Static?.ControlledEntity as MyCockpit;
            if (!Eligible(next)) next=null;
            if (Seat!=next)
            {
                Seat=next; Offset=Vector3.Zero;
                Player.SeatOrigin(Seat!=null);
                var saved=Seat==null ? null : Common.Config.SeatFits?.FirstOrDefault(s=>s.Subtype==Seat.BlockDefinition.Id.SubtypeName);
                if (saved!=null) Offset=Limit(new Vector3(saved.X,saved.Y,saved.Z));
            }
            var now=DateTime.UtcNow;
            elapsed=Math.Min(.05,Math.Max(0,(now-lastUpdate).TotalSeconds)); lastUpdate=now;
        }
        public static void Move(Vector3 direction,bool reset=false)
        {
            if (Seat==null || !InputRouter.Gameplay || Main.MenuOpen) return;
            // Called once per main update. Use simulation time, capped after a hitch.
            float step=(float)elapsed*.12f;
            Vector3 value=reset ? (Offset.Length()<=step ? Vector3.Zero : Offset-Vector3.Normalize(Offset)*step) : Step(Offset,direction,elapsed);
            if (value==Offset) return;
            Offset=value;
            string subtype=Seat.BlockDefinition.Id.SubtypeName;
            var others=(Common.Config.SeatFits ?? new SeatFitSetting[0]).Where(s=>s.Subtype!=subtype);
            Common.Config.SeatFits=others.Concat(new[] { new SeatFitSetting { Subtype=subtype,X=value.X,Y=value.Y,Z=value.Z } }).ToArray();
        }
        public static MatrixD UnadjustedHead()
        {
            MatrixD result=Seat.GetHeadMatrix(false,false);
            result.Translation-=Vector3D.TransformNormal(Offset,Seat.WorldMatrix);
            return result;
        }
        public static void Reset() { Seat=null; Offset=Vector3.Zero; }
    }
}
