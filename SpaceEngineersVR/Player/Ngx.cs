#if !LINUX
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using SharpDX.Direct3D11;
using Device=SharpDX.Direct3D11.Device;
using Resource=SharpDX.Direct3D11.Resource;

namespace SpaceEngineersVR.Player
{
    // NVIDIA NGX D3D11 through the driver's own _nvngx.dll, after SE-DLSS (MIT, see licenses/SE-DLSS-LICENSE); every failure is a return value.
    internal static class Ngx
    {
        internal enum Verdict { Unknown, Supported, NeedsDriver, Unavailable, Failed }
        private const int VersionApi=0x15,EngineTypeCustom=0,FeatureSuperSampling=1;
        // NVSDK_NGX_PerfQuality_Value.
        internal const int QualityMaxPerf=0,QualityBalanced=1,QualityMax=2,QualityDlaa=5;
        private const int FlagMvLowRes=1<<1,FlagDepthInverted=1<<3,FlagAutoExposure=1<<6;
        private const string ProjectId="6d2f0c9e-4b3a-4e8f-9a51-2c7d8e1f0b63";
        private static NgxDriver driver;
        private static NgxFeatureInfo info;
        private static NgxParameter capabilities,parameters;
        private static Device device;
        private static IntPtr devicePointer,projectId,engineVersion,logPath;
        internal static bool Initialized { get; private set; }
        internal static Verdict Support { get; private set; }
        internal static string LastError { get; private set; }="";
        internal static string DriverPath => driver?.LoadedFrom ?? "";

        private static bool Failed(int result)
        {
            uint u=(uint)result;
            return u!=1 && ((u&0x80000000u)!=0 || (u&0xFFF00000u)==0xBAD00000u);
        }
        private static string Hex(int value) => "0x"+((uint)value).ToString("X8");

        // runtimeDirectory holds nvngx_dlss.dll; the feature info path list tells the driver core where to find it.
        internal static Verdict Init(Device target,string runtimeDirectory,string logDirectory)
        {
            if(Initialized) return Support;
            Support=Verdict.Unknown;
            if(target==null || target.IsDisposed) return Fail(Verdict.Failed,"no D3D11 device");
            NgxDriver.Preload(Path.Combine(runtimeDirectory,Dlss.RuntimeFile));
            driver=new NgxDriver();
            string error;
            if(!driver.TryLoad(out error)) { driver=null; return Fail(Verdict.Unavailable,error); }
            device=target; devicePointer=target.NativePointer;
            var paths=new List<string> {runtimeDirectory};
            string core=NgxDriver.CoreDirectory();
            if(!string.IsNullOrEmpty(core)) paths.Add(core);
            info=new NgxFeatureInfo(paths);
            projectId=Marshal.StringToHGlobalAnsi(ProjectId);
            engineVersion=Marshal.StringToHGlobalAnsi(Plugin.Common.Version.ToString());
            logPath=Marshal.StringToHGlobalUni(string.IsNullOrEmpty(logDirectory) ? "." : logDirectory);
            int result;
            try { result=driver.Init(projectId,EngineTypeCustom,engineVersion,logPath,devicePointer,VersionApi,info.Pointer); }
            catch(Exception ex) { Teardown(false); return Fail(Verdict.Failed,"NGX init threw "+ex.GetType().Name+": "+ex.Message); }
            if(Failed(result)) { Teardown(false); return Fail(Verdict.Failed,"NGX init failed "+Hex(result)+NgxLog.Tail()); }
            IntPtr caps;
            result=driver.GetCapabilityParameters(out caps);
            if(Failed(result) || caps==IntPtr.Zero) { Teardown(true); return Fail(Verdict.Failed,"NGX capabilities failed "+Hex(result)); }
            capabilities=NgxParameter.FromNative(caps);
            IntPtr eval;
            if(driver.AllocateParameters!=null && !Failed(driver.AllocateParameters(out eval)) && eval!=IntPtr.Zero) parameters=NgxParameter.FromNative(eval);
            int needsDriver,available,initResult,major,minor;
            bool hasNeedsDriver=capabilities.TryGet(NgxNames.SuperSamplingNeedsUpdatedDriver,out needsDriver);
            bool hasAvailable=capabilities.TryGet(NgxNames.SuperSamplingAvailable,out available);
            bool hasInitResult=capabilities.TryGet(NgxNames.SuperSamplingFeatureInitResult,out initResult);
            capabilities.TryGet(NgxNames.SuperSamplingMinDriverVersionMajor,out major); capabilities.TryGet(NgxNames.SuperSamplingMinDriverVersionMinor,out minor);
            if(hasNeedsDriver && needsDriver!=0) { Teardown(true); return Fail(Verdict.NeedsDriver,"NGX needs NVIDIA driver "+major+"."+minor+" or newer"); }
            if(hasInitResult && Failed(initResult)) { Teardown(true); return Fail(Verdict.Unavailable,"NGX denied Super Sampling "+Hex(initResult)); }
            if(!hasAvailable || available==0) { Teardown(true); return Fail(Verdict.Unavailable,"NGX Super Sampling is not available on this GPU"); }
            Initialized=true; Support=Verdict.Supported; LastError="";
            return Support;
        }
        private static Verdict Fail(Verdict verdict,string error) { Support=verdict; LastError=error; return verdict; }

