using System;
using System.IO;
using SharpDX;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using SharpDX.Mathematics.Interop;
using SpaceEngineersVR.Player;
using Valve.VR;
using Buffer = SharpDX.Direct3D11.Buffer;
using Device = SharpDX.Direct3D11.Device;
using Resource = SharpDX.Direct3D11.Resource;

namespace SpaceEngineersVR.Diagnostics
{
    internal static class HiddenAreaTests
    {
        private const int Size=32;
        public static void Run(Action<string> log)
        {
            var corners=HiddenAreaMask.Triangles(new[] { new HmdVector2_t { v0=0,v1=0 },new HmdVector2_t { v0=1,v1=1 } });
            if(corners[0].X!=-1 || corners[0].Y!=1 || corners[1].X!=1 || corners[1].Y!=-1) throw new Exception("Hidden-area UV is not mapped to clip space");
            if(HiddenAreaMask.PatchHistogram("weight *= depthWeight;")!=null) throw new Exception("Unrecognized histogram shader was accepted");
            string shaders=Path.GetFullPath(Path.Combine(Path.GetDirectoryName(typeof(VRageRender.MyRenderProxy).Assembly.Location),"..","Content","Shaders"));
            using(var device=new Device(DriverType.Warp))
            {
                // Left half of the target is hidden.
                var left=new[] { new RawVector2(-1,1),new RawVector2(0,1),new RawVector2(-1,-1),new RawVector2(0,1),new RawVector2(0,-1),new RawVector2(-1,-1) };
                HiddenAreaMask.Create(device,new[] { left,left },HiddenAreaMask.CompileHistograms(device,shaders),true);
                Depth(device);
                Exposure(device);
            }
            log("PASS hidden-area mask: clip-space mesh, exact near-depth mark and clear restore, installed histogram shader patch, hidden pixels carry no exposure weight");
        }

        private static void Depth(Device device)
        {
            using(var texture=new Texture2D(device,new Texture2DDescription { Width=Size,Height=Size,MipLevels=1,ArraySize=1,Format=Format.R32_Typeless,
                SampleDescription=new SampleDescription(1,0),Usage=ResourceUsage.Default,BindFlags=BindFlags.DepthStencil }))
            using(var view=new DepthStencilView(device,texture,new DepthStencilViewDescription { Format=Format.D32_Float,Dimension=DepthStencilViewDimension.Texture2D }))
            {
                var context=device.ImmediateContext;
                context.ClearDepthStencilView(view,DepthStencilClearFlags.Depth,.25f,0);
                HiddenAreaMask.DrawDepth(context,view,0,1);
                var marked=Read<float>(device,texture);
                for(int y=0;y<Size;y++) for(int x=0;x<Size;x++)
                    if(marked[y*Size+x]!=(x<Size/2 ? 1:.25f)) throw new Exception($"Hidden-area depth mark wrong at {x},{y}: {marked[y*Size+x]}");
                HiddenAreaMask.DrawDepth(context,view,0,0);
                var restored=Read<float>(device,texture);
                for(int y=0;y<Size;y++) for(int x=0;x<Size;x++)
                    if(restored[y*Size+x]!=(x<Size/2 ? 0:.25f)) throw new Exception($"Hidden-area depth restore wrong at {x},{y}: {restored[y*Size+x]}");
            }
        }

        private static void Exposure(Device device)
        {
            var context=device.ImmediateContext;
            var gray=new byte[Size*Size*4];
            for(int i=0;i<gray.Length;i++) gray[i]=128;
            var near=new float[Size*Size*4];
            for(int i=0;i<near.Length;i++) near[i]=1;
            using(var source=Texture(device,Format.R8G8B8A8_UNorm,Size,gray))
            using(var depth=Texture(device,Format.R32_Float,Size*2,near))
            using(var sourceView=new ShaderResourceView(device,source))
            using(var depthView=new ShaderResourceView(device,depth))
            using(var histogram=new Buffer(device,new BufferDescription(64*4,ResourceUsage.Default,BindFlags.UnorderedAccess,CpuAccessFlags.None,ResourceOptionFlags.BufferStructured,4)))
            using(var uav=new UnorderedAccessView(device,histogram))
            using(var constants=Buffer.Create(device,BindFlags.ConstantBuffer,new[] { 1/12f,8/12f,12,-8, 1/256f,256,.25f,.98f, Size,Size,1f/(Size*Size),0, 1,0,0,0 }))
            {
                context.ClearUnorderedAccessView(uav,new RawInt4());
                HiddenAreaMask.Histogram(context,0,false,constants,uav,sourceView,depthView,Size/16,Size/16,1);
                long total=0;
                foreach(uint bin in Read<uint>(device,histogram)) total+=bin;
                if(total!=Size*Size/2) throw new Exception("Histogram counted "+total+" pixels; expected only the visible "+Size*Size/2);
            }
        }

        private static Texture2D Texture<T>(Device device,Format format,int size,T[] data) where T:struct
        {
            var handle=System.Runtime.InteropServices.GCHandle.Alloc(data,System.Runtime.InteropServices.GCHandleType.Pinned);
            try
            {
                return new Texture2D(device,new Texture2DDescription { Width=size,Height=size,MipLevels=1,ArraySize=1,Format=format,SampleDescription=new SampleDescription(1,0),
                    Usage=ResourceUsage.Immutable,BindFlags=BindFlags.ShaderResource },new DataRectangle(handle.AddrOfPinnedObject(),size*FormatHelper.SizeOfInBytes(format)));
            }
            finally { handle.Free(); }
        }

        private static T[] Read<T>(Device device,Resource resource) where T:struct
        {
            var context=device.ImmediateContext;
            Resource staging;
            int count;
            if(resource is Texture2D texture)
            {
                var d=texture.Description;
                d.Usage=ResourceUsage.Staging; d.BindFlags=BindFlags.None; d.CpuAccessFlags=CpuAccessFlags.Read;
                staging=new Texture2D(device,d); count=d.Width*d.Height;
            }
            else
            {
                var d=((Buffer)resource).Description;
                d.Usage=ResourceUsage.Staging; d.BindFlags=BindFlags.None; d.CpuAccessFlags=CpuAccessFlags.Read;
                d.OptionFlags=ResourceOptionFlags.None; d.StructureByteStride=0;
                staging=new Buffer(device,d); count=d.SizeInBytes/4;
            }
            using(staging)
            {
                context.CopyResource(resource,staging);
                var box=context.MapSubresource(staging,0,MapMode.Read,SharpDX.Direct3D11.MapFlags.None);
                try
                {
                    var result=new T[count];
                    int width=resource is Texture2D t ? t.Description.Width : count;
                    for(int row=0;row<count/width;row++) Utilities.Read(box.DataPointer+row*box.RowPitch,result,row*width,width);
                    return result;
                }
                finally { context.UnmapSubresource(staging,0); }
            }
        }
    }
}
