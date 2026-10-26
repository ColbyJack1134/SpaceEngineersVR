using System;
using Valve.VR;
#if LINUX
using System.Text;
using SpaceEngineersVR.Plugin;
using SpaceEngineersVR.Wrappers;
#endif

namespace SpaceEngineersVR.Player
{
    internal static class VrSubmit
    {
        internal static EVRCompositorError Eye(EVREye eye,IntPtr texture,EColorSpace colorSpace,ref VRTextureBounds_t bounds)
        {
#if LINUX
            var b=bounds;
            var result=Dxvk.With(texture,colorSpace,input => OpenVR.Compositor.Submit(eye,ref input,ref b,EVRSubmitFlags.Submit_Default));
            Dxvk.Submitted();
            return result;
#else
            var input=new Texture_t { eColorSpace=colorSpace,eType=ETextureType.DirectX,handle=texture };
            return OpenVR.Compositor.Submit(eye,ref input,ref bounds,EVRSubmitFlags.Submit_Default);
#endif
        }

        internal static EVROverlayError Overlay(ulong handle,IntPtr texture,EColorSpace colorSpace)
        {
#if LINUX
            return Dxvk.With(texture,colorSpace,input => OpenVR.Overlay.SetOverlayTexture(handle,ref input));
#else
            var input=new Texture_t { eColorSpace=colorSpace,eType=ETextureType.DirectX,handle=texture };
            return OpenVR.Overlay.SetOverlayTexture(handle,ref input);
#endif
        }

        internal static void WaitGetPoses(TrackedDevicePose_t[] render,TrackedDevicePose_t[] game)
        {
#if LINUX
            Dxvk.WaitGetPoses(render,game);
#else
            OpenVR.Compositor.WaitGetPoses(render,game);
#endif
        }

        internal static void PostPresentHandoff()
        {
#if LINUX
            Dxvk.PostPresentHandoff();
#else
            OpenVR.Compositor.PostPresentHandoff();
#endif
        }

#if LINUX
        // SteamVR on Linux takes only Vulkan images and copies on DXVK's queue, so calls hold DXVK's queue lock with explicit timing, as Proton's vrclient does.
        private static unsafe class Dxvk
        {
            private static readonly Guid SurfaceId=new Guid("5546cf8c-77e7-4341-b05d-8d4d5000e77d");
            private static readonly Guid DeviceId=new Guid("e2ef5fa5-dc21-4af7-90c4-f67ef6a09323");
            private const int TransferSource=6,ImageCreateInfo=14;
            private struct ImageInfo
            {
                public int Type; public IntPtr Next; public uint Flags; public int ImageType; public int Format;
                public uint Width,Height,Depth,MipLevels,ArrayLayers; public int Samples,Tiling; public uint Usage;
                public int SharingMode; public uint QueueFamilyCount; public IntPtr QueueFamilies; public int InitialLayout;
            }
            private struct Range { public uint Aspect,BaseMip,Levels,BaseLayer,Layers; }

            private static IntPtr d3dDevice,device,instance,physical,vkDevice,queue;
            private static uint family;

            private static IntPtr Slot(IntPtr com,int index) => (*(IntPtr**)com)[index];
            private static int Query(IntPtr com,Guid id,out IntPtr result)
            {
                IntPtr found;
                int hr=((delegate* unmanaged<IntPtr,Guid*,IntPtr*,int>)Slot(com,0))(com,&id,&found);
                result=found;
                return hr;
            }
            private static void Release(IntPtr com) => ((delegate* unmanaged<IntPtr,uint>)Slot(com,2))(com);
            private static void Call(int slot) => ((delegate* unmanaged<IntPtr,void>)Slot(device,slot))(device);
            private static void Flush() => Call(6);
            private static void Lock() => Call(7);
            private static void Unlock() => Call(8);
            private static void Transition(IntPtr surface,Range* range,int from,int to) =>
                ((delegate* unmanaged<IntPtr,IntPtr,Range*,int,int,void>)Slot(device,5))(device,surface,range,from,to);

            private static bool Ready()
            {
                var d3d=MyRender11.DeviceInstance?.NativePointer ?? IntPtr.Zero;
                if(d3d==IntPtr.Zero) return false;
                if(d3d==d3dDevice) return true;
                if(device!=IntPtr.Zero) { Release(device); device=IntPtr.Zero; }
                if(Query(d3d,DeviceId,out IntPtr interop)<0)
                    throw new InvalidOperationException("The game's Direct3D 11 device is not DXVK; VR on Linux needs linux-compat's DXVK");
                IntPtr i,p,d,q; uint f;
                ((delegate* unmanaged<IntPtr,IntPtr*,IntPtr*,IntPtr*,void>)Slot(interop,3))(interop,&i,&p,&d);
                ((delegate* unmanaged<IntPtr,IntPtr*,uint*,void>)Slot(interop,4))(interop,&q,&f);
                instance=i; physical=p; vkDevice=d; queue=q; family=f; device=interop; d3dDevice=d3d;
                PatchVrClientVulkanNames();
                // SteamVR's Linux vrclient resolves core-promoted Vulkan functions by their KHR-suffixed
                // names, but the Vulkan loader only returns those when the extension was enabled at device
                // creation. linux-compat's DXVK device does not enable VK_KHR_get_memory_requirements2, so
                // vrclient caches NULL entry points for vkGetImage/BufferMemoryRequirements2KHR and later
                // crashes calling one while allocating its shared copy texture. The plain core functions
                // exist on every Vulkan 1.1+ device, so the query names are rewritten in vrclient's
                // read-only data before the first submission can cache the NULL pointers.
                OpenVR.Compositor.SetExplicitTimingMode(EVRCompositorTimingMode.Implicit);
                var required=new StringBuilder(4096);
                OpenVR.Compositor.GetVulkanInstanceExtensionsRequired(required,(uint)required.Capacity);
                Logger.Info("SteamVR Vulkan instance extensions: "+required);
                required.Clear();
                OpenVR.Compositor.GetVulkanDeviceExtensionsRequired(physical,required,(uint)required.Capacity);
                Logger.Info("SteamVR Vulkan device extensions: "+required);
                return true;
            }

