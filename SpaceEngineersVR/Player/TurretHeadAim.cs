using System;
using System.Collections.Generic;
using HarmonyLib;
using Sandbox.Game.Entities;
using Sandbox.Game.Weapons;
using Sandbox.Game.World;
using Sandbox.ModAPI;
using VRage.Game.Entity;
using SpaceEngineersVR.Plugin;
using VRage.Game.ModAPI;
using VRageMath;

namespace SpaceEngineersVR.Player
{
    // Drives the controlled turret toward the head ray through the native mouse path, so its limits, sync and HUD stay vanilla.
    internal static class TurretHeadAim
    {
        private static readonly AccessTools.FieldRef<MyLargeTurretBase,float> rotation=AccessTools.FieldRefAccess<MyLargeTurretBase,float>("m_rotation");
        private static readonly AccessTools.FieldRef<MyLargeTurretBase,float> elevation=AccessTools.FieldRefAccess<MyLargeTurretBase,float>("m_elevation");
        private static readonly AccessTools.FieldRef<MyLargeTurretBase,float> speed=AccessTools.FieldRefAccess<MyLargeTurretBase,float>("m_rotationSpeed");
        private static readonly AccessTools.FieldRef<MyLargeTurretBase,float> targetFov=AccessTools.FieldRefAccess<MyLargeTurretBase,float>("m_targetFov");
        private static readonly AccessTools.FieldRef<MyLargeTurretBase,float> elevationSpeed=AccessTools.FieldRefAccess<MyLargeTurretBase,float>("m_elevationSpeed");
        private static readonly Func<MyLargeTurretBase,float> multiplier=AccessTools.MethodDelegate<Func<MyLargeTurretBase,float>>(AccessTools.Method(typeof(MyLargeTurretBase),"GetRotationMultiplier"));
        private static readonly List<IHitInfo> hits=new List<IHitInfo>();
        internal const float MinRange=100;

        internal static bool Available => Main.VrActive && MySession.Static?.ControlledEntity is MyLargeTurretBase;
        internal static int Mode => Available ? RemoteView.AimMode : 0;
        internal static bool Active => Mode>0;
        internal static bool Face => Mode==2;
        internal static string Label => Mode==2 ? "Turret aim: head + screen" : Mode==1 ? "Turret aim: head" : "Turret aim: normal";

        private static MatrixD Head => (MatrixD)Player.Headset.pose.deviceToAbsolute.matrix*RemoteView.PhysicalTrackingToWorld;
        private static Vector3D Up(bool seat) => seat && RemoteView.HomeSeat!=null ? RemoteView.HomeSeat.WorldMatrix.Up : Head.Up;
        internal static Vector2 Indicator(MyLargeTurretBase turret)
        {
            var head=Head;
            if(!Player.Headset.pose.isTracked || !head.IsValid()) return Vector2.Zero;
            return Indicator(turret,AimPoint(turret,head.Translation,head.Forward));
        }
        // A fixed depth per turret for the face picture, so it does not jump as the head ray crosses grids.
        internal static float FaceDepth(MyLargeTurretBase turret) => MathHelper.Clamp(((Sandbox.ModAPI.Ingame.IMyLargeTurretBase)turret).Range,MinRange,1000);
        // The native mouse step: rotation -= indicator.Y*step, elevation -= indicator.X*step, clamped by the turret afterwards.
        // AI turrets turn at most speed*16 per update; capping the indicator at 20 keeps that rate and the native zoomed slowdown.
        internal const float MaxIndicator=20;
        internal static Vector2 Indicator(MyLargeTurretBase turret,Vector3D point)
        {
            float rate=speed(turret),step=.05f*rate*16*multiplier(turret);
            if(!(step>0)) return Vector2.Zero;
            var target=turret.LookAt(point);
            float pitch=MaxIndicator*elevationSpeed(turret)/rate;
            var indicator=new Vector2(MathHelper.Clamp(-(target.X-elevation(turret))/step,-pitch,pitch),
                MathHelper.Clamp(-MathHelper.WrapAngle(target.Y-rotation(turret))/step,-MaxIndicator,MaxIndicator));
            return indicator.IsValid() ? indicator:Vector2.Zero;
        }
        // On the face screen the view is never wider than the screen, so zoomed out it matches the world 1:1 and zooming in magnifies.
        internal static void LimitZoom(MyLargeTurretBase turret)
        {
            float widest=MathHelper.ToRadians(RemoteFeed.FaceHeightDegrees);
            if(Face && targetFov(turret)>widest) targetFov(turret)=widest;
        }
        // The barrel's own aim, past the own ship like the head ray, for the world crosshair.
        internal static bool TryBarrel(out MyLargeTurretBase turret,out Vector3D point,out Vector3D up)
        {
            point=up=Vector3D.Zero;
            turret=MySession.Static?.ControlledEntity as MyLargeTurretBase;
            if(turret==null || !Active || Face || turret.Closed) return false;
            var muzzle=((IMyGunObject<MyGunBase>)turret).GunBase?.GetMuzzleWorldMatrix();
            if(!muzzle.HasValue || !muzzle.Value.IsValid()) return false;
            point=AimPoint(turret,muzzle.Value.Translation,muzzle.Value.Forward);
            // The barrel spins while firing; the seat's or the head's up keeps the crosshair still.
            up=Up(true);
            return true;
        }
        // Roll about the view axis only, toward the head's up on the face screen and the seat's up in the window; aim is unchanged.
        internal static bool Upright(MyLargeTurretBase turret,MatrixD view,out MatrixD upright)
        {
            upright=view;
            if(!Active || MySession.Static?.ControlledEntity!=turret) return false;
            var up=Up(!Face);
            var world=MatrixD.Invert(view);
            var level=up-world.Forward*Vector3D.Dot(up,world.Forward);
            if(!(level.LengthSquared()>1e-4)) return false;
            upright=MatrixD.Invert(MatrixD.CreateWorld(world.Translation,world.Forward,Vector3D.Normalize(level)));
            return upright.IsValid();
        }
        // The turret converges on what the head ray meets past the own ship, or on its range when nothing is hit.
        internal static Vector3D AimPoint(MyLargeTurretBase turret,Vector3D from,Vector3D direction)
        {
            double range=Math.Max(((Sandbox.ModAPI.Ingame.IMyLargeTurretBase)turret).Range,MinRange);
            var to=from+direction*range;
            hits.Clear();
            MyAPIGateway.Physics?.CastRay(from,to,hits);
            hits.Sort((a,b)=>a.Fraction.CompareTo(b.Fraction));
            foreach(var hit in hits)
                if(!Own((hit.HitEntity as MyEntity)?.GetTopMostParent(),turret)) return hit.Position;
            return to;
        }
        internal static bool Own(MyEntity entity,MyLargeTurretBase turret) => entity==null || entity==MySession.Static?.LocalCharacter ||
            entity is MyCubeGrid grid && MyCubeGridGroups.Static.Physical.HasSameGroup(grid,turret.CubeGrid);
    }
}
