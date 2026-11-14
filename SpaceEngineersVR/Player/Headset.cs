using System;
using System.Reflection;
using HarmonyLib;
using SharpDX.DXGI;
using SpaceEngineersVR.Plugin;
using SpaceEngineersVR.Util;
using SpaceEngineersVR.Wrappers;
using Valve.VR;
using VRageMath;

namespace SpaceEngineersVR.Player
{
    public class Headset : TrackedDevice
    {
        private BorrowedRtvTexture leftTexture, rightTexture;
        private VRTextureBounds_t bounds = new VRTextureBounds_t { uMax = 1, vMax = 1 };
        private int submittedFrames;
        private bool nativeGizmosUnavailable;
        private bool recenterPending = true;
        public bool MirroredDesktop { get; private set; }

        public Headset() : base(actionName: "")
        {
            deviceId = OpenVR.k_unTrackedDeviceIndex_Hmd;
            uint width = 0, height = 0;
            OpenVR.System.GetRecommendedRenderTargetSize(ref width, ref height);
            EyeResolution.Recommend(width,height);
            Logger.Info($"SteamVR recommends {width}x{height} per eye; headset targets are independent of the desktop.");
        }

        public bool RenderUpdate()
        {
            MirroredDesktop=false;
            if (!MyRender11.m_DrawScene || !renderPose.isTracked) return false;
            Vector2I size = EyeResolution.Update();
            if (size.X < 1 || size.Y < 1) return false;
            ReleaseTextures();
            var matrices = MyRender11.Environment_Matrices;
            var snapshot = matrices.Capture();
            MatrixD gameView = matrices.ViewD;
            // Match the body/animation command batch, not a newer simulation tick.
            var rig=RenderFrameBridge.ForCurrentOwner(CameraRig.Current);
            var markers=ReferenceEquals(rig,RenderFrameBridge.Current) ? RenderFrameBridge.Markers : null;
            var crosshair=ReferenceEquals(rig,RenderFrameBridge.Current) ? RenderFrameBridge.Crosshair : null;
            rig=SpectatorView.Active ? SpectatorView.RenderFrame(rig):ThirdPersonView.RenderFrame(rig);
            Matrix originInverse=rig?.OriginInverse ?? Player.RenderPlayerToAbsolute.inverted;
            double scale=rig?.UnitsPerMeter ?? 1;
            if(rig!=null) gameView=MatrixD.Invert(rig.Anchor);
            SceneCamera sceneCamera = SceneCamera.Current();
            Vector3D originalCamera = sceneCamera.Position;
            object leftAmbient=null,rightAmbient=null;
            EyeResolution.Scene resources=null;
            try
            {
                if(RenderFrameBridge.Remote!=null) NativeGloves.Prepare(null);
                DesktopCapture.Update(MyRender11.DeviceInstance);
                RemoteFeed.Render(RenderFrameBridge.Remote);
                MatrixD headView=VrMath.EyeView(gameView,renderPose.deviceToAbsolute.matrix,originInverse,Matrix.Identity,scale);
                LcdFeed.Render(RenderFrameBridge.Lcds,headView);
                NativeGloves.Prepare(rig,matrices.FarClipping);
                resources=new EyeResolution.Scene(size);
                Dlss.BeginFrame(size,rig);
                var output=Dlss.Output(size);
                leftTexture = MyManagers.RwTexturesPool.BorrowRtv("SEVR.Left", output.X, output.Y, Format.R8G8B8A8_UNorm_SRgb);
                rightTexture = MyManagers.RwTexturesPool.BorrowRtv("SEVR.Right", output.X, output.Y, Format.R8G8B8A8_UNorm_SRgb);
                StereoRenderState.Begin(headView,
                    rig?.ThirdPerson==true ? .005*scale : Math.Max(.03,matrices.NearClipping),matrices.LargeDistanceFarClipping);
                ThirdPersonView.RecordTrace(rig);
                StereoExposure.Begin();
                float left=0,right=0,top=0,bottom=0;
                var signalLimits=new Vector2(float.MaxValue);
                foreach(var eye in VrMath.Eyes)
                {
                    OpenVR.System.GetProjectionRaw(eye,ref left,ref right,ref top,ref bottom);
                    signalLimits=Vector2.Min(signalLimits,new Vector2(Math.Min(-left,right),Math.Min(-top,bottom))*.96f);
                }
                WorldMarkers.BeginFrame(MatrixD.Invert(headView),markers,signalLimits,rig?.TrackingToWorld,crosshair,EyeResolution.HudWidth(size.X));
                RenderEye(EVREye.Eye_Left, leftTexture, matrices, gameView, sceneCamera, originInverse,rig,out leftAmbient);
                if(leftAmbient!=null) { new BorrowedRtvTexture(leftAmbient).Release(); leftAmbient=null; }
                RenderEye(EVREye.Eye_Right, rightTexture, matrices, gameView, sceneCamera, originInverse,rig,out rightAmbient);
                SubmitEye(EVREye.Eye_Left,leftTexture);
                SubmitEye(EVREye.Eye_Right,rightTexture);
                GpuTiming.Begin(GpuTiming.Area.Companion);
                var source=(SharpDX.Direct3D11.Texture2D)leftTexture.GetResource();
                var destination=(SharpDX.Direct3D11.Texture2D)MyRender11.GetBackbuffer().GetResource();
                if(!Common.Config.MirrorDesktop)
                {
                    // The optional diagnostic desktop view is a separate scene pass.
                    matrices.Restore(snapshot); sceneCamera.Position=originalCamera; StereoRenderState.View=-1;
                    var desktop=MyManagers.RwTexturesPool.BorrowRtv("SEVR.Desktop",size.X,size.Y,Format.R8G8B8A8_UNorm_SRgb);
                    object ambient=null;
                    try
                    {
                        MyRender11.DrawGameScene(desktop,out ambient);
                        EyeResolution.Mirror((SharpDX.Direct3D11.Texture2D)desktop.GetResource(),destination,false);
                    }
                    finally { if(ambient!=null) new BorrowedRtvTexture(ambient).Release(); desktop.Release(); }
                }
                else EyeResolution.Mirror(source,destination);
                matrices.Restore(snapshot); sceneCamera.Position=originalCamera; StereoRenderState.View=-1;
                resources.Dispose();
                object debug=rightAmbient; rightAmbient=null;
                MyRender11.DrawDebugScene(debug);
                GpuTiming.End(GpuTiming.Area.Companion);
                MirroredDesktop=true;
                if (++submittedFrames == 1) Logger.Info($"FIRST STEREO FRAME submitted: {size.X}x{size.Y} per eye");
            }
            finally
            {
                StereoExposure.End();
                if(leftAmbient!=null) new BorrowedRtvTexture(leftAmbient).Release();
                if(rightAmbient!=null) new BorrowedRtvTexture(rightAmbient).Release();
                matrices.Restore(snapshot);
                sceneCamera.Position = originalCamera;
                StereoRenderState.View=-1;
                resources?.Dispose();
            }
            return true;
        }

