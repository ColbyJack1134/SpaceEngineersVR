using System;
using System.IO;
using System.Collections.Generic;
using HarmonyLib;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SpaceEngineersVR.Player;
using SpaceEngineersVR.Patches;
using SpaceEngineersVR.Wrappers;
using VRageMath;

namespace SpaceEngineersVR.Diagnostics
{
    internal static class StereoSceneTests
    {
        internal static void Run(Action<string> log)
        {
            foreach(var projection in new[] {VrMath.Projection(-1,1,-1,1,.03),VrMath.Projection(-1.3f,.7f,-.8f,1.2f,.03)})
            {
                var offset=StereoAmbientOcclusionPatch.ViewOffset((Matrix)projection);
                foreach(double u in new[] {0d,.25,.5,.75,1}) foreach(double v in new[] {0d,.5,1})
                {
                    var position=new Vector3D((2*u/projection.M11+offset.X)*12,(-2*v/projection.M22+offset.Y)*12,-12);
                    var clip=Vector4D.Transform(new Vector4D(position,1),projection);
                    Near(clip.X/clip.W,2*u-1,"AO horizontal reconstruction");
                    Near(clip.Y/clip.W,1-2*v,"AO vertical reconstruction");
                }
            }
            foreach(float near in new[] {.03f,.5f})
            {
                var projection=StereoRenderState.FlareFrustum(new Vector4(-1.2f,.8f,-.9f,1.1f),near);
                foreach(float distance in new[] {100000f,1000000f,2000000f}) foreach(float yaw in new[] {-.01f,0,.01f})
                {
                    var view=Matrix.CreateRotationY(yaw)*Matrix.CreateRotationZ(.4f);
                    var combined=view*projection;
                    var point=new Vector3(distance*.1f,0,distance);
                    var clip=Vector4.Transform(point,combined); clip/=clip.W;
                    var shifted=new Vector3(clip.X*1.1f,clip.Y*1.04f,clip.Z);
                    var back=Vector3.Transform(shifted,Matrix.Invert(combined));
                    Require(back.IsValid(),"Shifted distant flare generated nonfinite geometry");
                    var depth=Vector3.Transform(point,view).Z;
                    Require(Math.Abs(Vector3.Transform(back,view).Z-depth)<distance*.00001,"Flare ghost changed depth at sun scale");
                }
            }
            var active=AccessTools.Field(typeof(StereoRenderState),"<Active>k__BackingField");
            var slope=AccessTools.Field(typeof(StereoRenderState),"<PixelSlopeX>k__BackingField");
            var headField=AccessTools.Field(typeof(WorldMarkers),"renderHead");
            object oldActive=active.GetValue(null),oldSlope=slope.GetValue(null),oldHead=headField.GetValue(null);
            int oldView=StereoRenderState.View;
            try
            {
                active.SetValue(null,true); slope.SetValue(null,1d);
                var head=MatrixD.CreateRotationZ(.4)*MatrixD.CreateTranslation(1e7,2e7,3e7);
                headField.SetValue(null,head);
                foreach(double scale in new[] {1d,100d})
                {
                    var position=head.Translation+head.Forward*3*scale+head.Right*scale;
                    var left=MatrixD.Invert(MatrixD.CreateTranslation(-.032*scale,0,0)*head);
                    var right=MatrixD.Invert(MatrixD.CreateTranslation(.032*scale,0,0)*head);
                    StereoRenderState.View=0;
                    Require(MarkerBillboard.TryCreatePixels(position,100,left,VrMath.Projection(-1.3f,.7f,-1,1,.03),1200,out var a,head.Up,true),"Left marker missing");
                    StereoRenderState.View=1;
                    Require(MarkerBillboard.TryCreatePixels(position,100,right,VrMath.Projection(-.7f,1.3f,-1,1,.03),1200,out var b,head.Up,true),"Right marker missing");
                    Near(Vector3D.Distance(a.Point(.5,.5),b.Point(.5,.5)),0,"Marker world corners disagree");
                    Near(a.Scale,b.Scale,"Marker physical size disagrees");
                    var spriteA=new NativeSprite(); var spriteB=new NativeSprite();
                    Require(a.Project(new RectangleF(-.5f,-.5f,1,1),left,VrMath.Projection(-1,1,-1,1,.03),ref spriteA) &&
                        b.Project(new RectangleF(-.5f,-.5f,1,1),right,VrMath.Projection(-1,1,-1,1,.03),ref spriteB),"Shared marker failed eye projection");
                    Require(spriteA.TopLeft.X/spriteA.TopLeft.W>spriteB.TopLeft.X/spriteB.TopLeft.W,"Shared facing removed stereo parallax");
                }
            }
            finally { active.SetValue(null,oldActive); slope.SetValue(null,oldSlope); headField.SetValue(null,oldHead); StereoRenderState.View=oldView; }
            string shaders=Path.GetFullPath(Path.Combine(Path.GetDirectoryName(typeof(VRageRender.MyRenderProxy).Assembly.Location),"..","Content","Shaders"));
            using(var device=new Device(DriverType.Warp))
            {
                try { StereoParticles.Ensure(device,shaders); StereoSmokeTests.Run(device,shaders,log); }
                finally { StereoParticles.Dispose(); }
            }
            log("PASS stereo scenes: asymmetric AO reconstruction, finite shifted sun-scale flare placement under head rotation, shared marker geometry/size with eye parallax at large coordinates and third-person scale, installed particle vertex shaders");
        }
        private static void Require(bool condition,string message) { if(!condition) throw new Exception(message); }
        internal static void RunNative(BorrowedRtvTexture target,string output,Action<string> log)
        {
            var camera=MyRender11.Environment_Matrices; var savedCamera=camera.Capture();
            try
            {
                var init=AccessTools.Method(AccessTools.TypeByName("VRageRender.MyHBAO"),"InitConstantBuffer");
                var viewport=AccessTools.Field(AccessTools.TypeByName("VRageRender.MyRender11"),"FullResViewport").GetValue(null);
                camera.Projection=(Matrix)VrMath.Projection(-1.3f,.7f,-.8f,1.2f,.03);
                var constants=init.Invoke(null,new[] {viewport});
                var a=(Vector2)AccessTools.Field(init.ReturnType,"UVToViewA").GetValue(constants);
                var b=(Vector2)AccessTools.Field(init.ReturnType,"UVToViewB").GetValue(constants);
                var point=new Vector3D((a.X*.3+b.X)*12,(a.Y*.8+b.Y)*12,-12);
                var clip=Vector4D.Transform(new Vector4D(point,1),(MatrixD)camera.Projection);
                Near(clip.X/clip.W,-.4,"Native AO horizontal reconstruction"); Near(clip.Y/clip.W,-.6,"Native AO vertical reconstruction");
                LineFades(camera);
                RenderMarkers(MyRender11.DeviceInstance,output);
                log("PASS native stereo AO constants, common line-billboard angular fades with custom/remote camera preservation, and production crosshair/locking-ring/lead renders at scales 1 and 100");
            }
            finally { camera.Restore(savedCamera); }
        }
        private static void LineFades(EnvironmentMatrices camera)
        {
            var active=AccessTools.Field(typeof(StereoRenderState),"<Active>k__BackingField");
            var center=AccessTools.Field(typeof(StereoRenderState),"<CenterView>k__BackingField");
            var direction=AccessTools.Method(typeof(StereoBillboardPatch),"Direction");
            object savedActive=active.GetValue(null),savedCenter=center.GetValue(null);
            int savedView=StereoRenderState.View; var savedCamera=camera.CameraPosition;
            try
            {
                active.SetValue(null,true);
                var head=MatrixD.CreateRotationZ(.4)*MatrixD.CreateTranslation(1e7,2e7,3e7);
                center.SetValue(null,MatrixD.Invert(head));
                var board=new VRageRender.MyBillboard {CustomViewProjection=-1};
                foreach(double scale in new[] {1d,100d})
                {
                    var source=head.Translation+head.Forward*.15*scale+head.Right*.02*scale;
                    camera.CameraPosition=head.Translation-head.Right*.032*scale; StereoRenderState.View=0;
                    var left=(Vector3D)direction.Invoke(null,new object[] {camera.CameraPosition-source,board});
                    camera.CameraPosition=head.Translation+head.Right*.032*scale; StereoRenderState.View=1;
                    var raw=camera.CameraPosition-source;
                    var right=(Vector3D)direction.Invoke(null,new object[] {raw,board});
                    Require(Vector3D.Distance(left,right)<1e-7,"Line-billboard angular fade directions disagree");
                    float Fade(Vector3D ray)
                    {
                        float n=1-Math.Abs(Vector3.Dot(VRage.Utils.MyUtils.Normalize(ray),(Vector3)head.Forward));
                        return (1-(float)Math.Pow(1-n,30))*.5f;
                    }
                    Near(Fade(left),Fade(right),"Line-billboard brightness differs between eyes");
                    board.CustomViewProjection=0;
                    Require((Vector3D)direction.Invoke(null,new object[] {raw,board})==raw,"Custom-view line fade changed");
                    board.CustomViewProjection=-1;
                    using(var remote=new RemoteScene())
                    {
                        Require(!StereoRenderState.PhysicalEye,"Remote scene inherited physical-eye facing");
                        Require((Vector3D)direction.Invoke(null,new object[] {raw,board})==raw,"Remote line fade borrowed the headset camera");
                    }
                }
            }
            finally
            {
                active.SetValue(null,savedActive); center.SetValue(null,savedCenter);
                StereoRenderState.View=savedView; camera.CameraPosition=savedCamera;
            }
        }
        internal static void RenderMarkers(Device device,string output)
        {
            var active=AccessTools.Field(typeof(StereoRenderState),"<Active>k__BackingField");
            var slope=AccessTools.Field(typeof(StereoRenderState),"<PixelSlopeX>k__BackingField");
            var headField=AccessTools.Field(typeof(WorldMarkers),"renderHead");
            object oldActive=active.GetValue(null),oldSlope=slope.GetValue(null),oldHead=headField.GetValue(null); int oldView=StereoRenderState.View;
            try
            {
                active.SetValue(null,true); slope.SetValue(null,1d);
                var head=MatrixD.CreateTranslation(1e7,2e7,3e7); headField.SetValue(null,head);
                foreach(double scale in new[] {1d,100d}) for(int eye=0;eye<2;eye++)
                {
                    StereoRenderState.View=eye;
                    var view=MatrixD.Invert(MatrixD.CreateTranslation((eye==0 ? -.032:.032)*scale,0,0)*head);
                    var projection=VrMath.Projection(eye==0 ? -1.3f:-.7f,eye==0 ? .7f:1.3f,-1,1,.03);
                    using(var canvas=new OverlayCanvas("Stereo marker fixture",1600,1200,1,false,device))
                    {
                        canvas.Clear(System.Drawing.Color.FromArgb(12,18,24)); canvas.Upload();
                        var targetPosition=head.Translation+new Vector3D(.7,0,-5)*scale;
                        var ring=NativeSignalProbe.Ring("Enemy",1);
                        var sprites=new List<NativeSprite>();
                        ring.AddNative(sprites,targetPosition,view,projection,1600,Vector3D.Up,true);
                        NativeSprites.Draw(canvas.Texture,sprites);
                        var lead=new NativeLead.View {Position=targetPosition+new Vector3D(.7,.25,0)*scale,Target=targetPosition,
                            CircleSize=ring.Size,Color=ring.LockColor,InRange=true};
                        NativeLead.Draw(canvas.Texture,lead,view,projection,Vector3D.Up,true);
                        var crosshair=ShipCrosshair.Read(new Sandbox.Game.Gui.MyHudCrosshair(),head);
                        crosshair.Position=head.Translation+new Vector3D(-.7,0,-5)*scale;
                        ShipCrosshair.Draw(canvas.Texture,crosshair,head,view,projection,true);
                        SignalTests.WaitIcons();
                        canvas.Clear(System.Drawing.Color.FromArgb(12,18,24)); canvas.Upload();
                        NativeSprites.Draw(canvas.Texture,sprites); NativeLead.Draw(canvas.Texture,lead,view,projection,Vector3D.Up,true);
                        ShipCrosshair.Draw(canvas.Texture,crosshair,head,view,projection,true);
                        UiTests.Save(canvas.Texture,Path.Combine(output,"stereo-markers-"+scale+"-"+eye+".png"));
                    }
                }
            }
            finally { active.SetValue(null,oldActive); slope.SetValue(null,oldSlope); headField.SetValue(null,oldHead); StereoRenderState.View=oldView; }
        }
        private static void Near(double actual,double expected,string message)
        { Require(actual.IsValid() && Math.Abs(actual-expected)<1e-5,message+": "+actual); }
    }
}
