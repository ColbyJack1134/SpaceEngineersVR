using System;
using System.IO;
using System.Xml.Serialization;
using HarmonyLib;
using SharpDX.Direct3D11;
using SpaceEngineersVR.Config;
using SpaceEngineersVR.Player;
using SpaceEngineersVR.Wrappers;
using VRageMath;
using VRageRender.Messages;

namespace SpaceEngineersVR.Diagnostics
{
    internal static class ResolutionTests
    {
        public static void Run(Action<string> log)
        {
            var recommendation=new Vector2I(2112,2304);
            if(EyeResolution.Size(recommendation,1)!=recommendation || EyeResolution.Size(recommendation,.75f)!=new Vector2I(1584,1728))
                throw new Exception("Eye scaling changed the runtime aspect or default recommendation");
            foreach(float invalid in new[] {float.NaN,float.PositiveInfinity})
                if(EyeResolution.Size(recommendation,invalid)!=recommendation) throw new Exception("Invalid eye scale was accepted");
            var bounded=EyeResolution.Size(new Vector2I(16000,18000),1.5f);
            if(bounded.X>8192 || bounded.Y>8192 || (long)bounded.X*bounded.Y>33560000 || Math.Abs((double)bounded.X/bounded.Y-16000d/18000)>.001)
                throw new Exception("Eye target allocation limit changed its aspect or exceeds the budget");
            bool rejected=false;
            try { EyeResolution.Size(Vector2I.Zero,1); } catch(InvalidOperationException) { rejected=true; }
            if(!rejected) throw new Exception("Invalid runtime recommendation accepted");
            var bounds=EyeResolution.MirrorBounds(recommendation,new Vector2I(1280,720),true);
            if(Math.Abs(bounds.Height-720)>.01 || bounds.X<300 || Math.Abs(bounds.Width/bounds.Height-2112f/2304)>.001)
                throw new Exception("Desktop mirror crops or stretches the eye image");
            var serializer=new XmlSerializer(typeof(PluginConfig));
            PluginConfig config;
            using(var reader=new StringReader("<PluginConfig><TrackedArms>false</TrackedArms></PluginConfig>")) config=(PluginConfig)serializer.Deserialize(reader);
            if(config.EyeRenderScale!=1 || !config.MirrorDesktop || config.TrackedArms) throw new Exception("Existing profiles lost their rendering defaults");
            config.EyeRenderScale=.75f;
            using(var writer=new StringWriter())
            {
                serializer.Serialize(writer,config);
                using(var reader=new StringReader(writer.ToString())) config=(PluginConfig)serializer.Deserialize(reader);
            }
            if(config.EyeRenderScale!=.75f || config.TrackedArms) throw new Exception("Render scale persistence changed other options");
            log("PASS independent eye resolution: runtime default, scale/aspect, allocation bounds, letterbox and existing profile persistence");
        }

