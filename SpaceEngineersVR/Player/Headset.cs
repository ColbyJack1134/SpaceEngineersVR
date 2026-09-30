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
        private bool reportedMirrorFallback;
        public bool MirroredDesktop { get; private set; }

        public Headset() : base(actionName: "")
        {
            deviceId = OpenVR.k_unTrackedDeviceIndex_Hmd;
            uint width = 0, height = 0;
            OpenVR.System.GetRecommendedRenderTargetSize(ref width, ref height);
            Logger.Info($"SteamVR recommends {width}x{height} per eye. Prototype uses the game window resolution to avoid unsafe swapchain resizing.");
        }

        public bool RenderUpdate()
        {
            MirroredDesktop=false;
            if (!MyRender11.m_DrawScene || !renderPose.isTracked) return false;
            Vector2I size = MyRender11.Resolution;
            if (size.X < 1 || size.Y < 1) return false;
            ReleaseTextures();
            leftTexture = MyManagers.RwTexturesPool.BorrowRtv("SEVR.Left", size.X, size.Y, Format.R8G8B8A8_UNorm_SRgb);
            rightTexture = MyManagers.RwTexturesPool.BorrowRtv("SEVR.Right", size.X, size.Y, Format.R8G8B8A8_UNorm_SRgb);
            var matrices = MyRender11.Environment_Matrices;
            var snapshot = matrices.Capture();
            MatrixD gameView = matrices.ViewD;
            // Match the body/animation command batch, not a newer simulation tick.
            var rig=RenderFrameBridge.ForCurrentOwner(CameraRig.Current);
            Matrix originInverse=rig?.OriginInverse ?? Player.RenderPlayerToAbsolute.inverted;
            if(rig!=null) gameView=MatrixD.Invert(rig.Anchor);
            SceneCamera sceneCamera = SceneCamera.Current();
            Vector3D originalCamera = sceneCamera.Position;
            object leftAmbient=null,rightAmbient=null;
            try
            {
                StereoRenderState.Begin(VrMath.EyeView(gameView,renderPose.deviceToAbsolute.matrix,originInverse,Matrix.Identity),Math.Max(.03,matrices.NearClipping),matrices.LargeDistanceFarClipping);
                WorldMarkers.BeginFrame(MatrixD.Invert(VrMath.EyeView(gameView,renderPose.deviceToAbsolute.matrix,originInverse,Matrix.Identity)));
                RenderEye(EVREye.Eye_Left, leftTexture, matrices, gameView, sceneCamera, originInverse,out leftAmbient);
                if(leftAmbient!=null) { new BorrowedRtvTexture(leftAmbient).Release(); leftAmbient=null; }
                RenderEye(EVREye.Eye_Right, rightTexture, matrices, gameView, sceneCamera, originInverse,out rightAmbient);
                if(Common.Config.MirrorDesktop)
                {
                    var source=(SharpDX.Direct3D11.Texture2D)leftTexture.GetResource();
                    var destination=(SharpDX.Direct3D11.Texture2D)MyRender11.GetBackbuffer().GetResource();
                    var a=source.Description; var b=destination.Description;
                    bool format=b.Format==a.Format || b.Format==Format.R8G8B8A8_UNorm || b.Format==Format.R8G8B8A8_Typeless;
                    if(format && a.Width==b.Width && a.Height==b.Height && a.SampleDescription.Count==b.SampleDescription.Count &&
                        a.SampleDescription.Quality==b.SampleDescription.Quality && a.MipLevels==b.MipLevels && a.ArraySize==b.ArraySize)
                    {
                        MyRender11.DeviceInstance.ImmediateContext.CopyResource(source,destination);
                        matrices.Restore(snapshot); sceneCamera.Position=originalCamera; StereoRenderState.View=-1;
                        object debug=rightAmbient; rightAmbient=null;
                        MyRender11.DrawDebugScene(debug);
                        MirroredDesktop=true;
                    }
                    else if(!reportedMirrorFallback)
                    { reportedMirrorFallback=true; Logger.Info("Desktop texture cannot copy the VR eye; retaining the native desktop render"); }
                }
                if (++submittedFrames == 1) Logger.Info($"FIRST STEREO FRAME submitted: {size.X}x{size.Y} per eye");
            }
            finally
            {
                if(leftAmbient!=null) new BorrowedRtvTexture(leftAmbient).Release();
                if(rightAmbient!=null) new BorrowedRtvTexture(rightAmbient).Release();
                matrices.Restore(snapshot);
                sceneCamera.Position = originalCamera;
                StereoRenderState.View=-1;
            }
            return true;
        }

        private void RenderEye(EVREye eye, BorrowedRtvTexture target, EnvironmentMatrices env,
            MatrixD gameView, SceneCamera sceneCamera, Matrix originInverse,out object ambientOcclusion)
        {
            StereoRenderState.View=(int)eye;
            var timing=System.Diagnostics.Stopwatch.StartNew();
            MatrixD view = VrMath.EyeView(gameView, renderPose.deviceToAbsolute.matrix,
                originInverse, OpenVR.System.GetEyeToHeadTransform(eye).ToMatrix());
            MatrixD world = MatrixD.Invert(view);
            float l=0,r=0,t=0,b=0;
            OpenVR.System.GetProjectionRaw(eye, ref l, ref r, ref t, ref b);
            double near = Math.Max(0.03, env.NearClipping);
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
            MyRender11.DrawGameScene(target, out ambientOcclusion);
            WorldMarkers.Draw((SharpDX.Direct3D11.Texture2D)target.GetResource(),view,projection);
            if (Main.MenuOpen) MenuHands.DrawInWorld((SharpDX.Direct3D11.Texture2D)target.GetResource(), eye);
            SpatialUi.Draw((SharpDX.Direct3D11.Texture2D)target.GetResource(),view,projection,RenderFrameBridge.Surfaces);
            FloatingKeyboard.Draw((SharpDX.Direct3D11.Texture2D)target.GetResource(),eye);
            ToolbarWheel.DrawWorld((SharpDX.Direct3D11.Texture2D)target.GetResource(),view,projection,(MatrixD)originInverse*MatrixD.Invert(gameView));
            var input = new Texture_t { eColorSpace=EColorSpace.Auto, eType=ETextureType.DirectX, handle=target.GetResource().NativePointer };
            var error = OpenVR.Compositor.Submit(eye,ref input,ref bounds,EVRSubmitFlags.Submit_Default);
            StereoRenderState.Record("eye",timing.Elapsed.TotalMilliseconds,world.Translation.X,world.Translation.Y,world.Translation.Z,projection.M11,projection.M22,projection.M31,projection.M32);
            if (error != EVRCompositorError.None && error != EVRCompositorError.DoNotHaveFocus)
                throw new InvalidOperationException("OpenVR eye submission failed: " + error);
        }

        public void ReleaseTextures()
        {
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
