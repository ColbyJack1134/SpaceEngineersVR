using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Sandbox.Definitions;
using Sandbox.Game.World;
using SpaceEngineersVR.Player;
using SpaceEngineersVR.Plugin;
using VRage.Game;
using VRage.Utils;
using VRageMath;
using VRageRender;

namespace SpaceEngineersVR.Patches
{
    // Native trails are a few millimetres thick and scaled by the desktop zoom level, which VR never changes.
    [HarmonyPatch]
    internal static class TracerPatch
    {
        internal const float ThicknessScale=3,LengthScale=2;
        internal const double HeadPixels=2.6,FadeStart=70,FadeEnd=400,FarBrightness=.4,FallbackPixel=Math.PI/3600,OwnShot=2,StreakReach=15,StreakStart=.3;
        private static readonly MyStringId dot=MyStringId.GetOrCompute("WhiteDot");
        private static Vector3D muzzle;
        private static float range;
        private static MethodBase TargetMethod() => AccessTools.Method(AccessTools.TypeByName("Sandbox.Game.Weapons.MyProjectile"),"Draw");
        internal static float Thickness(float value) => Main.VrActive ? value*ThicknessScale:value;
        internal static float Length(float value) => Main.VrActive ? value*LengthScale:value;
        // Native skips trails that start within 2 m of the muzzle; in VR the shot should be seen leaving the gun.
        private static double MinimumDistance(double value) => Main.VrActive ? 0:value;
        private static Vector3D Muzzle(Vector3D origin) => muzzle=origin;
        private static void Range(float value) => range=value;
        // Shots end at their randomized range (pistol rounds at 200 to 250 m); the dot dims over the second half of it instead of vanishing.
        internal static float RangeFade(double travelled,float maximum) => maximum>0 ? (float)MathHelper.Clamp((maximum-travelled)/(maximum*.5),0,1):1;

        // The renderer dims a line by 1-|cos|^30 of the angle to the camera, so a trail seen from the shooter's eye or head-on
        // vanishes. The head dot takes the remainder.
        internal static float HeadWeight(Vector3D head,Vector3 direction,Vector3D eye,out double distance)
        {
            var toEye=eye-head;
            distance=toEye.Length();
            if(!(distance>.1)) return 0;
            return (float)Math.Pow(Math.Abs(Vector3D.Dot(toEye/distance,direction)),30);
        }
        // Native trail colour is HDR (x10 to x20); at that level bloom spreads the dot well beyond its quad.
        internal const float HeadBrightness=.18f;
        internal static float Fade(double distance) => (float)MathHelper.Lerp(1,FarBrightness,MathHelper.Clamp((distance-FadeStart)/(FadeEnd-FadeStart),0,1));
        // A point of light stays a few headset pixels wide at any range, like a real tracer.
        internal static float HeadRadius(double distance,float thickness,double pixel) => Math.Max(thickness,(float)(distance*pixel*HeadPixels/2));
        private static double Pixel()
        {
            int width=EyeResolution.Current.X;
            double slope=StereoRenderState.PixelSlopeX;
            return width>0 && slope>0 ? 2*slope/width:FallbackPixel;
        }
        // The renderer fades by the angle to the trail start only, so a young own shot drawn from just past the muzzle is
        // seen at a steep angle and reads as a streak leaving the gun.
        internal static bool Streak(Vector3D muzzle,Vector3D eye,ref Vector3D start,Vector3 direction,ref float length)
        {
            var head=start+direction*length;
            if(Vector3D.DistanceSquared(muzzle,eye)>OwnShot*OwnShot || Vector3D.DistanceSquared(muzzle,head)>StreakReach*StreakReach) return false;
            var near=muzzle+(Vector3D)direction*StreakStart;
            double stretched=Vector3D.Dot(head-near,direction);
            if(!(stretched>length)) return false;
            start=near; length=(float)stretched;
            return true;
        }

        private static void Line(MyStringId material,Vector4 color,Vector3D origin,Vector3 direction,float length,float thickness,
            MyBillboard.BlendTypeEnum blend,int view,float intensity,List<MyBillboard> persistent)
        {
            var camera=MySector.MainCamera;
            bool vr=Main.VrActive && camera!=null;
            var source=color;
            if(vr)
            {
                Streak(muzzle,camera.Position,ref origin,direction,ref length);
                // Billboards blend premultiplied and the renderer dims only the colour of an end-on line, which leaves it
                // opaque and dark. Fading the alpha by the same amount keeps it a glow.
                color.W*=1-HeadWeight(origin,direction,camera.Position,out _);
            }
            MyTransparentGeometry.AddLineBillboard(material,color,origin,direction,length,thickness,blend,view,intensity,persistent);
            if(!vr) return;
            var head=origin+direction*length;
            float weight=HeadWeight(head,direction,camera.Position,out double distance);
            if(weight<.01f) return;
            var glow=source*(HeadBrightness*weight*Fade(distance)*RangeFade(Vector3D.Distance(head,muzzle),range));
            MyTransparentGeometry.AddPointBillboard(dot,glow,head,HeadRadius(distance,thickness,Pixel()),0,view,blend,persistent);
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var list=instructions.ToList();
            var type=AccessTools.TypeByName("Sandbox.Game.Weapons.MyProjectile");
            var scale=AccessTools.Field(typeof(MyProjectileAmmoDefinition),nameof(MyProjectileAmmoDefinition.ProjectileTrailScale));
            var lerp=AccessTools.Method(typeof(MathHelper),nameof(MathHelper.Lerp),new[] {typeof(float),typeof(float),typeof(float)});
            var line=AccessTools.Method(typeof(MyTransparentGeometry),nameof(MyTransparentGeometry.AddLineBillboard),
                new[] {typeof(MyStringId),typeof(Vector4),typeof(Vector3D),typeof(Vector3),typeof(float),typeof(float),typeof(MyBillboard.BlendTypeEnum),typeof(int),typeof(float),typeof(List<MyBillboard>)});
            var minimum=AccessTools.Field(type,"MINIMUM_DISTANCE_SQRD");
            var origin=AccessTools.Field(type,"m_origin");
            var maximum=AccessTools.Field(type,"m_maxTrajectory");
            int length=list.FindIndex(i=>i.LoadsField(scale));
            int width=list.FindIndex(i=>i.Calls(lerp));
            int skip=list.FindIndex(i=>i.LoadsField(minimum));
            var draws=list.Where(i=>i.Calls(line)).ToList();
            if(maximum==null || length<0 || skip<length || width<skip || draws.Count==0 || !list[skip-2].LoadsField(origin))
                throw new InvalidOperationException("Native projectile trail drawing changed");
            foreach(var draw in draws) draw.operand=AccessTools.Method(typeof(TracerPatch),nameof(Line));
            list.Insert(width+1,CodeInstruction.Call(typeof(TracerPatch),nameof(Thickness)));
            list.Insert(skip+1,CodeInstruction.Call(typeof(TracerPatch),nameof(MinimumDistance)));
            list.Insert(skip-1,CodeInstruction.Call(typeof(TracerPatch),nameof(Muzzle)));
            list.Insert(length+1,CodeInstruction.Call(typeof(TracerPatch),nameof(Length)));
            list.InsertRange(0,new[] {new CodeInstruction(OpCodes.Ldarg_0),new CodeInstruction(OpCodes.Ldfld,maximum),CodeInstruction.Call(typeof(TracerPatch),nameof(Range))});
            return list;
        }
    }
}
