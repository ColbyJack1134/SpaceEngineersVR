#if SEVR_DIAGNOSTICS
using System;
using SpaceEngineersVR.Player;
using VRageMath;

namespace SpaceEngineersVR.Diagnostics
{
    internal static class GrabTests
    {
        private static void Require(bool value,string message) { if(!value) throw new Exception(message); }
        public static void Run(Action<string> log)
        {
            // Size rule in metres from the mod's setting, distance clamp, snap distance along the ray.
            Require(GridGrab.WithinSize(new Vector3I(0,0,0),new Vector3I(6,6,6),2.5f,20) && !GridGrab.WithinSize(new Vector3I(0,0,0),new Vector3I(7,0,0),2.5f,20) &&
                GridGrab.WithinSize(new Vector3I(-10,0,0),new Vector3I(28,3,3),.5f,20) && !GridGrab.WithinSize(new Vector3I(0,0,0),new Vector3I(4,0,0),2.5f,10),"Size cap does not follow the mod's limit");
            Require(GridGrab.ClampDistance(.2,8)==GridGrab.MinDistance && GridGrab.ClampDistance(30,8)==8 && GridGrab.ClampDistance(3,8)==3 && GridGrab.ClampDistance(9,12)==9,"Distance clamp is wrong");
            Require(Math.Abs(GridGrab.SnapDistance(new Vector3D(0,1,4),Vector3D.Zero,Vector3D.Forward*-1,8)-4)<1e-6,"Snap distance is not the projection on the ray");
            // The mod's pull point is head + forward * distance + offset; the offset lands it on the hand target without the close-range stiffening.
            var head=MatrixD.CreateWorld(new Vector3D(10,2,0),Vector3D.Forward,Vector3D.Up);
            var target=new Vector3D(12,1,-3);
            foreach(double distance in new[] {1.2,1.9,2,5})
            {
                double sent=GridGrab.PacketDistance(distance);
                Require(sent>=GridGrab.StiffDistance && (distance<GridGrab.StiffDistance || sent==distance),"Sent distance drops below the stiffening threshold or changes a long hold");
                var point=head.Translation+head.Forward*sent+GridGrab.Offset(head,sent,target);
                Require(Vector3D.Distance(point,target)<1e-9,"Offset does not place the mod's pull point on the hand target");
            }
            // Rotation axes: each camera axis takes a distinct grid axis with the matching sign; the demands become body torque.
            var axes=GridGrab.ResolveAxes(MatrixD.CreateRotationY(Math.PI),MatrixD.Identity);
            Require(axes[0]==Vector3.Left && axes[1]==Vector3.Down && axes[2]==Vector3.Backward,"Rotation axes do not follow the camera after a grid yaw of 180 degrees");
            var torque=GridGrab.RotationTorque(axes,new Vector3(1,0,0));
            Require(torque.X<0 && Math.Abs(torque.Length()-GridGrab.Torque)<1e-2f,"Pitch demand does not torque about the latched axis");
            // Two-hand turn: turning the fingertip frame 90 degrees about up turns the target by the same rotation; alignment torque points along it.
            var start=GridGrab.TurnFrame(Vector3D.Zero,Vector3D.Right,Vector3D.Up,Vector3D.Up);
            var turned=GridGrab.TurnFrame(Vector3D.Zero,Vector3D.Forward,Vector3D.Up,Vector3D.Up);
            var turnTarget=GridGrab.TurnTarget(start,turned,MatrixD.Identity);
            Require(Vector3D.Distance(turnTarget.Right,Vector3D.Forward)<1e-6 && Vector3D.Distance(turnTarget.Up,Vector3D.Up)<1e-6,"Turn target does not follow the hand frame");
            var align=GridGrab.AlignTorque(MatrixD.Identity,turnTarget,1000);
            Require(align.Y>0 && Math.Abs(Math.Abs(align.Y)-GridGrab.Torque*.5f)<1 && Math.Abs(align.X)<1e-2f && Math.Abs(align.Z)<1e-2f,"Alignment torque is not about the turn axis");
            Require(GridGrab.AlignTorque(MatrixD.Identity,MatrixD.CreateRotationY(MathHelper.ToRadians(2)),10)==Vector3.Zero,"Alignment torque ignores the deadzone");
            // Body contact counter: a quarter second of contact drops, a break recovers after the settle.
            var contact=new GridGrab.Contact();
            int ticks=0; while(!GridGrab.Step(contact,true,true) && ticks<100) ticks++;
            Require(ticks==13,"Contact drop time is not the counter limit over the drain rate");
            contact=new GridGrab.Contact();
            for(int i=0;i<5;i++) GridGrab.Step(contact,true,true);
            int after=contact.Counter;
            for(int i=0;i<GridGrab.ContactSettle;i++) GridGrab.Step(contact,true,false);
            Require(contact.Counter==after,"Counter recovers before the settle has passed");
            GridGrab.Step(contact,true,false);
            Require(contact.Counter==after+GridGrab.ContactRecovery,"Counter does not recover after the settle");
            log("PASS hand grab: size rule, distance clamp and snap, mod pull point offset, rotation axes and torque, two-hand turn, contact counter.");
        }
    }
}
#endif
