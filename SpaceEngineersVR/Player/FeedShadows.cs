using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace SpaceEngineersVR.Player
{
    // Camera feeds fit their own shadow cascades; the eyes' depth arrays and constants stay theirs.
    internal sealed class FeedShadows
    {
        private static readonly Type cascadesType=AccessTools.TypeByName("VRageRender.MyShadowCascades");
        private static readonly FieldInfo manager=AccessTools.Field(AccessTools.TypeByName("VRage.Render11.Common.MyManagers"),"Shadows");
        private static readonly FieldInfo current=manager==null ? null:AccessTools.Field(manager.FieldType,"<ShadowCascades>k__BackingField");
        private static readonly FieldInfo sceneFrame=AccessTools.Field(AccessTools.TypeByName("VRage.Render.Scene.MyScene"),"FrameCounter");
        private static readonly ConstructorInfo create=cascadesType==null ? null:AccessTools.Constructor(cascadesType,
            new[] {AccessTools.TypeByName("VRage.Render11.RenderContext.MyRenderContext"),typeof(int),typeof(int)});
        private static readonly PropertyInfo count=AccessTools.Property(cascadesType,"CascadeCount"),resolution=AccessTools.Property(cascadesType,"CascadeResolution");
        private static readonly MethodInfo unload=AccessTools.Method(cascadesType,"UnloadResources");
        private static readonly FieldInfo settings=AccessTools.Field(cascadesType,"Settings");
        private static readonly FieldInfo settingsData=settings==null ? null:AccessTools.Field(settings.FieldType,"Data");
        private static readonly FieldInfo everyFrame=settingsData==null ? null:AccessTools.Field(settingsData.FieldType,"UpdateCascadesEveryFrame");
        private static readonly FieldInfo states=AccessTools.Field(cascadesType,"m_cascadeStates");
        private static readonly FieldInfo lightAge=states==null ? null:AccessTools.Field(states.FieldType.GetElementType(),"FramesSinceLightUpdate");
        internal static readonly FeedShadows Camera=new FeedShadows("Camera feed",false),Lcd=new FeedShadows("LCD camera",true);
        private readonly string name;
        // LCD screens take turns with one cascade set, so every cascade must be refit to the screen's camera.
        private readonly bool refitAll;
        private object feed;
        private int feedCount,eyeResolution;
        private long frame;
        private bool failed;
        private FeedShadows(string name,bool refitAll) { this.name=name; this.refitAll=refitAll; }
        internal bool Available => !failed && current!=null && sceneFrame!=null && create!=null && count!=null && resolution!=null && unload!=null && (!refitAll || everyFrame!=null && lightAge!=null);
        internal object Instance => feed;
        internal static object Eyes { get { object shadows=manager?.GetValue(null); return shadows==null || current==null ? null:current.GetValue(shadows); } }
        internal static FeedShadows For(object cascades) => cascades==null ? null : ReferenceEquals(cascades,Camera.feed) ? Camera : ReferenceEquals(cascades,Lcd.feed) ? Lcd : null;
        internal bool Use(List<Action> restore)
        {
            if(!Available) return false;
            object shadows=manager.GetValue(null),eyes=shadows==null ? null:current.GetValue(shadows);
            if(eyes==null || For(eyes)!=null) return false;
            int cascades=(int)count.GetValue(eyes,null),size=(int)resolution.GetValue(eyes,null);
            if(feed==null || feedCount!=cascades || eyeResolution!=size)
            {
                Release();
                // PrepareQueries sizes viewports and texel snapping from the shadow quality setting, so the feed needs the eyes' resolution.
                try { feed=create.Invoke(new[] {Wrappers.MyRender11.RawRC,cascades,size}); }
                catch(Exception ex) { failed=true; Plugin.Logger.Warning(ex,name+" shadows use the eye cascades"); return false; }
                feedCount=cascades; eyeResolution=size;
                // The constructor sets skip frames from the scene counter; the feed counter continues from it.
                frame=(long)sceneFrame.GetValue(null);
            }
            current.SetValue(shadows,feed);
            restore.Add(()=>current.SetValue(shadows,eyes));
            return true;
        }
        // Native cascades refresh on fixed frame offsets; feeds skip headset frames, so each set counts its own frames.
        internal Action BeginFrame()
        {
            long saved=(long)sceneFrame.GetValue(null);
            sceneFrame.SetValue(null,++frame);
            if(!refitAll) return ()=>sceneFrame.SetValue(null,saved);
            // Far cascades keep their sun direction for several of their own frames, which steps on screens rendered every few frames.
            foreach(object state in (Array)states.GetValue(feed)) lightAge.SetValue(state,1<<20);
            object shared=settings.GetValue(null);
            object original=settingsData.GetValue(shared),data=settingsData.GetValue(shared);
            everyFrame.SetValue(data,true); settingsData.SetValue(shared,data);
            var render=Wrappers.MyRender11.Settings;
            bool usageSkip=render.ShadowCascadeUsageBasedSkip;
            render.ShadowCascadeUsageBasedSkip=false; Wrappers.MyRender11.Settings=render;
            return ()=>
            {
                sceneFrame.SetValue(null,saved);
                settingsData.SetValue(shared,original);
                var restored=Wrappers.MyRender11.Settings; restored.ShadowCascadeUsageBasedSkip=usageSkip; Wrappers.MyRender11.Settings=restored;
            };
        }
        internal void Release()
        {
            if(feed==null) return;
            try { unload.Invoke(feed,null); }
            finally { feed=null; }
        }
    }
}