        internal static void RunNative(string output,MyRenderMessageSetCameraViewMatrix camera)
        {
            var bufferField=AccessTools.Field(AccessTools.TypeByName("VRage.Render11.Resources.MyGBuffer"),"Main");
            object desktop=bufferField.GetValue(null),previousEye=null,previousCamera=null;
            var aoField=AccessTools.Field(AccessTools.TypeByName("VRageRender.MyHBAO"),"m_fullResViewDepthTarget");
            object desktopAo=aoField.GetValue(null);
            var desktopSize=MyRender11.Resolution;
            var matrices=MyRender11.Environment_Matrices;
            var snapshot=matrices.Capture();
            int allocations=EyeResolution.Scene.Allocations;
            try
            {
                for(int pass=0;pass<3;pass++)
                {
                    var size=pass==2 ? new Vector2I(1584,1728) : new Vector2I(2112,2304);
                    using(new EyeResolution.Scene(size))
                    {
                        object eye=bufferField.GetValue(null);
                        if(ReferenceEquals(eye,desktop) || (pass==1 && (!ReferenceEquals(eye,previousEye) || EyeResolution.Scene.Allocations!=allocations+2)))
                            throw new Exception("Native eye resources were not isolated and retained between frames");
                        previousEye=eye;
                        var aoTexture=(Texture2D)CockpitRender.Member(aoField.GetValue(null),"Resource");
                        if(aoTexture.Description.Width!=size.X || aoTexture.Description.Height!=size.Y)
                            throw new Exception("Eye AO resources do not match the eye target");
                        foreach(string attachment in new[] {"GBuffer0","GBuffer1","GBuffer2","LBuffer","DepthStencil"})
                        {
                            var texture=(Texture2D)CockpitRender.Member(CockpitRender.Member(eye,attachment),"Resource");
                            if(texture.Description.Width!=size.X || texture.Description.Height!=size.Y) throw new Exception(attachment+" has the wrong eye resolution");
                        }
                        var viewport=(Vector2I)AccessTools.Property(AccessTools.TypeByName("VRageRender.MyRender11"),"ViewportResolution").GetValue(null);
                        if(MyRender11.Resolution!=size || viewport!=size || MyRender11.Settings.User.DRScaling) throw new Exception("Eye viewport or DRS state differs from its buffers");
                        MyRender11.SetupCameraMatrices(camera);
                        var target=MyManagers.RwTexturesPool.BorrowRtv("SEVR resolution probe",size.X,size.Y,SharpDX.DXGI.Format.R8G8B8A8_UNorm_SRgb);
                        object ao=null;
                        try
                        {
                            MyRender11.DrawGameScene(target,out ao);
                            var albedo=(Texture2D)CockpitRender.Member(CockpitRender.Member(eye,"GBuffer0"),"Resource");
                            EyeResolution.Mirror(albedo,(Texture2D)target.GetResource(),false);
                            if(pass!=1) UiTests.Save((Texture2D)target.GetResource(),Path.Combine(output,"eye-resolution-"+size.X+".png"));
                            if(pass==0)
                            {
                                var mirror=MyManagers.RwTexturesPool.BorrowRtv("SEVR mirror probe",1280,720,SharpDX.DXGI.Format.R8G8B8A8_UNorm_SRgb);
                                try
                                {
                                    EyeResolution.Mirror((Texture2D)target.GetResource(),(Texture2D)mirror.GetResource());
                                    UiTests.Save((Texture2D)mirror.GetResource(),Path.Combine(output,"eye-mirror-720.png"));
                                }
                                finally { mirror.Release(); }
                            }
                        }
                        finally { if(ao!=null) new BorrowedRtvTexture(ao).Release(); target.Release(); }
                    }
                    if(!ReferenceEquals(desktop,bufferField.GetValue(null)) || !ReferenceEquals(desktopAo,aoField.GetValue(null)) || MyRender11.Resolution!=desktopSize)
                        throw new Exception("Eye rendering did not restore desktop resources");
                    if(pass<2)
                    {
                        using(new EyeResolution.Scene(RemoteFeed.Resolution,true))
                        {
                            var cameraBuffer=bufferField.GetValue(null);
                            if(ReferenceEquals(cameraBuffer,previousEye) || (pass==1 && !ReferenceEquals(cameraBuffer,previousCamera)))
                                throw new Exception("1080p camera resources were not isolated and retained");
                            previousCamera=cameraBuffer;
                            var cameraDepth=(Texture2D)CockpitRender.Member(aoField.GetValue(null),"Resource");
                            if(cameraDepth.Description.Width!=1920 || cameraDepth.Description.Height!=1080)
                                throw new Exception("Camera AO target is not 1080p");
                        }
                    }
                }
                if(EyeResolution.Scene.Allocations!=allocations+3) throw new Exception("Eye/camera resources were reallocated between fixed-size frames");
                Plugin.Logger.Info("PASS native independent resolution: retained eye and 1920x1080 camera scene/depth/AO resources, eye resize, 1280x720 mirror, desktop restored");
            }
            finally { matrices.Restore(snapshot); EyeResolution.Scene.RestoreNative(); }
        }
    }
}