            [System.Runtime.InteropServices.DllImport("libc",SetLastError=true)]
            private static extern int mprotect(IntPtr addr,UIntPtr len,int prot);

            private static unsafe void PatchVrClientVulkanNames()
            {
                try
                {
                    var replacements=new (string From,string To)[]
                    {
                        ("vkGetImageMemoryRequirements2KHR","vkGetImageMemoryRequirements2"),
                        ("vkGetBufferMemoryRequirements2KHR","vkGetBufferMemoryRequirements2"),
                    };
                    foreach(var line in System.IO.File.ReadAllLines("/proc/self/maps"))
                    {
                        if(!line.Contains("vrclient.so")) continue;
                        var fields=line.Split(new[]{' '},StringSplitOptions.RemoveEmptyEntries);
                        if(fields.Length<5) continue;
                        string perms=fields[1];
                        if(perms.Length<3||perms[0]!='r') continue;
                        var range=fields[0].Split('-');
                        ulong start=Convert.ToUInt64(range[0],16),end=Convert.ToUInt64(range[1],16);
                        if(end<=start) continue;
                        int size=checked((int)(end-start));
                        byte* ptr=(byte*)start;
                        var snapshot=new byte[size];
                        System.Runtime.InteropServices.Marshal.Copy((IntPtr)ptr,snapshot,0,size);
                        var span=snapshot.AsSpan();
                        int prot=0;
                        if(perms[0]=='r') prot|=0x1;
                        if(perms[1]=='w') prot|=0x2;
                        if(perms[2]=='x') prot|=0x4;
                        foreach(var (from,to) in replacements)
                        {
                            var pattern=System.Text.Encoding.ASCII.GetBytes(from);
                            var patch=System.Text.Encoding.ASCII.GetBytes(to);
                            int offset=0;
                            while(offset+pattern.Length<=size)
                            {
                                int found=span.Slice(offset).IndexOf(pattern);
                                if(found<0) break;
                                found+=offset;
                                ulong pageStart=(ulong)(ptr+found)&~0xFFFUL;
                                ulong pageEnd=((ulong)(ptr+found)+(uint)pattern.Length+0xFFFUL)&~0xFFFUL;
                                UIntPtr pageLen=(UIntPtr)(pageEnd-pageStart);
                                if(mprotect((IntPtr)pageStart,pageLen,prot|0x2)!=0)
                                {
                                    Logger.Error("vrclient Vulkan name patch: mprotect failed for 0x"+pageStart.ToString("x"));
                                    offset=found+1;
                                    continue;
                                }
                                fixed(byte* src=patch)
                                {
                                    Buffer.MemoryCopy(src,ptr+found,patch.Length,patch.Length);
                                    for(int i=patch.Length;i<pattern.Length;i++) ptr[found+i]=0;
                                }
                                mprotect((IntPtr)pageStart,pageLen,prot);
                                Logger.Info("vrclient Vulkan entry point name patched: "+from+" -> "+to);
                                offset=found+1;
                            }
                        }
                    }
                }
                catch(Exception e) { Logger.Error("vrclient Vulkan name patch failed: "+e.Message); }
            }

            internal static T With<T>(IntPtr texture,EColorSpace colorSpace,Func<Texture_t,T> call)
            {
                if(!Ready()) throw new InvalidOperationException("VR texture submitted before the render device exists");
                if(Query(texture,SurfaceId,out IntPtr surface)<0) throw new InvalidOperationException("VR texture is not a DXVK surface");
                try
                {
                    ulong image; int layout;
                    var info=new ImageInfo { Type=ImageCreateInfo };
                    int hr=((delegate* unmanaged<IntPtr,ulong*,int*,ImageInfo*,int>)Slot(surface,4))(surface,&image,&layout,&info);
                    if(hr<0) throw new InvalidOperationException("DXVK image info failed: 0x"+hr.ToString("x8"));
                    var data=new VRVulkanTextureData_t { m_nImage=image,m_pDevice=vkDevice,m_pPhysicalDevice=physical,m_pInstance=instance,
                        m_pQueue=queue,m_nQueueFamilyIndex=family,m_nWidth=info.Width,m_nHeight=info.Height,m_nFormat=(uint)info.Format,
                        m_nSampleCount=(uint)info.Samples };
                    var range=new Range { Aspect=1,Levels=info.MipLevels,Layers=info.ArrayLayers };
                    Transition(surface,&range,layout,TransferSource);
                    Flush();
                    Lock();
                    try { return call(new Texture_t { handle=(IntPtr)(&data),eType=ETextureType.Vulkan,eColorSpace=colorSpace }); }
                    finally { Unlock(); Transition(surface,&range,TransferSource,layout); }
                }
                finally { Release(surface); }
            }

            internal static void WaitGetPoses(TrackedDevicePose_t[] render,TrackedDevicePose_t[] game)
            {
                if(!Ready()) { OpenVR.Compositor.WaitGetPoses(render,game); return; }
                Lock();
                try { OpenVR.Compositor.WaitGetPoses(render,game); }
                finally { Unlock(); }
            }

            internal static void PostPresentHandoff()
            {
                // No explicit timing mode: PostPresentHandoff must not be called outside it.
            }

            // Explicit timing is disabled, so nothing tracks a pending handoff.
            internal static void Submitted() { }
        }
#endif
    }
}
