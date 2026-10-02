using System;
using System.IO;
using System.Runtime.InteropServices;
using HarmonyLib;
using SharpDX.Direct3D11;
using SpaceEngineersVR.Player;
using VRageMath;

namespace SpaceEngineersVR.Diagnostics
{
    internal static class PolishRenderTests
    {
        internal static void BeginHands(int width,int height)
        {
            var state=AccessTools.Field(typeof(NativeGloves),"current").GetValue(null);
            NativeHandLayer.Actor=((uint[])CockpitRender.Member(state,"Actors"))[0];
            NativeHandLayer.Begin(width,height,true);
        }
        internal static void Hands(Texture2D target,string output,MatrixD view,MatrixD projection)
        {
            NativeHandLayer.End();
            using(var source=NativeHandLayer.Depth.ResourceAs<Texture2D>())
            {
                var description=source.Description;
                description.BindFlags=BindFlags.None; description.Usage=ResourceUsage.Staging; description.CpuAccessFlags=CpuAccessFlags.Read;
                using(var readback=new Texture2D(source.Device,description))
                {
                    var context=source.Device.ImmediateContext;
                    context.CopyResource(source,readback);
                    var mapped=context.MapSubresource(readback,0,MapMode.Read,MapFlags.None);
                    int count=0;
                    try
                    {
                        var row=new float[description.Width];
                        for(int y=0;y<description.Height;y++)
                        {
                            Marshal.Copy(IntPtr.Add(mapped.DataPointer,y*mapped.RowPitch),row,0,row.Length);
                            for(int x=0;x<row.Length;x++) if(row[x]>0) count++;
                        }
                    }
                    finally { context.UnmapSubresource(readback,0); }
                    if(count<100) throw new Exception("Native glove depth capture empty: "+count);
                    Plugin.Logger.Info("PASS native glove depth capture: "+count+" pixels");
                }
            }
            using(var panel=new OverlayCanvas("Hand layering fixture",8,8,1,false,target.Device))
            {
                panel.Clear(System.Drawing.Color.FromArgb(255,12,65,87)); panel.Upload();
                using(var texture=new ShaderResourceView(target.Device,panel.Texture))
                    NativeSprites.Draw(target,new[] {new NativeSprite(null,new RectangleF(0,0,target.Description.Width,target.Description.Height),Vector4.One) {Texture=texture}},handDepth:NativeHandLayer.Depth);
                UiTests.Save(target,Path.Combine(output,"native-hand-over-panel.png"));
            }
            using(var panel=new OverlayCanvas("Native menu layering fixture",1920,1080,1,false,target.Device))
            using(var menu=System.Drawing.Image.FromFile(Path.Combine(output,"settings-native-44.png")))
            {
                panel.Graphics.DrawImage(menu,0,0,1920,1080); panel.Upload();
                using(var texture=new ShaderResourceView(target.Device,panel.Texture))
                    FloatingMenu.DrawPanel(target,texture,new FloatingMenu.Snapshot {
                        Pose=(Matrix)(MatrixD.CreateTranslation(0,0,-.5)*MatrixD.Invert(view)),Width=1.2f,Height=.675f
                    },view,projection,NativeHandLayer.Depth);
                UiTests.Save(target,Path.Combine(output,"native-menu-hand-layer.png"));
            }
            NativeHandLayer.Reset(); NativeHandLayer.Actor=uint.MaxValue;
        }
    }
}