        // Transformer preset K is the quality default; CNN preset F costs about 40 percent of it per pixel.
        internal const uint PresetK=11,PresetF=6;
        // One feature per view, created at the largest render size it will see; evaluates pass the current render subrect.
        internal static IntPtr CreateFeature(int width,int height,int outWidth,int outHeight,int quality=QualityDlaa,uint preset=PresetK)
        {
            if(!Initialized) { LastError="NGX is not initialized"; return IntPtr.Zero; }
            var p=parameters ?? capabilities;
            p.Reset();
            p.Set(NgxNames.Width,(uint)width); p.Set(NgxNames.Height,(uint)height);
            p.Set(NgxNames.OutWidth,(uint)outWidth); p.Set(NgxNames.OutHeight,(uint)outHeight);
            p.Set(NgxNames.PerfQualityValue,quality);
            foreach(var hint in NgxNames.PresetHints) p.Set(hint,preset);
            p.Set(NgxNames.CreateFlags,FlagDepthInverted|FlagAutoExposure|FlagMvLowRes);
            p.Set(NgxNames.EnableOutputSubrects,0);
            IntPtr handle;
            int result=driver.CreateFeature(Context(),FeatureSuperSampling,p.Pointer,out handle);
            if(Failed(result) || handle==IntPtr.Zero) { LastError="CreateFeature failed "+Hex(result)+NgxLog.Tail(); return IntPtr.Zero; }
            return handle;
        }
        internal static void ReleaseFeature(ref IntPtr handle)
        {
            if(handle==IntPtr.Zero) return;
            if(driver?.ReleaseFeature!=null) { try { driver.ReleaseFeature(handle); } catch { } }
            handle=IntPtr.Zero;
        }

        // Motion vectors are unjittered pixel offsets from this frame to the previous one at render size.
        internal static bool Evaluate(IntPtr feature,Resource color,Resource depth,Resource motion,Resource output,float jitterX,float jitterY,bool reset,int width,int height)
        {
            if(!Initialized || feature==IntPtr.Zero) { LastError="NGX feature missing"; return false; }
            var p=parameters ?? capabilities;
            p.Reset();
            p.SetResource(NgxNames.Color,color.NativePointer);
            p.SetResource(NgxNames.Output,output.NativePointer);
            p.SetResource(NgxNames.Depth,depth.NativePointer);
            p.SetResource(NgxNames.MotionVectors,motion.NativePointer);
            p.Set(NgxNames.JitterOffsetX,jitterX); p.Set(NgxNames.JitterOffsetY,jitterY);
            p.Set(NgxNames.Sharpness,0f);
            p.Set(NgxNames.Reset,reset ? 1:0);
            p.Set(NgxNames.MvScaleX,1f); p.Set(NgxNames.MvScaleY,1f);
            p.Set(NgxNames.RenderSubrectWidth,(uint)width); p.Set(NgxNames.RenderSubrectHeight,(uint)height);
            int result=driver.EvaluateFeature(Context(),feature,p.Pointer,IntPtr.Zero);
            if(Failed(result)) { LastError="EvaluateFeature failed "+Hex(result)+NgxLog.Tail(); return false; }
            return true;
        }
        private static IntPtr Context() => device.ImmediateContext.NativePointer;

