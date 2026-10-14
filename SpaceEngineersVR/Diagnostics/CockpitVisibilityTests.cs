#if SEVR_DIAGNOSTICS
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using SharpDX.Direct3D11;
using SpaceEngineersVR.Player;
using SpaceEngineersVR.Wrappers;
using VRage.FileSystem;
using VRage.Render.Scene;

namespace SpaceEngineersVR.Diagnostics
{
    internal static class CockpitVisibilityTests
    {
        internal static void RunNative(Action<string> log)
        {
            var source=(CockpitRender.Verification)AccessTools.Field(typeof(CockpitRender),"verification").GetValue(null);
            var ids=source.Actors.Where(id=>id!=uint.MaxValue).ToArray();
            if(ids.Length<2) throw new InvalidOperationException("Cockpit visibility fixture needs native replacement actors");
            var exterior=MyIDTracker<MyActor>.FindByID(ids[0]);
            bool savedExterior=exterior.IsVisible;
            var check=new CockpitRender.Verification(source.Interior,CockpitRig.Find(CockpitLayout.Fighter).Geometry(MyFileSystem.ContentPath),Array.Empty<string>()) {
                Exterior=ids[0],Actors=ids.Skip(1).ToArray() };
            var actors=new[] {check.Interior}.Concat(check.Actors).Select(id=>MyIDTracker<MyActor>.FindByID(id)).ToArray();
            var saved=actors.Select(actor=>actor.IsVisible).ToArray();
            var restore=new List<Action>();
            try
            {
                exterior.SetVisibility(false); exterior.UpdateBeforeDraw();
                try
                {
                    CockpitRender.RemoteVisibility(check,restore);
                    if(!exterior.IsVisible || actors.Any(actor=>actor.IsVisible)) throw new InvalidOperationException("Camera feed left native interior or replacement cockpit visible");
                    var nested=new List<Action>();
                    CockpitRender.RemoteVisibility(check,nested);
                    if(nested.Count!=0) throw new InvalidOperationException("Nested camera visibility changed an already isolated scene");
                }
                finally
                {
                    for(int i=restore.Count-1;i>=0;i--) restore[i]();
                }
                if(exterior.IsVisible || actors.Where((actor,i)=>actor.IsVisible!=saved[i]).Any()) throw new InvalidOperationException("Camera feed failed to restore physical cockpit visibility");
            }
            finally { exterior.SetVisibility(savedExterior); exterior.UpdateBeforeDraw(); }
            log("PASS native cockpit camera visibility: exterior-only feed, hidden interior/replacements, nested isolation and finally restoration of physical cockpit actors.");
        }
        // Flags the proxies directly; EnableHead(false) is not available in the main menu.
        internal static void RunFirstPerson(uint character,BorrowedRtvTexture target,string output,Action<string> log)
        {
            // The main menu has no character definitions loaded; read the installed astronaut's list.
            var astronaut=System.Xml.Linq.XDocument.Load(Path.Combine(MyFileSystem.ContentPath,"Data","Characters.sbc")).Descendants("Character")
                .First(c=>(string)c.Element("Name")=="Default_Astronaut");
            var materials=RemoteView.FirstPersonMaterials(new Sandbox.Definitions.MyCharacterDefinition {
                MaterialsDisabledIn1st=astronaut.Element("MaterialsDisabledIn1st").Elements("Material").Select(m=>m.Value).ToArray() });
            var renderable=RemoteScene.RenderableOf.Invoke(MyIDTracker<MyActor>.FindByID(character),null);
            var flags=RemoteScene.ProxyFlags; long hidden=RemoteScene.FirstPersonHidden;
            var head=new List<object>(); var other=new List<object>();
            foreach(object lod in (IEnumerable)CockpitRender.Member(renderable,"Lods"))
            foreach(object proxy in (IEnumerable)CockpitRender.Member(lod,"RenderableProxies"))
            {
                object material=CockpitRender.Member(proxy,"Material");
                bool listed=Convert.ToInt32(CockpitRender.Member(material,"Index"))>=0 && materials.Contains(CockpitRender.Member(CockpitRender.Member(material,"Info"),"Name").ToString());
                (listed ? head:other).Add(proxy);
            }
            if(head.Count==0) throw new InvalidOperationException("Astronaut has no first-person materials");
            var saved=head.Concat(other).ToDictionary(p=>p,p=>flags.GetValue(p));
            long Value(object proxy) => Convert.ToInt64(flags.GetValue(proxy));
            try
            {
                foreach(object proxy in head) flags.SetValue(proxy,Enum.ToObject(flags.FieldType,Value(proxy)|hidden));
                var firstPerson=Albedo(target);
                var restore=new List<Action>();
                byte[] feed;
                try
                {
                    RemoteScene.ShowFirstPerson(character,materials,restore);
                    if(head.Any(p=>(Value(p)&hidden)!=0)) throw new InvalidOperationException("Camera feed left first-person materials hidden");
                    if(other.Any(p=>Value(p)!=Convert.ToInt64(saved[p]))) throw new InvalidOperationException("Camera feed changed unrelated character materials");
                    var nested=new List<Action>();
                    RemoteScene.ShowFirstPerson(character,materials,nested);
                    if(nested.Count!=0) throw new InvalidOperationException("Nested camera feed changed already shown materials");
                    feed=Albedo(target);
                    UiTests.Save(GBuffer0(),Path.Combine(output,"character-feed-head.png"));
                }
                finally { for(int i=restore.Count-1;i>=0;i--) restore[i](); }
                if(head.Any(p=>(Value(p)&hidden)!=hidden)) throw new InvalidOperationException("Camera feed did not hide first-person materials again");
                var restored=Albedo(target);
                UiTests.Save(GBuffer0(),Path.Combine(output,"character-first-person-head.png"));
                int shown=Differences(firstPerson,feed),leaked=Differences(firstPerson,restored);
                if(shown<200 || leaked>shown/20) throw new InvalidOperationException("Feed head pixels "+shown+", restored first-person differences "+leaked);
                log("PASS native first-person materials in camera feed: "+head.Count+" head/backpack proxies drawn in the feed scope ("+shown+" albedo pixels), hidden again before the next view ("+leaked+" differing), other materials unchanged.");
            }
            finally
            {
                foreach(var pair in saved) flags.SetValue(pair.Key,pair.Value);
                Albedo(target);
            }
        }
        private static Texture2D GBuffer0() =>
            (Texture2D)CockpitRender.Member(CockpitRender.Member(AccessTools.Field(AccessTools.TypeByName("VRage.Render11.Resources.MyGBuffer"),"Main").GetValue(null),"GBuffer0"),"Resource");
        private static byte[] Albedo(BorrowedRtvTexture target)
        {
            object ao=null;
            try { MyRender11.DrawGameScene(target,out ao); } finally { if(ao!=null) new BorrowedRtvTexture(ao).Release(); }
            var texture=GBuffer0(); var description=texture.Description;
            description.BindFlags=BindFlags.None; description.Usage=ResourceUsage.Staging; description.CpuAccessFlags=CpuAccessFlags.Read; description.OptionFlags=ResourceOptionFlags.None;
            using(var staging=new Texture2D(texture.Device,description))
            {
                var context=texture.Device.ImmediateContext;
                context.CopyResource(texture,staging);
                var mapped=context.MapSubresource(staging,0,MapMode.Read,MapFlags.None);
                try
                {
                    var bytes=new byte[description.Width*description.Height*4];
                    for(int y=0;y<description.Height;y++) SharpDX.Utilities.Read(IntPtr.Add(mapped.DataPointer,y*mapped.RowPitch),bytes,y*description.Width*4,description.Width*4);
                    return bytes;
                }
                finally { context.UnmapSubresource(staging,0); }
            }
        }
        private static int Differences(byte[] a,byte[] b)
        {
            int count=0;
            for(int i=0;i<a.Length;i+=4) if(Math.Abs(a[i]-b[i])+Math.Abs(a[i+1]-b[i+1])+Math.Abs(a[i+2]-b[i+2])>24) count++;
            return count;
        }
    }
}
#endif
