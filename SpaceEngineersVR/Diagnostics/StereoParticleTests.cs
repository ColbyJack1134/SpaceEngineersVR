using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using SharpDX.Direct3D11;
using SpaceEngineersVR.Player;
using SpaceEngineersVR.Plugin;
using SpaceEngineersVR.Wrappers;
using VRageMath;
using Buffer=SharpDX.Direct3D11.Buffer;

namespace SpaceEngineersVR.Diagnostics
{
    internal static class StereoParticleTests
    {
        [ThreadStatic] private static ShaderResourceView expectedDepth;
        private static int depthChecks;
        private static void CaptureDepth(object __1) => expectedDepth=(ShaderResourceView)CockpitRender.Member(__1,"Srv");
        private static void VerifyDepth(object __0)
        {
            var stage=CockpitRender.Member(__0,"PixelShader");
            var resources=(ShaderResourceView[])AccessTools.Field(stage.GetType(),"m_srvs").GetValue(stage);
            Require(expectedDepth!=null && resources[0]!=null && resources[0].NativePointer==expectedDepth.NativePointer,
                "Particle pixel shader did not bind this view's depth at t0");
            System.Threading.Interlocked.Increment(ref depthChecks);
        }
        private static bool RetainEmitter(ref int __result) { __result=1; return false; }
        internal static void RunNative(BorrowedRtvTexture target,Action<string> log)
        {
            VerifyDensity(log);
            var native=AccessTools.TypeByName("VRageRender.MyGPUParticleRenderer");
            var common=AccessTools.TypeByName("VRageRender.MyCommon");
            var context=MyRender11.DeviceInstance.ImmediateContext;
            var camera=MyRender11.Environment_Matrices; var savedCamera=camera.Capture();
            var fields=new Dictionary<FieldInfo,object>();
            var backups=new List<Tuple<Buffer,Buffer,UnorderedAccessView,int>>();
            var config=Common.Config; bool mask=config.HiddenAreaMask,shadows=config.StableShadows;
            var update=AccessTools.Method(native,"Update");
            var harmony=new Harmony("SpaceEngineersVR.StereoParticleFixture");
            void Save(Type type,string name)
            {
                var field=AccessTools.Field(type,name); fields[field]=field.GetValue(null);
            }
            foreach(string name in new[] {"m_aliveIndexBuffer1","m_aliveIndexBuffer2","m_aliveIndexBufferIndex","m_emitterCount","m_emitterData","m_resetSystem"}) Save(native,name);
            Save(common,"m_lastFrameTimeDelta"); Save(common,"m_lastCameraPosition");
            foreach(string name in new[] {"<Active>k__BackingField","<CenterView>k__BackingField"}) Save(typeof(StereoRenderState),name);
            foreach(string name in new[] {"particleCamera","hasCamera"}) Save(typeof(StereoParticles),name);
            int view=StereoRenderState.View;
            try
            {
                foreach(string name in new[] {"m_particleBuffer","m_aliveIndexBuffer1","m_aliveIndexBuffer2","m_deadListBuffer",
                    "m_skippedParticleCountBuffer","m_indirectDrawArgsBuffer","m_indirectSimulateArgsBuffer","m_activeListConstantBuffer"})
                {
                    object resource=AccessTools.Field(native,name).GetValue(null);
                    var buffer=(Buffer)CockpitRender.Member(resource,"Resource");
                    var description=buffer.Description; description.Usage=ResourceUsage.Default; description.CpuAccessFlags=CpuAccessFlags.None;
                    var backup=new Buffer(MyRender11.DeviceInstance,description);
                    context.CopyResource(buffer,backup);
                    UnorderedAccessView uav=null;
                    if(CockpitRender.Find(resource.GetType(),"Uav")!=null) uav=(UnorderedAccessView)CockpitRender.Member(resource,"Uav");
                    int count=uav!=null && (uav.Description.Buffer.Flags & UnorderedAccessViewBufferFlags.Counter)!=0 ? Counter(uav) : -1;
                    backups.Add(Tuple.Create(buffer,backup,uav,count));
                }
                var emitterArray=(Array)AccessTools.Field(native,"m_emitterData").GetValue(null);
                var copy=(Array)emitterArray.Clone(); var emitterType=copy.GetType().GetElementType();
                object emitter=Activator.CreateInstance(emitterType);
                AccessTools.Field(emitterType,"ParticleLifeSpan").SetValue(emitter,5f);
                AccessTools.Field(emitterType,"UserLifeMultiplier").SetValue(emitter,1f);
                AccessTools.Field(emitterType,"AccelerationKey1").SetValue(emitter,.33f);
                AccessTools.Field(emitterType,"AccelerationKey2").SetValue(emitter,.66f);
                copy.SetValue(emitter,0);
                AccessTools.Field(native,"m_emitterData").SetValue(null,copy);
                AccessTools.Field(native,"m_emitterCount").SetValue(null,1);
                harmony.Patch(update,new HarmonyMethod(typeof(StereoParticleTests),nameof(RetainEmitter)) {priority=Priority.First});
                depthChecks=0;
                harmony.Patch(AccessTools.Method(native,"Run"),prefix:new HarmonyMethod(typeof(StereoParticleTests),nameof(CaptureDepth)));
                harmony.Patch(AccessTools.Method(native,"Render"),prefix:new HarmonyMethod(typeof(StereoParticleTests),nameof(VerifyDepth)));
                config.HiddenAreaMask=config.StableShadows=false;
                AccessTools.Field(common,"m_lastFrameTimeDelta").SetValue(null,.1f);
                AccessTools.Field(typeof(StereoRenderState),"<Active>k__BackingField").SetValue(null,true);
                AccessTools.Field(typeof(StereoRenderState),"<CenterView>k__BackingField").SetValue(null,camera.ViewD);
                StereoParticles.ResetCamera();
                var particle=(Buffer)CockpitRender.Member(AccessTools.Field(native,"m_particleBuffer").GetValue(null),"Resource");
                var seed=new Vector4[particle.Description.SizeInBytes/16];
                seed[0]=new Vector4(0,0,-5,0); seed[1]=new Vector4(1,0,0,2);
                seed[2]=new Vector4(0,0,5,0); seed[3]=new Vector4(0,0,-5,0);
                context.UpdateSubresource(seed,particle);
                object alive=AccessTools.Field(native,"m_aliveIndexBuffer2").GetValue(null);
                var aliveBuffer=(Buffer)CockpitRender.Member(alive,"Resource");
                context.UpdateSubresource(new float[aliveBuffer.Description.SizeInBytes/4],aliveBuffer);
                context.ComputeShader.SetUnorderedAccessView(0,(UnorderedAccessView)CockpitRender.Member(alive,"Uav"),1);
                context.ComputeShader.SetUnorderedAccessView(0,null);
                AccessTools.Field(native,"m_resetSystem").SetValue(null,false);
                using(var exposure=new RemoteExposure())
                {
                    StereoRenderState.View=0; Draw(target);
                    var left=Read(particle); Require(Math.Abs(left[10]-4.9f)<.0001,"First eye did not simulate one timestep: "+left[10]);
                    Require(Math.Abs(left[0]-.2f)<.0001,"First eye particle motion did not advance once");
                    object leftAlive=AccessTools.Field(native,"m_aliveIndexBuffer2").GetValue(null);
                    Require(Counter((UnorderedAccessView)CockpitRender.Member(leftAlive,"Uav"))==1,"Seed particle was lost");
                    var probe=AccessTools.Field(AccessTools.TypeByName("VRage.Render11.Common.MyManagers"),"EnvironmentProbe").GetValue(null);
                    var probeState=new Dictionary<FieldInfo,object>();
                    foreach(string name in new[] {"m_state","m_lastUpdateTime","m_blendDone","m_blendClose","m_initWorkProbes"})
                    { var field=AccessTools.Field(probe.GetType(),name); probeState[field]=field.GetValue(probe); }
                    camera.CameraPosition+=new Vector3D(.064,0,0);
                    StereoRenderState.View=1; Draw(target);
                    Require(Read(particle).SequenceEqual(left),"Right eye changed simulation state");
                    Require(ReferenceEquals(leftAlive,AccessTools.Field(native,"m_aliveIndexBuffer2").GetValue(null)),"Right eye swapped alive lists");
                    Require(probeState.All(s=>Equals(s.Value,s.Key.GetValue(probe))),"Right eye advanced reflection probes");
                    using(var remote=new RemoteScene())
                    {
                        camera.CameraPosition+=new Vector3D(1000,0,0); Draw(target);
                        Require(Read(particle).SequenceEqual(left),"Remote camera changed physical particles");
                        var flare=AccessTools.Field(AccessTools.TypeByName("VRage.Render11.Common.MyManagers"),"FlareOcclusionRenderer").GetValue(null);
                        AccessTools.Method(flare.GetType(),"Render").Invoke(flare,new object[4]);
                    }
                    StereoRenderState.View=-1; Draw(target);
                    Require(Read(particle).SequenceEqual(left),"Diagnostic desktop changed simulation state");
                    Require(probeState.All(s=>Equals(s.Value,s.Key.GetValue(probe))),"Camera or diagnostic desktop advanced reflection probes");
                    camera.Restore(savedCamera);
                    StereoRenderState.View=0; Draw(target);
                    var next=Read(particle);
                    Require(Math.Abs(next[10]-4.8f)<.0001,"Following physical frame did not advance once");
                    Require(Math.Abs(next[0]-.4f)<.0001 && next[1]==left[1] && next[2]==left[2],"Remote camera leaked into particle origin history");
                }
                Require(depthChecks>=5,"Particle depth binding was not checked in every view");
                log("PASS native stereo particles: per-view pixel depth binding, one lifetime/motion step, unchanged right/camera/desktop GPU state and alive lists, distant-camera origin isolation; remote flare queries and extra-view reflection updates blocked");
            }
            finally
            {
                harmony.UnpatchAll(harmony.Id); expectedDepth=null;
                foreach(var backup in backups)
                {
                    context.CopyResource(backup.Item2,backup.Item1);
                    if(backup.Item4>=0)
                    { context.ComputeShader.SetUnorderedAccessView(0,backup.Item3,backup.Item4); context.ComputeShader.SetUnorderedAccessView(0,null); }
                    backup.Item2.Dispose();
                }
                foreach(var saved in fields) saved.Key.SetValue(null,saved.Value);
                camera.Restore(savedCamera); StereoRenderState.View=view;
                config.HiddenAreaMask=mask; config.StableShadows=shadows;
            }
        }
        private static void VerifyDensity(Action<string> log)
        {
            var type=AccessTools.TypeByName("VRageRender.MyGPUEmitter");
            var emitters=AccessTools.TypeByName("VRageRender.MyGPUEmitters");
            var common=AccessTools.TypeByName("VRageRender.MyCommon");
            var delta=AccessTools.Field(common,"m_lastFrameTimeDelta");
            var multiplier=AccessTools.Field(emitters,"<ParticleCountMultiplier>k__BackingField");
            var active=AccessTools.Field(typeof(StereoRenderState),"<Active>k__BackingField");
            var failed=AccessTools.Field(typeof(Main),"failed");
            object savedDelta=delta.GetValue(null),savedMultiplier=multiplier.GetValue(null),savedActive=active.GetValue(null),savedFailed=failed.GetValue(null);
            float savedDensity=Common.Config.ParticleDensity;
            int Emit(float rate,float burst,int steps)
            {
                var emitter=Activator.CreateInstance(type,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,new object[] {"SEVR density fixture"},null);
                AccessTools.Field(type,"BufferIndex").SetValue(emitter,0);
                var field=AccessTools.Field(type,"EmitterData"); var data=Activator.CreateInstance(field.FieldType);
                AccessTools.Field(field.FieldType,"ParticlesPerSecond").SetValue(data,rate);
                AccessTools.Field(field.FieldType,"ParticlesPerFrame").SetValue(data,burst);
                AccessTools.Field(field.FieldType,"AtlasTexture").SetValue(data,"SEVR density fixture");
                AccessTools.Field(field.FieldType,"ParentID").SetValue(data,uint.MaxValue);
                AccessTools.Field(field.FieldType,"DistanceMaxSqr").SetValue(data,float.MaxValue);
                field.SetValue(emitter,data);
                var update=AccessTools.Method(type,"Update"); var layout=AccessTools.Field(field.FieldType,"Data").FieldType;
                int count=0;
                for(int i=0;i<steps;i++)
                {
                    var args=new[] {Activator.CreateInstance(layout)};
                    update.Invoke(emitter,args);
                    count+=(int)AccessTools.Field(layout,"NumParticlesToEmitThisFrame").GetValue(args[0]);
                }
                return count;
            }
            try
            {
                delta.SetValue(null,.1f); multiplier.SetValue(null,1f); active.SetValue(null,true);
                Common.Config.ParticleDensity=1;
                int original=Emit(100,0,10);
                Require(original==100,"Native full particle density changed emission");
                Common.Config.ParticleDensity=.5f;
                Require(Emit(100,0,10)==50,"Half particle density did not halve native continuous emission");
                Require(Math.Abs(Emit(3,0,100)-15)<=1,"Reduced low-rate particles lost fractional accumulation");
                Require(Emit(0,7,10)==7,"Particle density changed burst emission");
                Common.Config.ParticleDensity=.15f;
                Require(Math.Abs(Emit(100,0,10)-15)<=1,"Minimum particle density did not preserve native fractional emission");
                Common.Config.ParticleDensity=.5f;
                multiplier.SetValue(null,.25f);
                Require(Math.Abs(Emit(100,0,10)-12)<=1,"Particle density replaced native quality scaling");
                multiplier.SetValue(null,1f); failed.SetValue(null,true); active.SetValue(null,false);
                Require(Emit(100,0,10)==original,"Particle density changed non-VR emission");
                log("PASS native particle density: original/half rates, fractional accumulation, native quality multiplication, bursts unchanged and non-VR isolation.");
            }
            finally
            {
                delta.SetValue(null,savedDelta); multiplier.SetValue(null,savedMultiplier); active.SetValue(null,savedActive); failed.SetValue(null,savedFailed);
                Common.Config.ParticleDensity=savedDensity;
            }
        }
        private static int Counter(UnorderedAccessView uav)
        {
            var device=MyRender11.DeviceInstance;
            using(var buffer=new Buffer(device,16,ResourceUsage.Default,BindFlags.None,CpuAccessFlags.None,ResourceOptionFlags.None,0))
            { device.ImmediateContext.CopyStructureCount(buffer,0,uav); return BitConverter.ToInt32(BitConverter.GetBytes(Read(buffer)[0]),0); }
        }
        private static float[] Read(Buffer buffer)
        {
            var device=MyRender11.DeviceInstance;
            var description=buffer.Description; description.Usage=ResourceUsage.Staging; description.BindFlags=BindFlags.None;
            description.CpuAccessFlags=CpuAccessFlags.Read; description.OptionFlags=ResourceOptionFlags.None; description.StructureByteStride=0;
            using(var staging=new Buffer(device,description))
            {
                device.ImmediateContext.CopyResource(buffer,staging);
                var mapped=device.ImmediateContext.MapSubresource(staging,0,MapMode.Read,MapFlags.None);
                try { var result=new float[Math.Min(16,description.SizeInBytes/4)]; SharpDX.Utilities.Read(mapped.DataPointer,result,0,result.Length); return result; }
                finally { device.ImmediateContext.UnmapSubresource(staging,0); }
            }
        }
        private static void Draw(BorrowedRtvTexture target)
        { object ao=null; try { MyRender11.DrawGameScene(target,out ao); } finally { if(ao!=null) new BorrowedRtvTexture(ao).Release(); } }
        private static void Require(bool condition,string message) { if(!condition) throw new InvalidOperationException(message); }
    }
}