        // Forgets the session without calling into the driver; used after the renderer device was recreated.
        internal static void Forget() => Teardown(false);
        private static void Teardown(bool shutdown)
        {
            if(parameters!=null && driver?.DestroyParameters!=null) { try { driver.DestroyParameters(parameters.Pointer); } catch { } }
            if(shutdown && driver?.Shutdown1!=null && devicePointer!=IntPtr.Zero) { try { driver.Shutdown1(devicePointer); } catch { } }
            Initialized=false; capabilities=parameters=null; device=null; devicePointer=IntPtr.Zero;
            driver?.Dispose(); driver=null;
            info?.Dispose(); info=null;
            Free(ref projectId); Free(ref engineVersion); Free(ref logPath);
        }
        private static void Free(ref IntPtr pointer) { if(pointer!=IntPtr.Zero) Marshal.FreeHGlobal(pointer); pointer=IntPtr.Zero; }
    }

    internal sealed class NgxDriver : IDisposable
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int InitWithProjectIdFn(IntPtr projectId,int engineType,IntPtr engineVersion,IntPtr logPath,IntPtr device,IntPtr info,int version);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int ShutdownFn(IntPtr device);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int ParametersFn(out IntPtr parameters);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int DestroyParametersFn(IntPtr parameters);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int CreateFeatureFn(IntPtr context,int feature,IntPtr parameters,out IntPtr handle);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int ReleaseFeatureFn(IntPtr handle);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int EvaluateFn(IntPtr context,IntPtr handle,IntPtr parameters,IntPtr callback);
        private const uint LoadWithAlteredSearchPath=8;
        private const ulong SteamAppId=244850;
        private IntPtr module,initProject,initExt,initApp;
        private InitWithProjectIdFn initWithProject;
        internal string LoadedFrom { get; private set; }
        internal ShutdownFn Shutdown1 { get; private set; }
        internal ParametersFn GetCapabilityParameters { get; private set; }
        internal ParametersFn AllocateParameters { get; private set; }
        internal DestroyParametersFn DestroyParameters { get; private set; }
        internal CreateFeatureFn CreateFeature { get; private set; }
        internal ReleaseFeatureFn ReleaseFeature { get; private set; }
        internal EvaluateFn EvaluateFeature { get; private set; }

