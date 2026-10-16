using System;
using HarmonyLib;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Cube;
using Sandbox.Engine.Physics;
using VRage.Game.Entity;
using SpaceEngineersVR.Plugin;
using VRage.Game;
using VRage.Utils;
using VRageMath;

namespace SpaceEngineersVR.Player
{
    // Build aim dot at the hit point, shown only when aiming needs it: the preview is beyond arm's reach and either the attach
    // face just changed on the same grid (thin edges flicker) or the ray hits nothing. Hidden while the pose is locked.
    internal static class BuildRayCue
    {
        private static readonly AccessTools.FieldRef<MyCubeBuilder,MyCubeBuilderGizmo> gizmo=AccessTools.FieldRefAccess<MyCubeBuilder,MyCubeBuilderGizmo>("m_gizmo");
        internal const double ArmReach=1.2,FlickerHold=.5;
        // A soft round dot sized like the HUD markers (two degrees) reads as a small sphere from any distance and in the diorama.
        private static readonly Vector4 color=new Color(60,220,255).ToVector4();
        private static readonly MyStringId dot=MyStringId.GetOrCompute("WhiteDot");
        private static MyCubeGrid lastGrid;
        private static Vector3I lastDirection;
        private static DateTime unstableUntil,sampled;
        private static float alpha;
        private static bool disabled;
        internal static bool Visible(bool far,bool hit,bool unstable,bool locked) => far && !locked && (unstable || !hit);
        internal static float Fade(float current,bool visible,float dt) => visible ? Math.Min(1,current+dt*8):Math.Max(0,current-dt*8);
        public static void Draw()
        {
            if(disabled) return;
            try
            {
                var now=DateTime.UtcNow;
                float dt=(float)Math.Min(.05,Math.Max(0,(now-sampled).TotalSeconds)); sampled=now;
                var builder=MyCubeBuilder.Static;
                MatrixD pose=MatrixD.Identity;
                bool active=Main.VrActive && builder!=null && builder.IsActivated && builder.CurrentBlockDefinition!=null && PlacementControls.BlockTool &&
                    !Main.MenuOpen && PlacementControls.TryBuilderPose(out pose);
                if(!active) { alpha=0; lastGrid=null; return; }
                var space=gizmo(builder)?.SpaceDefault;
                bool hit=builder.HitInfo.HasValue;
                var grid=hit ? (builder.HitInfo.Value.HkHitInfo.GetHitEntity() as MyEntity)?.GetTopMostParent() as MyCubeGrid : null;
                if(space!=null && grid!=null && grid==lastGrid && space.m_addDir!=lastDirection) unstableUntil=now.AddSeconds(FlickerHold);
                lastGrid=grid; if(space!=null) lastDirection=space.m_addDir;
                Vector3D start=pose.Translation,direction=Vector3D.Normalize(pose.Forward);
                Vector3D end=hit ? builder.HitInfo.Value.Position : space!=null && space.m_worldMatrixAdd.Translation.IsValid() && grid!=null ? space.m_worldMatrixAdd.Translation : start+direction*PlacementControls.RawDistance();
                bool far=Vector3D.Distance(start,end)>ArmReach;
                alpha=Fade(alpha,Visible(far,hit,now<unstableUntil,PlacementControls.HoldingPose),dt);
                if(alpha<=0 || !end.IsValid()) return;
                var head=CameraRig.DeviceWorld(Player.Headset.pose.deviceToAbsolute.matrix);
                if(!MarkerBillboard.TryCreate(end,head,out var board)) return;
                float radius=(float)board.Scale/2;
                // Lifted off the surface by its radius so the sphere sits on the hit instead of sinking into it.
                var centre=end+Vector3D.Normalize(head.Translation-end)*radius;
                var halo=color; halo.W*=alpha*.45f; var core=color; core.W*=alpha;
                MyTransparentGeometry.AddPointBillboard(dot,halo,centre,radius,0);
                MyTransparentGeometry.AddPointBillboard(dot,core,centre,radius*.55f,0);
            }
            catch(Exception ex) { disabled=true; Logger.Warning(ex,"Build ray cue disabled"); }
        }
    }
}
