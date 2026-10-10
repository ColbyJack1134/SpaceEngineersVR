using System;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using HarmonyLib;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Cube;
using SpaceEngineersVR.Player;
using SpaceEngineersVR.Wrappers;
using VRage.Game.Entity;
using VRageMath;

namespace SpaceEngineersVR.Diagnostics
{
    internal static class SymmetryRenderTests
    {
        internal static SymmetryPlanes.View[] Capture(MySymmetrySettingModeEnum mode)
        {
            var builder=(MyCubeBuilder)FormatterServices.GetUninitializedObject(typeof(MyCubeBuilder));
            var grid=(MyCubeGrid)FormatterServices.GetUninitializedObject(typeof(MyCubeGrid));
            var entity=new MyEntity(); entity.PositionComp.LocalAABB=new BoundingBox(new Vector3(-5,-6,-7),new Vector3(5,6,7));
            AccessTools.GetDeclaredFields(typeof(MyEntity)).First(field=>typeof(VRage.Game.Components.MyPositionComponentBase).IsAssignableFrom(field.FieldType)).SetValue(grid,entity.PositionComp);
            AccessTools.Field(typeof(MyCubeGrid),"<GridSize>k__BackingField").SetValue(grid,2.5f);
            AccessTools.Field(typeof(MyCubeBuilder),"m_currentGrid").SetValue(builder,grid);
            bool previous=SymmetryPlanes.Capturing;
            SymmetryPlanes.Begin(); SymmetryPlanes.Capturing=true;
            try
            {
                AccessTools.Method(typeof(MyCubeBuilder),"DrawSymmetryPlane").Invoke(builder,new object[] {mode,grid,Vector3.Zero});
                SymmetryPlanes.Commit(); return SymmetryPlanes.Current;
            }
            finally { SymmetryPlanes.Capturing=previous; }
        }
        internal static void Render(string output)
        {
            var matrices=MyRender11.Environment_Matrices;
            var snapshot=matrices.Capture();
            var size=MyRender11.Resolution;
            var target=MyManagers.RwTexturesPool.BorrowRtv("Native symmetry eyes",size.X,size.Y,SharpDX.DXGI.Format.R8G8B8A8_UNorm_SRgb);
            try
            {
                var planes=new System.Collections.Generic.List<SymmetryPlanes.View>();
                foreach(var mode in new[] {MySymmetrySettingModeEnum.XPlane,MySymmetrySettingModeEnum.YPlane,MySymmetrySettingModeEnum.ZPlane}) planes.AddRange(Capture(mode));
                foreach(int eye in new[] {-1,1})
                {
                    var from=new Vector3D(18+eye*.032,14,22);
                    var view=MatrixD.CreateLookAt(from,Vector3D.Zero,Vector3D.Up); view.Translation=Vector3D.Zero;
                    matrices.CameraPosition=from; matrices.ViewProjectionAt0=(Matrix)(view*VrMath.Projection(-1.1f,1.1f,-.62f,.62f,.03));
                    using(var rtv=new SharpDX.Direct3D11.RenderTargetView(MyRender11.DeviceInstance,target.GetResource()))
                        MyRender11.DeviceInstance.ImmediateContext.ClearRenderTargetView(rtv,new SharpDX.Mathematics.Interop.RawColor4(.035f,.055f,.075f,1));
                    SymmetryPlanes.Draw(target.Instance,planes.ToArray());
                    UiTests.Save(target.GetResource(),Path.Combine(output,"symmetry-native-eye-"+(eye<0 ? "left":"right")+".png"));
                }
            }
            finally { target.Release(); matrices.Restore(snapshot); SymmetryPlanes.Begin(); }
        }
    }
}