        internal bool TryLoad(out string error)
        {
            error=null;
            int lastError=0;
            foreach(string path in Candidates())
            {
                module=Load(path);
                if(module!=IntPtr.Zero) { LoadedFrom=path; break; }
                int code=Marshal.GetLastWin32Error();
                if(code!=0) lastError=code;
            }
            if(module==IntPtr.Zero) { error="NVIDIA NGX core (_nvngx.dll) not found (Win32 "+lastError+")"; return false; }
            initProject=GetProcAddress(module,"NVSDK_NGX_D3D11_Init_ProjectID");
            initExt=GetProcAddress(module,"NVSDK_NGX_D3D11_Init_Ext");
            initApp=GetProcAddress(module,"NVSDK_NGX_D3D11_Init");
            IntPtr sdk=GetProcAddress(module,"NVSDK_NGX_D3D11_Init_with_ProjectID");
            if(sdk!=IntPtr.Zero) initWithProject=Marshal.GetDelegateForFunctionPointer<InitWithProjectIdFn>(sdk);
            Shutdown1=Export<ShutdownFn>("NVSDK_NGX_D3D11_Shutdown1");
            GetCapabilityParameters=Export<ParametersFn>("NVSDK_NGX_D3D11_GetCapabilityParameters");
            AllocateParameters=Export<ParametersFn>("NVSDK_NGX_D3D11_AllocateParameters");
            DestroyParameters=Export<DestroyParametersFn>("NVSDK_NGX_D3D11_DestroyParameters");
            CreateFeature=Export<CreateFeatureFn>("NVSDK_NGX_D3D11_CreateFeature");
            ReleaseFeature=Export<ReleaseFeatureFn>("NVSDK_NGX_D3D11_ReleaseFeature");
            EvaluateFeature=Export<EvaluateFn>("NVSDK_NGX_D3D11_EvaluateFeature") ?? Export<EvaluateFn>("NVSDK_NGX_D3D11_EvaluateFeature_C");
            bool hasInit=initProject!=IntPtr.Zero || initWithProject!=null || initExt!=IntPtr.Zero || initApp!=IntPtr.Zero;
            if(hasInit && Shutdown1!=null && GetCapabilityParameters!=null && CreateFeature!=null && ReleaseFeature!=null && EvaluateFeature!=null) return true;
            error="NGX D3D11 exports missing in "+LoadedFrom;
            Dispose();
            return false;
        }

        // The driver rejects callers whose return address has no module, so the private init exports go through OleAut32 (NgxInvoke).
        internal int Init(IntPtr projectId,int engineType,IntPtr engineVersion,IntPtr logPath,IntPtr device,int version,IntPtr info)
        {
            if(initProject!=IntPtr.Zero)
                return NgxInvoke.Call(initProject,new ushort[] {NgxInvoke.U8,NgxInvoke.I4,NgxInvoke.U8,NgxInvoke.U8,NgxInvoke.U8,NgxInvoke.U4,NgxInvoke.U8},
                    new[] {projectId.ToInt64(),engineType,engineVersion.ToInt64(),logPath.ToInt64(),device.ToInt64(),unchecked((uint)version),info.ToInt64()});
            if(initWithProject!=null) return initWithProject(projectId,engineType,engineVersion,logPath,device,info,version);
            if(initExt!=IntPtr.Zero)
                return NgxInvoke.Call(initExt,new ushort[] {NgxInvoke.U8,NgxInvoke.U8,NgxInvoke.U8,NgxInvoke.U4,NgxInvoke.U8},
                    new[] {unchecked((long)SteamAppId),logPath.ToInt64(),device.ToInt64(),unchecked((uint)version),info.ToInt64()});
            return NgxInvoke.Call(initApp,new ushort[] {NgxInvoke.U8,NgxInvoke.U8,NgxInvoke.U8,NgxInvoke.U4},
                new[] {unchecked((long)SteamAppId),logPath.ToInt64(),device.ToInt64(),unchecked((uint)version)});
        }

