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
            leftTexture = MyManagers.RwTexturesPool.BorrowRtv("SEVR.Left", size.X, size.Y, Format.R8G8B8A8_UNorm_SRgb);
            rightTexture = MyManagers.RwTexturesPool.BorrowRtv("SEVR.Right", size.X, size.Y, Format.R8G8B8A8_UNorm_SRgb);
            var matrices = MyRender11.Environment_Matrices;
            var snapshot = matrices.Capture();
            MatrixD gameView = matrices.ViewD;
            // Match the body/animation command batch, not a newer simulation tick.
            var rig=RenderFrameBridge.ForCurrentOwner(CameraRig.Current);
            var markers=ReferenceEquals(rig,RenderFrameBridge.Current) ? RenderFrameBridge.Markers : null;
            rig=ThirdPersonView.RenderFrame(rig);
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
                RemoteFeed.Render(RenderFrameBridge.Remote);
                NativeGloves.Prepare(rig);
                resources=new EyeResolution.Scene(size);
                StereoRenderState.Begin(VrMath.EyeView(gameView,renderPose.deviceToAbsolute.matrix,originInverse,Matrix.Identity,scale),
                    rig?.ThirdPerson==true ? .005*scale : Math.Max(.03,matrices.NearClipping),matrices.LargeDistanceFarClipping);
                ThirdPersonView.RecordTrace(rig);
                WorldMarkers.BeginFrame(MatrixD.Invert(VrMath.EyeView(gameView,renderPose.deviceToAbsolute.matrix,originInverse,Matrix.Identity,scale)),markers);
                RenderEye(EVREye.Eye_Left, leftTexture, matrices, gameView, sceneCamera, originInverse,rig,out leftAmbient);
                if(leftAmbient!=null) { new BorrowedRtvTexture(leftAmbient).Release(); leftAmbient=null; }
                RenderEye(EVREye.Eye_Right, rightTexture, matrices, gameView, sceneCamera, originInverse,rig,out rightAmbient);
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
                if(leftAmbient!=null) new BorrowedRtvTexture(leftAmbient).Release();
                if(rightAmbient!=null) new BorrowedRtvTexture(rightAmbient).Release();
                matrices.Restore(snapshot);
                sceneCamera.Position = originalCamera;
                StereoRenderState.View=-1;
                resources?.Dispose();
            }
            return true;
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
            MatrixD projection = VrMath.Projection(l,r,t,b,near);
            MatrixD atZero = view; atZero.Translation = Vector3D.Zero;
            env.CameraPosition = world.Translation;
            env.ViewD = view; env.InvViewD = world;
            env.ViewAt0 = atZero; env.InvViewAt0 = MatrixD.Invert(atZero);
            env.Projection = projection; env.ProjectionForSkybox = projection;
            env.InvProjection = MatrixD.Invert(projection);
            env.ViewProjectionD = view * projection; env.InvViewProjectionD = MatrixD.Invert(env.ViewProjectionD);
            env.ViewProjectionAt0 = atZero * projection; env.InvViewProjectionAt0 = MatrixD.Invert(env.ViewProjectionAt0);
            env.FovH = (float)(Math.Atan(r) - Math.Atan(l));
            env.FovV = (float)(Math.Atan(b) - Math.Atan(t));
            env.OriginalProjection = VrMath.Projection(l,r,t,b,near,Math.Max(env.FarClipping,near+1));
            env.OriginalProjectionFar = VrMath.Projection(l,r,t,b,near,Math.Max(env.LargeDistanceFarClipping,near+1));
            env.ViewFrustumClippedD = new BoundingFrustumD(view * env.OriginalProjection);
            env.ViewFrustumClippedFarD = new BoundingFrustumD(view * env.OriginalProjectionFar);
            sceneCamera.Position = world.Translation;
            var sceneArea=eye==EVREye.Eye_Left ? GpuTiming.Area.SceneLeft : GpuTiming.Area.SceneRight;
            var uiArea=eye==EVREye.Eye_Left ? GpuTiming.Area.WorldUiLeft : GpuTiming.Area.WorldUiRight;
            GpuTiming.Begin(sceneArea);
            long sceneStart=FeatureTiming.Start();
            var targetSize=((SharpDX.Direct3D11.Texture2D)target.GetResource()).Description;
            bool handLayer=RenderFrameBridge.Remote!=null || rig?.ThirdPerson!=true && (Main.MenuOpen || Main.ShowDesktopPanel);
            NativeHandLayer.Begin(targetSize.Width,targetSize.Height,handLayer);
            try { MyRender11.DrawGameScene(target, out ambientOcclusion); }
            finally { NativeHandLayer.End(); }
            FeatureTiming.End(eye==EVREye.Eye_Left ? FeatureTiming.Area.SceneLeft : FeatureTiming.Area.SceneRight,sceneStart);
            GpuTiming.End(sceneArea);
            GpuTiming.Begin(uiArea);
            WorldMarkers.Draw((SharpDX.Direct3D11.Texture2D)target.GetResource(),view,projection);
            MatrixD trackingView=MatrixD.Invert((MatrixD)OpenVR.System.GetEyeToHeadTransform(eye).ToMatrix()*renderPose.deviceToAbsolute.matrix);
            RemoteFeed.Draw((SharpDX.Direct3D11.Texture2D)target.GetResource(),RenderFrameBridge.Remote,trackingView,VrMath.Projection(l,r,t,b,.03));
            if (Main.MenuOpen || rig?.ThirdPerson==true) MenuHands.DrawInWorld((SharpDX.Direct3D11.Texture2D)target.GetResource(),eye,Main.MenuOpen);
            SpatialUi.Draw((SharpDX.Direct3D11.Texture2D)target.GetResource(),view,projection,RenderFrameBridge.Surfaces);
            if(rig?.ThirdPerson==true && NativeGloves.Visible)
                SpatialUi.Draw((SharpDX.Direct3D11.Texture2D)target.GetResource(),view,projection,RenderFrameBridge.Surfaces,tracking:true,trackingToWorld:rig.TrackingToWorld);
            FloatingKeyboard.Draw((SharpDX.Direct3D11.Texture2D)target.GetResource(),eye);
            ToolbarWheel.DrawWorld((SharpDX.Direct3D11.Texture2D)target.GetResource(),view,projection,
                rig?.TrackingToWorld ?? (MatrixD)originInverse*MatrixD.Invert(gameView));
            GpuTiming.End(uiArea);
            var input = new Texture_t { eColorSpace=EColorSpace.Auto, eType=ETextureType.DirectX, handle=target.GetResource().NativePointer };
            var error = OpenVR.Compositor.Submit(eye,ref input,ref bounds,EVRSubmitFlags.Submit_Default);
            StereoRenderState.Record("eye",timing.Elapsed.TotalMilliseconds,world.Translation.X,world.Translation.Y,world.Translation.Z,projection.M11,projection.M22,projection.M31,projection.M32);
            if (error != EVRCompositorError.None && error != EVRCompositorError.DoNotHaveFocus)
                throw new InvalidOperationException("OpenVR eye submission failed: " + error);
        }

        public void ReleaseTextures()
        {
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
