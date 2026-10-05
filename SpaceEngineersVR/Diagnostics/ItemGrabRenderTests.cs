using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using SpaceEngineersVR.Player;
using SpaceEngineersVR.Plugin;
using VRageMath;
using VRageRender;
using VRageRender.Import;
using VRageRender.Messages;

namespace SpaceEngineersVR.Diagnostics
{
    internal static partial class PhysicalRendererProbe
    {
        private static int grabStep;
        private static string grabCapture;
        private static readonly string[] grabModels={"AutomaticRifle","Pistol_Warfare","RocketLauncher_Regular","Welder","AngleGrinder","HandDrill","Rifle_SemiAuto_Magazine_Warfare","Pistol_Magazine_Warfare","MagazineRL_Regular"};
        private static void UpdateItemGrabs()
        {
            if(camera!=null)
            {
                MyRenderProxy.SetCameraViewMatrix(camera.ViewMatrix,camera.ProjectionMatrix,camera.ProjectionFarMatrix,camera.FOV,camera.FOV,.005f,100,100,camera.CameraPosition,smooth:false);
                MyRenderProxy.Draw3DScene();
            }
            if(DateTime.UtcNow<next) return;
            if(grabCapture!=null) { pending=grabCapture; grabCapture=null; return; }
            if(pending!=null)
            {
                if(captured!=pending) return;
                pending=null; grabStep++;
                ItemGrabVisual.Reset();
                if(native!=uint.MaxValue) { MyRenderProxy.RemoveRenderObject(native,MyRenderProxy.ObjectType.Entity); native=uint.MaxValue; }
                if(grabStep==(Environment.GetEnvironmentVariable("SEVR_AMMO_PARENT")=="1" ? 3:grabModels.Length)*4)
                {
                    Stop(); Logger.Info("PHYSICAL RENDER SMOKE PASSED: installed item grab mesh highlights, idle, hover, moved parent and hidden held cue."); return;
                }
            }
            string name=grabModels[grabStep/4],model="Models/Weapons/"+name+".mwm";
            var profile=WeaponProfile.All.FirstOrDefault(p=>p.Model==model);
            Vector3? contact=null;
            if(profile!=null)
            {
                var item=XDocument.Load(Path.Combine(VRage.FileSystem.MyFileSystem.ContentPath,"Data","HandItems.sbc")).Descendants("HandItem")
                    .Single(x=>(string)x.Element("PhysicalItemId")?.Attribute("Subtype")==profile.Item);
                contact=profile.SupportContact(WeaponTests.Palm(item,"Left"));
            }
            native=MyRenderProxy.CreateRenderEntity("SEVR item grab fixture",model,MatrixD.Identity,MyMeshDrawTechnique.MESH,RenderFlags.Visible|RenderFlags.ForceOldPipeline,(CullingOptions)0,Color.White,neutralPaint);
            int stage=grabStep%4;
            ItemGrabVisual.Preview(model,contact,MatrixD.Identity,native,stage==2 ? 1:stage==3 ? 2:stage);
            var world=MatrixD.Identity;
            if(stage>=2)
            {
                world=MatrixD.CreateFromYawPitchRoll(.65,-.3,.2); world.Translation=new Vector3D(1.3,-.7,2.1);
                MyRenderProxy.UpdateRenderObject(native,world);
            }
            var target=(Vector3D)(contact ?? VRage.Game.Models.MyModels.GetModelOnlyData(model).BoundingBox.Center);
            var eye=target+new Vector3D(-.65,.22,.25);
            eye=Vector3D.Transform(eye,world); target=Vector3D.Transform(target,world);
            var view=MatrixD.CreateLookAt(eye,target,world.Up);
            float fov=profile==null ? .23f:.45f,aspect=16f/9;
            var projection=(Matrix)VrMath.Projection(-aspect*fov,aspect*fov,-fov,fov,.005,100);
            camera=new MyRenderMessageSetCameraViewMatrix {ViewMatrix=view,ProjectionMatrix=projection,ProjectionFarMatrix=projection,FOV=2*(float)Math.Atan(fov),FOVForSkybox=2*(float)Math.Atan(fov),NearPlane=.005f,FarPlane=100,FarFarPlane=100,CameraPosition=eye};
            MyRenderProxy.SetCameraViewMatrix(view,projection,projection,camera.FOV,camera.FOV,.005f,100,100,eye,smooth:false);
            phase=1; grabCapture=Path.Combine(output,"item-grab-"+name+"-"+stage+".png"); next=DateTime.UtcNow.AddSeconds(3);
        }
    }
}