        private T Export<T>(string name) where T : class
        {
            IntPtr pointer=GetProcAddress(module,name);
            return pointer==IntPtr.Zero ? null:Marshal.GetDelegateForFunctionPointer<T>(pointer);
        }
        internal static void Preload(string path) { if(File.Exists(path)) Load(path); }
        internal static string CoreDirectory()
        {
            try
            {
                using(var hive=Microsoft.Win32.RegistryKey.OpenBaseKey(Microsoft.Win32.RegistryHive.LocalMachine,Microsoft.Win32.RegistryView.Registry64))
                using(var key=hive.OpenSubKey(@"SOFTWARE\NVIDIA Corporation\Global\NGXCore"))
                    return key?.GetValue("FullPath") as string;
            }
            catch { return null; }
        }
        private static IEnumerable<string> Candidates()
        {
            string system=Environment.GetFolderPath(Environment.SpecialFolder.System);
            if(!string.IsNullOrEmpty(system)) { yield return Path.Combine(system,"_nvngx.dll"); yield return Path.Combine(system,"nvngx.dll"); }
            string core=CoreDirectory();
            if(!string.IsNullOrEmpty(core)) { yield return Path.Combine(core,"_nvngx.dll"); yield return Path.Combine(core,"nvngx.dll"); }
            string[] stores=new string[0];
            string repository=string.IsNullOrEmpty(system) ? null:Path.Combine(system,"DriverStore","FileRepository");
            try { if(repository!=null && Directory.Exists(repository)) stores=Directory.GetDirectories(repository,"nv*"); } catch { }
            foreach(string store in stores) yield return Path.Combine(store,"_nvngx.dll");
        }
        private static IntPtr Load(string path)
        {
            try { if(!File.Exists(path)) return IntPtr.Zero; } catch { return IntPtr.Zero; }
            IntPtr module=LoadLibraryEx(path,IntPtr.Zero,LoadWithAlteredSearchPath);
            return module!=IntPtr.Zero ? module:LoadLibrary(path);
        }
        public void Dispose()
        {
            initProject=initExt=initApp=IntPtr.Zero; initWithProject=null;
            Shutdown1=null; GetCapabilityParameters=AllocateParameters=null; DestroyParameters=null; CreateFeature=null; ReleaseFeature=null; EvaluateFeature=null;
            if(module!=IntPtr.Zero) FreeLibrary(module);
            module=IntPtr.Zero;
        }
        [DllImport("kernel32",CharSet=CharSet.Unicode,SetLastError=true)] private static extern IntPtr LoadLibrary(string path);
        [DllImport("kernel32",CharSet=CharSet.Unicode,SetLastError=true)] private static extern IntPtr LoadLibraryEx(string path,IntPtr file,uint flags);
        [DllImport("kernel32",CharSet=CharSet.Ansi,SetLastError=true)] private static extern IntPtr GetProcAddress(IntPtr module,string name);
        [DllImport("kernel32",SetLastError=true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool FreeLibrary(IntPtr module);
    }

    // Calls a cdecl export through OleAut32 DispCallFunc so the callee sees a PE-backed return address.
    internal static class NgxInvoke
    {
        internal const ushort I4=3,U4=19,U8=21;
        private const int Cdecl=1,VariantSize=24,ValueOffset=8;
        internal static int Call(IntPtr function,ushort[] types,long[] arguments)
        {
            if(function==IntPtr.Zero) throw new ArgumentNullException(nameof(function));
            if(IntPtr.Size!=8) throw new PlatformNotSupportedException("NGX requires a 64-bit process");
            IntPtr storage=Marshal.AllocHGlobal(VariantSize*(arguments.Length+1));
            var pointers=new IntPtr[arguments.Length];
            try
            {
                for(int i=0;i<VariantSize*(arguments.Length+1);i++) Marshal.WriteByte(storage,i,0);
                for(int i=0;i<arguments.Length;i++)
                {
                    pointers[i]=IntPtr.Add(storage,i*VariantSize);
                    Marshal.WriteInt64(pointers[i],ValueOffset,arguments[i]);
                }
                IntPtr result=IntPtr.Add(storage,arguments.Length*VariantSize);
                int hr=DispCallFunc(IntPtr.Zero,new UIntPtr(unchecked((ulong)function.ToInt64())),Cdecl,I4,(uint)arguments.Length,types,pointers,result);
                if(hr!=0) throw new ExternalException("DispCallFunc failed",hr);
                return Marshal.ReadInt32(result,ValueOffset);
            }
            finally { Marshal.FreeHGlobal(storage); }
        }
        [DllImport("oleaut32.dll",ExactSpelling=true)]
        private static extern int DispCallFunc(IntPtr instance,UIntPtr function,int convention,ushort returnType,uint count,[In] ushort[] types,[In] IntPtr[] arguments,IntPtr result);
    }

    // MSVC vtable order: Set void*, D3D12, D3D11, int, uint, double, float, ull; Get in the same order; then Reset.
    internal sealed class NgxParameter
    {
        [UnmanagedFunctionPointer(CallingConvention.ThisCall)] private delegate void SetPointerFn(IntPtr self,IntPtr name,IntPtr value);
        [UnmanagedFunctionPointer(CallingConvention.ThisCall)] private delegate void SetIntFn(IntPtr self,IntPtr name,int value);
        [UnmanagedFunctionPointer(CallingConvention.ThisCall)] private delegate void SetUIntFn(IntPtr self,IntPtr name,uint value);
        [UnmanagedFunctionPointer(CallingConvention.ThisCall)] private delegate void SetFloatFn(IntPtr self,IntPtr name,float value);
        [UnmanagedFunctionPointer(CallingConvention.ThisCall)] private delegate int GetIntFn(IntPtr self,IntPtr name,out int value);
        [UnmanagedFunctionPointer(CallingConvention.ThisCall)] private delegate void ResetFn(IntPtr self);
        private static readonly Dictionary<IntPtr,Table> tables=new Dictionary<IntPtr,Table>();
        private sealed class Table { public SetPointerFn SetD3D11; public SetIntFn SetInt; public SetUIntFn SetUInt; public SetFloatFn SetFloat; public GetIntFn GetInt; public ResetFn Reset; }
        private readonly Table table;
        internal IntPtr Pointer { get; }
        private NgxParameter(IntPtr self,Table table) { Pointer=self; this.table=table; }
        internal static NgxParameter FromNative(IntPtr self)
        {
            IntPtr vtable=Marshal.ReadIntPtr(self);
            Table table;
            if(!tables.TryGetValue(vtable,out table))
            {
                tables[vtable]=table=new Table
                {
                    SetD3D11=Slot<SetPointerFn>(vtable,2),SetInt=Slot<SetIntFn>(vtable,3),SetUInt=Slot<SetUIntFn>(vtable,4),
                    SetFloat=Slot<SetFloatFn>(vtable,6),GetInt=Slot<GetIntFn>(vtable,11),Reset=Slot<ResetFn>(vtable,16)
                };
            }
            return new NgxParameter(self,table);
        }
        private static T Slot<T>(IntPtr vtable,int index) where T : class => Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(vtable,index*IntPtr.Size));
        internal void SetResource(IntPtr name,IntPtr resource) => table.SetD3D11(Pointer,name,resource);
        internal void Set(IntPtr name,int value) => table.SetInt(Pointer,name,value);
        internal void Set(IntPtr name,uint value) => table.SetUInt(Pointer,name,value);
        internal void Set(IntPtr name,float value) => table.SetFloat(Pointer,name,value);
        internal bool TryGet(IntPtr name,out int value)
        {
            int result=table.GetInt(Pointer,name,out value);
            return result==1;
        }
        internal void Reset() => table.Reset(Pointer);
    }

