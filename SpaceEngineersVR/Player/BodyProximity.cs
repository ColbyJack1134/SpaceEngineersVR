using System;
using Sandbox.Game.Entities;
using Sandbox.Game.World;
using SpaceEngineersVR.Plugin;
using VRageMath;
using VRageRender;

namespace SpaceEngineersVR.Player
{
    internal static class BodyProximity
    {
        internal static uint Actor { get; private set; }=uint.MaxValue;
        private static float fade;
        internal static bool Fading => fade>0;
        internal static float Amount(Vector3 point,Vector3 pelvis,Vector3 chest)
        {
            var segment=chest-pelvis;
            float t=segment.LengthSquared()<1e-6f ? 0:MathHelper.Clamp(Vector3.Dot(point-pelvis,segment)/segment.LengthSquared(),0,1);
            return MathHelper.SmoothStep(0,1,MathHelper.Clamp((.22f-Vector3.Distance(point,pelvis+segment*t))/.06f,0,1));
        }
        public static void Reset()
        {
            if(Actor!=uint.MaxValue && fade>0) MyRenderProxy.UpdateRenderEntity(Actor,null,null,0);
            Actor=uint.MaxValue; fade=0;
        }
        public static void Update()
        {
            var character=MySession.Static?.LocalCharacter;
            NativeHandLayer.Actor=character?.Render.GetRenderObjectID() ?? uint.MaxValue;
            if(!Main.VrActive || character==null || character.IsDead || ThirdPersonView.Active)
            { Reset(); return; }
            var seat=character.Parent as MyCockpit;
            string subtype=seat?.BlockDefinition.Id.SubtypeName;
            bool saddle=SeatFit.Eligible(seat) && (subtype=="SpeederCockpit" || subtype=="SpeederCockpitCompact");
            bool hide=Common.Config.HideFirstPersonBody || saddle;
            bool proximity=character.IsSitting ? Common.Config.BodyProximityFade : Common.Config.OnFootBodyProximityFade;
            if(!hide && !proximity) { Reset(); return; }
            uint id=character.Render.GetRenderObjectID();
            if(id!=Actor) { Reset(); Actor=id; }
            if(id==uint.MaxValue || !Player.Headset.pose.isTracked) return;
            var pelvis=character.AnimationController.FindBone("SE_RigPelvis",out _);
            var chest=character.AnimationController.FindBone("SE_RigRibcage",out _);
            if(pelvis==null || chest==null) { Reset(); return; }
            var head=SpatialUi.DeviceWorld(Player.Headset.pose.deviceToAbsolute.matrix).Translation;
            var local=(Vector3)Vector3D.Transform(head,character.PositionComp.WorldMatrixInvScaled);
            float target=hide ? 1 : Amount(local,pelvis.AbsoluteTransform.Translation,chest.AbsoluteTransform.Translation);
            float next=saddle ? 1 : MathHelper.Lerp(fade,target,.25f);
            if(Math.Abs(next-target)<.005f) next=target;
            if(Math.Abs(next-fade)<.001f) return;
            fade=next;
            MyRenderProxy.UpdateRenderEntity(id,null,null,fade);
        }
    }
}
