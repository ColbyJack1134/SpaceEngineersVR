using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace SpaceEngineersVR.Player
{
    // Camera feeds fit their own shadow cascades; the eyes' depth arrays and constants stay theirs.
    internal static class FeedShadows
    {
        private static readonly Type cascadesType=AccessTools.TypeByName("VRageRender.MyShadowCascades");
        private static readonly FieldInfo manager=AccessTools.Field(AccessTools.TypeByName("VRage.Render11.Common.MyManagers"),"Shadows");
        private static readonly FieldInfo current=manager==null ? null:AccessTools.Field(manager.FieldType,"<ShadowCascades>k__BackingField");
        private static readonly FieldInfo sceneFrame=AccessTools.Field(AccessTools.TypeByName("VRage.Render.Scene.MyScene"),"FrameCounter");
        private static readonly ConstructorInfo create=cascadesType==null ? null:AccessTools.Constructor(cascadesType,
            new[] {AccessTools.TypeByName("VRage.Render11.RenderContext.MyRenderContext"),typeof(int),typeof(int)});
        private static readonly PropertyInfo count=AccessTools.Property(cascadesType,"CascadeCount"),resolution=AccessTools.Property(cascadesType,"CascadeResolution");
        private static readonly MethodInfo unload=AccessTools.Method(cascadesType,"UnloadResources");
        private static object feed;
        private static int feedCount,eyeResolution;
        private static long frame;
        private static bool failed;
        internal static bool Available => !failed && current!=null && sceneFrame!=null && create!=null && count!=null && resolution!=null && unload!=null;
        internal static object Instance => feed;
        internal static object Eyes { get { object shadows=manager?.GetValue(null); return shadows==null || current==null ? null:current.GetValue(shadows); } }
        internal static bool Use(List<Action> restore)
        {
            if(!Available) return false;
            object shadows=manager.GetValue(null),eyes=shadows==null ? null:current.GetValue(shadows);
            if(eyes==null || ReferenceEquals(eyes,feed)) return false;
            int cascades=(int)count.GetValue(eyes,null),size=(int)resolution.GetValue(eyes,null);
            if(feed==null || feedCount!=cascades || eyeResolution!=size)
            {
                Release();
                // PrepareQueries sizes viewports and texel snapping from the shadow quality setting, so the feed needs the eyes' resolution.
                try { feed=create.Invoke(new[] {Wrappers.MyRender11.RawRC,cascades,size}); }
                catch(Exception ex) { failed=true; Plugin.Logger.Warning(ex,"Camera feed shadows use the eye cascades"); return false; }
                feedCount=cascades; eyeResolution=size;
                // The constructor sets skip frames from the scene counter; the feed counter continues from it.
                frame=(long)sceneFrame.GetValue(null);
            }
            current.SetValue(shadows,feed);
            restore.Add(()=>current.SetValue(shadows,eyes));
            return true;
        }
        // Native cascades refresh on fixed frame offsets; the feed renders every other frame, so it counts its own frames.
        internal static long BeginFrame()
        {
            long saved=(long)sceneFrame.GetValue(null);
            sceneFrame.SetValue(null,++frame);
            return saved;
        }
        internal static void EndFrame(long saved) => sceneFrame.SetValue(null,saved);
        internal static void Release()
        {
            if(feed==null) return;
            try { unload.Invoke(feed,null); }
            finally { feed=null; }
        }
    }
}
