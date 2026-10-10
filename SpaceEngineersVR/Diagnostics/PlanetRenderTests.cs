using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using HarmonyLib;
using SpaceEngineersVR.Player;
using SpaceEngineersVR.Wrappers;
using VRage.Game;
using VRage.ObjectBuilders;
using VRageMath;
using VRageRender.Messages;

namespace SpaceEngineersVR.Diagnostics
{
    internal static class PlanetRenderTests
    {
        internal static MyRenderMessageDebugDrawSphere[] Capture(Vector3D position,float radius,bool valid)
        {
            var type=AccessTools.TypeByName("Sandbox.Game.Entities.MyVoxelClipboard");
            var clipboard=FormatterServices.GetUninitializedObject(type);
            AccessTools.Field(type,"m_planetMode").SetValue(clipboard,true);
            AccessTools.Field(type,"m_pastePosition").SetValue(clipboard,position);
            AccessTools.Field(type,"m_canBePlaced").SetValue(clipboard,valid);
            AccessTools.Field(type,"m_copiedVoxelMaps").SetValue(clipboard,new List<MyObjectBuilder_EntityBase> {new MyObjectBuilder_Planet {Radius=radius}});
            bool previous=PlanetPreview.Capturing;
            PlanetPreview.Begin(); PlanetPreview.Capturing=true;
            try
            {
                AccessTools.Method(type,"UpdateVoxelMapTransformations").Invoke(clipboard,null);
                PlanetPreview.Commit(); return PlanetPreview.Current;
            }
            finally { PlanetPreview.Capturing=previous; }
        }
        internal static void Render(string output)
        {
            var matrices=MyRender11.Environment_Matrices;
            var saved=matrices.Capture(); var size=MyRender11.Resolution;
            var target=MyManagers.RwTexturesPool.BorrowRtv("Native planet eyes",size.X,size.Y,SharpDX.DXGI.Format.R8G8B8A8_UNorm_SRgb);
            try
            {
                foreach(bool valid in new[] {true,false}) foreach(int eye in new[] {-1,1})
                {
                    var center=new Vector3D(1e9,-1e9,1e9);
                    var from=center+new Vector3D(eye*3200,40000,160000);
                    var view=MatrixD.CreateLookAt(from,center,Vector3D.Up); view.Translation=Vector3D.Zero;
                    matrices.CameraPosition=from; matrices.ViewProjectionAt0=(Matrix)(view*VrMath.Projection(-1.1f,1.1f,-.62f,.62f,.03));
                    using(var rtv=new SharpDX.Direct3D11.RenderTargetView(MyRender11.DeviceInstance,target.GetResource()))
                        MyRender11.DeviceInstance.ImmediateContext.ClearRenderTargetView(rtv,new SharpDX.Mathematics.Interop.RawColor4(.035f,.055f,.075f,1));
                    var renderer=AccessTools.TypeByName("VRageRender.MyLinesRenderer");
                    var vertices=AccessTools.Field(renderer,"m_vertices").GetValue(null);
                    var batches=AccessTools.Field(renderer,"m_batches").GetValue(null);
                    NativeGizmos.Draw(target.Instance,null,Capture(center,65000,valid));
                    if(!ReferenceEquals(vertices,AccessTools.Field(renderer,"m_vertices").GetValue(null)) ||
                        !ReferenceEquals(batches,AccessTools.Field(renderer,"m_batches").GetValue(null))) throw new Exception("Planet eye renderer replaced desktop debug buffers");
                    UiTests.Save(target.GetResource(),Path.Combine(output,"planet-native-"+(valid ? "valid":"blocked")+"-"+(eye<0 ? "left":"right")+".png"));
                }
            }
            finally { target.Release(); matrices.Restore(saved); PlanetPreview.Begin(); }
        }
    }
}