        // The native HUD draws over the mirrored eye with the desktop camera; the eye already carries the VR HUD.
        internal void RestoreMirror()
        {
            if(!MirroredDesktop || !Common.Config.MirrorDesktop || leftTexture==null) return;
            EyeResolution.Mirror((SharpDX.Direct3D11.Texture2D)leftTexture.GetResource(),(SharpDX.Direct3D11.Texture2D)MyRender11.GetBackbuffer().GetResource());
        }

        private void RenderEye(EVREye eye, BorrowedRtvTexture target, EnvironmentMatrices env,
            MatrixD gameView, SceneCamera sceneCamera, Matrix originInverse,CameraRig.Frame rig,out object ambientOcclusion)
        {
            StereoRenderState.View=(int)eye;
            var timing=System.Diagnostics.Stopwatch.StartNew();
            MatrixD view = VrMath.EyeView(gameView, renderPose.deviceToAbsolute.matrix,
                originInverse, OpenVR.System.GetEyeToHeadTransform(eye).ToMatrix(),rig?.UnitsPerMeter ?? 1);
            MatrixD world = MatrixD.Invert(view);
            float l=0,r=0,t=0,b=0;
            OpenVR.System.GetProjectionRaw(eye, ref l, ref r, ref t, ref b);
            double near = rig?.ThirdPerson==true ? .005*rig.UnitsPerMeter : Math.Max(0.03, env.NearClipping);
            MatrixD projection = env.SetCamera(view,world,l,r,t,b,near,Dlss.Jitter((int)eye));
            Dlss.Capture((int)eye,env,projection);
            sceneCamera.Position = world.Translation;
            var sceneArea=eye==EVREye.Eye_Left ? GpuTiming.Area.SceneLeft : GpuTiming.Area.SceneRight;
            var uiArea=eye==EVREye.Eye_Left ? GpuTiming.Area.WorldUiLeft : GpuTiming.Area.WorldUiRight;
            GpuTiming.Begin(sceneArea);
            long sceneStart=FeatureTiming.Start();
            bool handLayer=RenderFrameBridge.Remote!=null || DesktopWindow.Current!=null || (RenderFrameBridge.Surfaces!=null && System.Linq.Enumerable.Any(RenderFrameBridge.Surfaces,s=>s.WindowPose.HasValue)) || Main.MenuOpen || Main.ShowDesktopPanel || MenuKeyboard.IsOpen;
            // The hand layer shares the scene's rasterization; DLSS resamples it with the scene depth.
            var sceneSize=MyRender11.Resolution;
            NativeHandLayer.Begin(sceneSize.X,sceneSize.Y,handLayer,rig?.ThirdPerson==true);
            Dlss.BeginParts(sceneSize);
            try { MyRender11.DrawGameScene(target, out ambientOcclusion); }
            finally { NativeHandLayer.End(); Dlss.EndParts(); }
            FeatureTiming.End(eye==EVREye.Eye_Left ? FeatureTiming.Area.SceneLeft : FeatureTiming.Area.SceneRight,sceneStart);
            GpuTiming.End(sceneArea);
            GpuTiming.Begin(uiArea);
            BloodOverlay.Draw((SharpDX.Direct3D11.Texture2D)target.GetResource(),projection);
            WorldMarkers.Draw((SharpDX.Direct3D11.Texture2D)target.GetResource(),view,projection);
            MatrixD trackingView=MatrixD.Invert((MatrixD)OpenVR.System.GetEyeToHeadTransform(eye).ToMatrix()*renderPose.deviceToAbsolute.matrix);
            RemoteFeed.Draw((SharpDX.Direct3D11.Texture2D)target.GetResource(),RenderFrameBridge.Remote,trackingView,VrMath.Projection(l,r,t,b,.03),renderPose.deviceToAbsolute.matrix);
            DesktopWindow.Draw((SharpDX.Direct3D11.Texture2D)target.GetResource(),trackingView,VrMath.Projection(l,r,t,b,.03));
            if (Main.MenuOpen || rig?.ThirdPerson==true) MenuHands.DrawInWorld((SharpDX.Direct3D11.Texture2D)target.GetResource(),eye,Main.MenuOpen);
            SpatialUi.Draw((SharpDX.Direct3D11.Texture2D)target.GetResource(),view,projection,RenderFrameBridge.Surfaces);
            if(rig?.ThirdPerson==true && NativeGloves.Shown(true))
                SpatialUi.Draw((SharpDX.Direct3D11.Texture2D)target.GetResource(),view,projection,RenderFrameBridge.Surfaces,tracking:true,trackingToWorld:rig.TrackingToWorld);
            SpatialUi.DrawFloating((SharpDX.Direct3D11.Texture2D)target.GetResource(),trackingView,VrMath.Projection(l,r,t,b,.03),RenderFrameBridge.Surfaces);
            FloatingKeyboard.Draw((SharpDX.Direct3D11.Texture2D)target.GetResource(),eye);
            ToolbarWheel.DrawWorld((SharpDX.Direct3D11.Texture2D)target.GetResource(),view,projection,
                rig?.TrackingToWorld ?? (MatrixD)originInverse*MatrixD.Invert(gameView));
            if(!nativeGizmosUnavailable)
            {
                try { NativeGizmos.DrawEye(target.Instance,RenderFrameBridge.Symmetry,RenderFrameBridge.Planets,view,projection); }
                catch(Exception ex)
                {
                    // Build and placement overlays must not stop stereo rendering.
                    nativeGizmosUnavailable=true;
                    Logger.Warning(ex,"Native build and placement gizmos disabled until restart");
                }
            }
            TargetFeedback.Draw((SharpDX.Direct3D11.Texture2D)target.GetResource(),GridSelection.Active || TargetLock.Aiming ? RenderFrameBridge.Selection:null,view,projection,PhysicalSurface.SceneDepth());
            if(rig?.ThirdPerson!=true) HoloSight.Draw((SharpDX.Direct3D11.Texture2D)target.GetResource(),RenderFrameBridge.Holo,view,projection,PhysicalSurface.SceneDepth());
            GpuTiming.End(uiArea);
            StereoRenderState.Record("eye",timing.Elapsed.TotalMilliseconds,world.Translation.X,world.Translation.Y,world.Translation.Z,projection.M11,projection.M22,projection.M31,projection.M32);
        }

        private void SubmitEye(EVREye eye,BorrowedRtvTexture target)
        {
            var error = VrSubmit.Eye(eye,target.GetResource().NativePointer,EColorSpace.Auto,ref bounds);
            if (error != EVRCompositorError.None && error != EVRCompositorError.DoNotHaveFocus)
                throw new InvalidOperationException("OpenVR eye submission failed: " + error);
        }

        public void ReleaseTextures()
        {
            TargetFeedback.Dispose();
            NativeHandLayer.Reset();
            leftTexture?.Release(); rightTexture?.Release();
            leftTexture = rightTexture = null;
        }

        public void RequestRecenter() { recenterPending = true; Components.VRGUIManager.RequestRecenter(); }
        protected override void OnStartTracking() { recenterPending = true; }
        public override void MainUpdate()
        {
            if (recenterPending && pose.isTracked)
            {
                Player.ResetPlayerFloor();
                recenterPending = false;
            }
        }
        public void CreatePopup(string message) { Logger.Info(message); }
    }
}