    internal static class NgxNames
    {
        internal static readonly IntPtr Width=Alloc("Width"),Height=Alloc("Height"),OutWidth=Alloc("OutWidth"),OutHeight=Alloc("OutHeight"),
            Sharpness=Alloc("Sharpness"),Reset=Alloc("Reset"),Color=Alloc("Color"),Output=Alloc("Output"),Depth=Alloc("Depth"),MotionVectors=Alloc("MotionVectors"),
            JitterOffsetX=Alloc("Jitter.Offset.X"),JitterOffsetY=Alloc("Jitter.Offset.Y"),MvScaleX=Alloc("MV.Scale.X"),MvScaleY=Alloc("MV.Scale.Y"),
            PerfQualityValue=Alloc("PerfQualityValue"),CreateFlags=Alloc("DLSS.Feature.Create.Flags"),EnableOutputSubrects=Alloc("DLSS.Enable.Output.Subrects"),
            RenderSubrectWidth=Alloc("DLSS.Render.Subrect.Dimensions.Width"),RenderSubrectHeight=Alloc("DLSS.Render.Subrect.Dimensions.Height"),
            SuperSamplingAvailable=Alloc("SuperSampling.Available"),SuperSamplingNeedsUpdatedDriver=Alloc("SuperSampling.NeedsUpdatedDriver"),
            SuperSamplingMinDriverVersionMajor=Alloc("SuperSampling.MinDriverVersionMajor"),SuperSamplingMinDriverVersionMinor=Alloc("SuperSampling.MinDriverVersionMinor"),
            SuperSamplingFeatureInitResult=Alloc("SuperSampling.FeatureInitResult");
        internal static readonly IntPtr[] PresetHints={Alloc("DLSS.Hint.Render.Preset.DLAA"),Alloc("DLSS.Hint.Render.Preset.Quality"),Alloc("DLSS.Hint.Render.Preset.Balanced"),
            Alloc("DLSS.Hint.Render.Preset.Performance"),Alloc("DLSS.Hint.Render.Preset.UltraPerformance")};
        private static IntPtr Alloc(string name) => Marshal.StringToHGlobalAnsi(name);
    }

    // NVSDK_NGX_FeatureCommonInfo for MSVC x64: path list, internal data, logging callback.
    internal sealed class NgxFeatureInfo : IDisposable
    {
        private IntPtr blob,pathArray;
        private readonly List<IntPtr> strings=new List<IntPtr>();
        internal IntPtr Pointer => blob;
        internal NgxFeatureInfo(List<string> paths)
        {
            blob=Marshal.AllocHGlobal(256);
            for(int i=0;i<256;i++) Marshal.WriteByte(blob,i,0);
            pathArray=Marshal.AllocHGlobal(Math.Max(1,paths.Count)*IntPtr.Size);
            for(int i=0;i<paths.Count;i++)
            {
                IntPtr text=Marshal.StringToHGlobalUni(paths[i]);
                strings.Add(text);
                Marshal.WriteIntPtr(pathArray,i*IntPtr.Size,text);
            }
            Marshal.WriteIntPtr(blob,0,pathArray);
            Marshal.WriteInt32(blob,8,paths.Count);
            Marshal.WriteIntPtr(blob,24,NgxLog.Callback);
            Marshal.WriteInt32(blob,32,NgxLog.LevelOn);
            // DisableOtherLoggingSinks: the callback is the only sink, so the driver writes no log files.
            Marshal.WriteByte(blob,36,1);
        }
        public void Dispose()
        {
            foreach(IntPtr text in strings) Marshal.FreeHGlobal(text);
            strings.Clear();
            if(pathArray!=IntPtr.Zero) Marshal.FreeHGlobal(pathArray);
            if(blob!=IntPtr.Zero) Marshal.FreeHGlobal(blob);
            pathArray=blob=IntPtr.Zero;
        }
    }

    // Keeps the driver's last messages for failure logs; NGX may call from any thread.
    internal static class NgxLog
    {
        internal const int LevelOn=1;
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void AppLogCallback(IntPtr message,int level,int component);
        private static readonly AppLogCallback callback=OnLog;
        internal static readonly IntPtr Callback=Marshal.GetFunctionPointerForDelegate(callback);
        private static readonly Queue<string> recent=new Queue<string>();
        private static void OnLog(IntPtr message,int level,int component)
        {
            try
            {
                string text=message==IntPtr.Zero ? null:Marshal.PtrToStringAnsi(message)?.Trim();
                if(string.IsNullOrEmpty(text)) return;
                lock(recent) { if(recent.Count>=8) recent.Dequeue(); recent.Enqueue(text.Length>300 ? text.Substring(0,300):text); }
            }
            catch { }
        }
        internal static string Tail()
        {
            lock(recent) return recent.Count==0 ? "" : "; NGX: "+string.Join(" | ",recent.ToArray());
        }
    }
}
#endif
