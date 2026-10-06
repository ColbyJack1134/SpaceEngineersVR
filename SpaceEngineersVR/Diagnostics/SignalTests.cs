using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using HarmonyLib;
using Sandbox.Game.GUI.HudViewers;
using SharpDX.Direct3D11;
using SpaceEngineersVR.Player;
using VRageMath;
using Color=System.Drawing.Color;

namespace SpaceEngineersVR.Diagnostics
{
    internal static class SignalTests
    {
        private static void Require(bool value,string message) { if(!value) throw new Exception(message); }
        private static WorldMarkers.Marker Marker(string id,string kind,string relation,Vector3D position,string name=null)
        {
            var color=relation=="Enemies" ? new Vector4(.89f,.24f,.25f,1):relation=="FactionShare" ? new Vector4(.4f,.70f,.35f,1):new Vector4(.46f,.79f,.94f,1);
            if(kind=="Ore") color=new Vector4(.94f,.9f,.55f,1);
            return new WorldMarkers.Marker { Id=id,Name=name ?? id,Kind=kind,Relation=relation,Position=position,Color=color,Cluster=kind!="Target" && kind!="ButtonMarker",
                Icon=kind=="Ore" ? "ore":@"Textures\HUD\marker_"+(kind=="GPS" || kind=="Objective" ? "gps":kind=="Scenario" ? "scenario":relation=="Enemies" ? "enemy":relation=="FactionShare" ? "friendly":relation=="Owner" ? "self":"neutral")+".dds",
                Distance=position.Length(),Description="" };
        }
        private static WorldMarkers.View ClosePair(MatrixD head,DateTime now)
        {
            var a=Marker("close-a","GPS","Owner",head.Translation+new Vector3D(-300,60,-10000),"Base");
            var b=Marker("close-b","GPS","Owner",head.Translation+new Vector3D(50,30,-12000),"Rendezvous");
            a.Distance=Vector3D.Distance(a.Position,head.Translation); b.Distance=Vector3D.Distance(b.Position,head.Translation);
            return new WorldMarkers.View(new[] {a,b},now) {Mode=MyHudMarkerRender.SignalMode.FullDisplay};
        }
        internal static WorldMarkers.View Scene(string scenario,MatrixD head,DateTime now)
        {
            var values=new List<WorldMarkers.Marker>();
            values.Add(Marker("home","GPS","Owner",new Vector3D(-170,70,-1000),"Base"));
            values.Add(Marker("ice","GPS","Owner",new Vector3D(210,100,-2000),"Ice"));
            values.Add(Marker("hostile","UnknownEntity","Enemies",new Vector3D(150,-60,-1200),"Small Grid"));
            values.Add(Marker("ore","Ore","NoOwnership",new Vector3D(-28,-15,-160),"Iron")); values.Last().Pinned=true; values.Last().Cluster=false;
            if(scenario.Contains("dense") || scenario.Contains("overlap") || scenario.Contains("cluster"))
            {
                var random=new Random(71);
                for(int i=0;i<(scenario.Contains("cluster") ? 6:120);i++)
                {
                    double distance=850+(i%3)*60;
                    double x=(scenario.Contains("cluster") ? 0:((i%5)-2)*.24)+(random.NextDouble()-.5)*.055;
                    double y=(scenario.Contains("cluster") ? 0:((i/5%3)-1)*.18)+(random.NextDouble()-.5)*.045;
                    string relation=i%4==0 ? "Enemies":i%4==1 ? "FactionShare":i%4==2 ? "Owner":"Neutral";
                    values.Add(Marker("grid-"+i.ToString("000"),i%2==0 ? "LargeEntity":"SmallEntity",relation,new Vector3D(x*distance,y*distance,-distance),"Small Grid "+(i+1)));
                }
            }
            if(scenario.Contains("categories"))
            {
                values.Clear();
                var kinds=new[] {"GPS","ContractGPS","Objective","Scenario","Ore","Hack","UnknownEntity","Character","SmallEntity","LargeEntity","StaticEntity","ButtonMarker","Target","OffscreenTarget"};
                for(int i=0;i<kinds.Length;i++) values.Add(Marker(kinds[i],kinds[i],i%3==0 ? "Enemies":i%3==1 ? "FactionShare":"Owner",new Vector3D((i%5-2)*115,(1-i/5)*100,-600),kinds[i]));
            }
            if(scenario.Contains("categories"))
            {
                var locked=values.First(m=>m.Kind=="OffscreenTarget"); locked.Name="Locked ship"; locked.LockState="Locking"; locked.LockProgress=.65f; locked.Pinned=true;
            }
            if(scenario.Contains("long"))
            {
                values.Add(Marker("long","ContractGPS","Owner",new Vector3D(0,0,-1400),"Mining Outpost 02 - Ice and Iron Storage - Landing Pad GPS"));
                values.Last().Pinned=true;
                values.Add(Marker("far","GPS","Owner",new Vector3D(240000,-100000,-1000000),"GPS"));
            }
            if(scenario.Contains("edge"))
            {
                values.Add(Marker("left","StaticEntity","Owner",new Vector3D(-2000,200,-1000),"Station behind left"));
                values.Add(Marker("right","GPS","Owner",new Vector3D(2000,200,-1000),"Return waypoint"));
                values.Add(Marker("behind","LargeEntity","Enemies",new Vector3D(100,0,1000),"Behind"));
                values.Add(Marker("top","GPS","Owner",new Vector3D(0,1800,-1000),"Above"));
                values.Add(Marker("bottom","GPS","Owner",new Vector3D(0,-1800,-1000),"Below"));
            }
            foreach(var m in values) m.Position=Vector3D.Transform(m.Position,head);
            return new WorldMarkers.View(values.ToArray(),now) { Mode=MyHudMarkerRender.SignalMode.DefaultMode };
        }
        public static void Run(Action<string> log)
        {
            HudProfileTests.Run(log);
            Require(typeof(Sandbox.Game.EntityComponents.MyTargetFocusComponent).GetMethod("OnLockRequest",Type.EmptyTypes)!=null,"Native lock request API changed");
            var now=DateTime.UtcNow;
            var head=MatrixD.Identity; head.Translation=new Vector3D(1e12,-2e12,3e12);
            var options=new SignalLayout.Options();
            var dense=Scene("dense-long-edge",head,now);
            var layout=SignalLayout.Build(dense,head,options,now);
            Require(!layout.Any(e=>e.Edge),"World HUD displays off-view directions");
            Require(layout.Length<dense.Markers.Length/2,"Dense scene failed to group");
            var sparse=Scene("sparse",head,now);
            sparse.Mode=MyHudMarkerRender.SignalMode.NoNames;
            var noNames=SignalLayout.Build(sparse,head,options,now);
            Require(noNames.All(e=>e.Labels.Any(l=>!string.IsNullOrEmpty(l.Distance))),"Native No Names lost distances");
            Require(noNames.Where(e=>!e.Primary.Pinned).All(e=>e.Labels.All(l=>string.IsNullOrEmpty(l.Name))),"Native No Names leaked ordinary names");
            Require(noNames.Where(e=>e.Primary.Pinned).Any(e=>e.Labels.Any(l=>!string.IsNullOrEmpty(l.Name))),"Native pinned-name exception lost");
            Require(SignalLayout.Build(sparse,head,new SignalLayout.Options {Distances=false},now).All(e=>e.Labels.All(l=>string.IsNullOrEmpty(l.Distance))),"Distance profile override ignored");
            sparse.Mode=MyHudMarkerRender.SignalMode.FullDisplay;
            Require(SignalLayout.Build(sparse,head,options,now).Sum(e=>e.Labels.Length)==4,"Details require head focus");
            var ids=layout.SelectMany(e=>e.Members).Select(m=>m.Id).OrderBy(id=>id).ToArray();
            Require(ids.Distinct().Count()==ids.Length,"Grouping duplicated signals");
            dense.Mode=MyHudMarkerRender.SignalMode.FullDisplay;
            layout=SignalLayout.Build(dense,head,options,now.AddSeconds(1));
            Require(layout.Sum(e=>e.Labels.Length)<=64,"Signal atlas row budget exceeded");
            Require(layout.Where(e=>e.Members.Any(m=>m.Pinned)).All(e=>e.Members.Length==1),"Pinned waypoint was clustered");
            var reordered=new WorldMarkers.View(dense.Markers.Reverse().ToArray(),now) {Mode=dense.Mode};
            Require(layout.Select(e=>e.Id).SequenceEqual(SignalLayout.Build(reordered,head,options,now).Select(e=>e.Id)),"POI enumeration reorders groups");
            var cluster=Scene("cluster",head,now); cluster.Mode=MyHudMarkerRender.SignalMode.NoNames;
            cluster.Reveal=true;
            var revealed=SignalLayout.Build(cluster,head,new SignalLayout.Options(),now);
            Require(revealed.Any(e=>e.Members.Length>1),"Native reveal unexpectedly disables grouping");
            Require(revealed.Where(e=>e.Members.Length>1).All(e=>e.Labels.Count(l=>l.LeftAligned)==e.Representatives.Count(m=>!string.IsNullOrEmpty(m.Name))),"Reveal suppressed group member names");
            var close=ClosePair(head,now);
            var pair=SignalLayout.Build(close,head,new SignalLayout.Options {Group=false},now);
            Require(pair.Length==2 && pair.All(e=>e.Labels.Length==1),"Two close ungrouped waypoints lost details");
            options.Group=false;
            Require(SignalLayout.Build(dense,head,options,now).All(e=>e.Members.Length==1 && !e.Edge),"Grouping off keeps clustered markers or edge directions");
            options.Gps=options.Contacts=options.Resources=false;
            Require(SignalLayout.Build(dense,head,options,now).Length==0,"Category switches leak hidden markers");
            options=new SignalLayout.Options();
            var panel=new SurfaceView {Width=.4f,Height=.225f,Pose=MatrixD.CreateTranslation(0,0,-.6)*head,Style=SurfaceStyle.WristMenu,SignalWindow=true};
            var center=WristSignals.Aperture.Center;
            var hit=Vector3D.Transform(new Vector3D((center.X-.5)*panel.Width,(.5-center.Y)*panel.Height,0),panel.Pose);
            var ray=Vector3D.Normalize(hit-head.Translation);
            var a=Marker("a","GPS","Owner",head.Translation+ray*500,"Coincident A");
            var b=Marker("b","SmallEntity","Enemies",head.Translation+ray*2000,"Coincident B");
            var before=Marker("before","GPS","Owner",head.Translation+ray*.2);
            var behind=Marker("behind","GPS","Owner",head.Translation-ray*500);
            var source=new WorldMarkers.View(new[] {a,b,before,behind},now) { Mode=MyHudMarkerRender.SignalMode.NoNames };
            WristSignals.Reset();
            var inspection=WristSignals.Resolve(source,panel,head.Translation,options,now);
            Require(inspection.Candidates.Length==2,"Wrist includes behind-head or nearer-than-glass signal");
            Require(inspection.Candidates.All(c=>c.Visible),"Coincident names cannot be viewed simultaneously");
            Require(!SignalLayout.Overlaps(inspection.Candidates[0].Label,inspection.Candidates[1].Label),"Coincident wrist names overlap");
            Require(inspection.Candidates.All(c=>c.Marker.Name.Length>0),"Icon mode discarded wrist names");
            var stacked=Scene("dense",MatrixD.Identity,now);
            var glass=panel.At(MatrixD.CreateTranslation(0,0,-.6));
            var denseGlass=WristSignals.Apply(glass,stacked,MatrixD.Identity,options,now).Signals;
            Require(WristSignals.Symbols(denseGlass).Length<denseGlass.Candidates.Length/2,"Overlapping wrist contacts did not use compact native groups");
            Require(WristSignals.Symbols(denseGlass).SelectMany(g=>g).Count()==denseGlass.Candidates.Count(c=>!c.Edge),"Wrist grouping lost an inspection candidate");
            Require(denseGlass.Candidates.All(c=>!SignalLayout.Overlaps(c.Reserved,WristKnob.Reserved)),"Inspected symbols overlap the tint knob");

            var worldEntries=SignalLayout.Build(source,head,options,now);
            var outside=WristSignals.OutsideWindow(worldEntries,panel,head);
            Require(!outside.SelectMany(e=>e.Members).Any(m=>m==a || m==b),"Wrist retained distant duplicate icons");
            var crossing=new SignalLayout.Entry {SymbolBounds=new VRageMath.RectangleF(1,1,.01f,.01f),Position=head.Translation+head.Forward*1000+head.Right*1000,Members=new[] {a},
                Labels=new[] {new SignalLayout.Label {Bounds=new VRageMath.RectangleF(-.01f,-.01f,.1f,.1f)}}};
            crossing.Members=new[] {Marker("side","GPS","Owner",crossing.Position)};
            var hiddenLabel=WristSignals.OutsideWindow(new[] {crossing},panel,head);
            Require(hiddenLabel.Length==1 && hiddenLabel[0].Labels.Length==0 && crossing.Labels.Length==1,"Off-window marker text crossed the wrist depth plane or mutated its source");
            panel.Signals=inspection;
            foreach(var candidate in inspection.Candidates)
            {
                var board=WristSignals.Board(panel,candidate);
                Require(Vector3D.Distance(board.Center,candidate.Marker.Position)<.001,"Inspected symbol lost native world depth");
                var plane=WristSignals.DetailPlane(panel,candidate);
                var iconLocal=Vector3D.TransformNormal(board.Center-plane.Translation,MatrixD.Invert(plane));
                Require(Math.Abs(iconLocal.Z)<.001,"Inspected text and symbol do not share the target depth plane");
            }
            inspection=WristSignals.Resolve(source,panel,head.Translation,options,now.AddSeconds(2));
            Require(inspection.Candidates.Length==0,"Stale contact persisted in wrist window");
            var back=panel.At(MatrixD.CreateRotationY(Math.PI)*panel.Pose);
            Require(WristSignals.Candidates(source,back,head.Translation,options).Length==0,"Inspection accepts back of tablet");
            var rotated=MatrixD.CreateFromYawPitchRoll(.4,.7,-.2); rotated.Translation=new Vector3D(2e10,-3e10,1e10);
            var localHead=MatrixD.Identity;
            var localPanel=panel.At(MatrixD.CreateTranslation(0,0,-.6));
            var local=Scene("long",localHead,now);
            var found=WristSignals.Candidates(local,localPanel,Vector3D.Zero,options).Select(c=>c.Marker.Id).ToArray();
            var moved=Scene("long",rotated,now);
            Require(found.SequenceEqual(WristSignals.Candidates(moved,localPanel.At(localPanel.Pose*rotated),rotated.Translation,options).Select(c=>c.Marker.Id)),"Third-person tracking-to-world changed wrist selection");
            var baseline=WristSignals.Apply(localPanel,local,MatrixD.Identity,options,now).Signals;
            foreach(double scale in new[] {.1,25,250})
            {
                var transform=MatrixD.CreateScale(scale)*rotated;
                var scaled=Scene("long",transform,now);
                var inspected=WristSignals.Apply(localPanel.At(localPanel.Pose*transform),scaled,rotated,options,now).Signals;
                Require(inspected.Candidates.Select(c=>c.Marker.Id).SequenceEqual(baseline.Candidates.Select(c=>c.Marker.Id)),"Scaled third-person lens changed candidate bearings");
                for(int i=0;i<baseline.Candidates.Length;i++) Require(inspected.Candidates[i].Visible==baseline.Candidates[i].Visible &&
                    Vector2.Distance(inspected.Candidates[i].UV,baseline.Candidates[i].UV)<.0001f,"Third-person scale broke stereo label clearance: "+scale+" / "+baseline.Candidates[i].Marker.Id+" / visible "+baseline.Candidates[i].Visible+" -> "+inspected.Candidates[i].Visible+" / UV delta "+Vector2.Distance(inspected.Candidates[i].UV,baseline.Candidates[i].UV)+" / reserved "+baseline.Candidates[i].Reserved+" -> "+inspected.Candidates[i].Reserved);
            }
            NativeSnapshot(log);
            WristSignals.Reset();
            log("PASS signals: native contacts retained, deterministic grouping, pinned exclusions, native distance/focus/reveal behavior, category switches, simultaneous coincident wrist names, front/beyond-panel checks, stale expiry, transformed large-world inspection and 0.1/25/250 third-person scales.");
        }
        private static void NativeSnapshot(Action<string> log)
        {
            var type=AccessTools.Inner(typeof(MyHudMarkerRender),"PointOfInterest");
            foreach(string field in new[] {"m_pointsOfInterest","m_disableFading","m_playerIndicatorsDict"}) Require(AccessTools.Field(typeof(MyHudMarkerRender),field)!=null,"Native marker field missing: "+field);
            foreach(string property in new[] {"WorldPosition","Text","POIType","Relationship","AlwaysVisible","AllowsCluster","Entity","Distance","ContainerRemainingTime"}) Require(AccessTools.Property(type,property)!=null,"Native POI property missing: "+property);
            var renderer=(MyHudMarkerRender)FormatterServices.GetUninitializedObject(typeof(MyHudMarkerRender));
            var fieldInfo=AccessTools.Field(typeof(MyHudMarkerRender),"m_pointsOfInterest");
            var list=(IList)Activator.CreateInstance(fieldInfo.FieldType); fieldInfo.SetValue(renderer,list);
            var kinds=Enum.GetValues(AccessTools.Property(type,"POIType").PropertyType).Cast<object>().Where(k=>k.ToString()!="Group").ToArray();
            for(int i=0;i<96;i++)
            {
                object poi=Activator.CreateInstance(type,true);
                AccessTools.Property(type,"POIType").SetValue(poi,kinds[i%kinds.Length]);
                AccessTools.Property(type,"WorldPosition").SetValue(poi,new Vector3D(i,0,-1000));
                AccessTools.Property(type,"Distance").SetValue(poi,1000d+i);
                AccessTools.Property(type,"ContainerRemainingTime").SetValue(poi,"8 min");
                ((StringBuilder)AccessTools.Property(type,"Text").GetValue(poi)).Append("Native name "+i);
                list.Add(poi);
            }
            var snapshot=WorldMarkers.Read(renderer,MyHudMarkerRender.SignalMode.NoNames,Vector3D.Zero,DateTime.UtcNow);
            Require(snapshot.Markers.Length==96,"Native snapshot still has a 32-marker cap");
            Require(snapshot.Markers.All(m=>m.Name.StartsWith("Native name") && m.Remaining=="8 min" && m.Distance>=1000),"Native details lost in icon mode");
            ((StringBuilder)AccessTools.Property(type,"Text").GetValue(list[0])).Clear();
            Require(snapshot.Markers[0].Name.Length>0,"Snapshot retained native pooled text");
            list.Clear();
            object proxy=Activator.CreateInstance(type,true);
            AccessTools.Property(type,"POIType").SetValue(proxy,Enum.Parse(AccessTools.Property(type,"POIType").PropertyType,"UnknownEntity"));
            ((StringBuilder)AccessTools.Property(type,"Text").GetValue(proxy)).Append("Moving proxy");
            list.Add(proxy);
            var now=DateTime.UtcNow;
            AccessTools.Property(type,"WorldPosition").SetValue(proxy,new Vector3D(1e9,0,-1000));
            var initial=WorldMarkers.Read(renderer,MyHudMarkerRender.SignalMode.DefaultMode,Vector3D.Zero,now);
            var updated=new Vector3D(1e9+5,0,-1000);
            AccessTools.Property(type,"WorldPosition").SetValue(proxy,updated);
            var moving=WorldMarkers.Read(renderer,MyHudMarkerRender.SignalMode.DefaultMode,Vector3D.Zero,now.AddSeconds(1.0/60));
            Require(initial.Markers[0].Id==moving.Markers[0].Id && moving.Markers[0].Position==updated,"Moving proxy lost selection identity or lagged behind native position");
            var expired=WorldMarkers.Read(renderer,MyHudMarkerRender.SignalMode.DefaultMode,Vector3D.Zero,now.AddSeconds(2));
            Require(expired.Markers[0].Id!=moving.Markers[0].Id,"Anonymous proxy identity survived an ambiguous stale gap");
            log("PASS installed native signal API: all POI categories, 96 entries, original names/timers/distances in icon mode, pooled data copied before recycling.");
        }
        private static void WindowClip(Device device,string output,Action<string> log)
        {
            using(var source=new OverlayCanvas("Clip source",8,8,1,false,device))
            using(var scene=new OverlayCanvas("Clip probe",64,64,1,false,device))
            using(var texture=new ShaderResourceView(device,source.Texture))
            {
                source.Clear(Color.White); source.Upload();
                var panel=new SurfaceView {Width=.4f,Height=.225f,Pose=MatrixD.CreateRotationZ(.2)*MatrixD.CreateTranslation(0,0,-.6)};
                var projection=VrMath.Projection(-.4f,.4f,-.4f,.4f,.05);
                foreach(float eye in new[] {-.032f,.032f})
                {
                    scene.Clear(Color.Black); scene.Upload();
                    var view=MatrixD.CreateTranslation(eye,0,0);
                    var sprite=PhysicalSurface.Quad(texture,MatrixD.CreateTranslation(0,0,-1000),new VRageMath.RectangleF(-1000,1000,2000,2000),new Vector4(0,0,1,1),Vector4.One,view,projection);
                    WristSignals.Clip(ref sprite,panel,view,projection);
                    NativeSprites.Draw(scene.Texture,new[] {sprite});
                    string path=Path.Combine(output,"wrist-clip-"+(eye<0 ? "left":"right")+".png"); UiTests.Save(scene.Texture,path);
                    using(var image=new Bitmap(path))
                    for(int y=0;y<64;y++) for(int x=0;x<64;x++)
                    {
                        var point=new Vector3((x+.5f)/32-1,1-(y+.5f)/32,1);
                        var distances=new[] {sprite.Clip0,sprite.Clip1,sprite.Clip2,sprite.Clip3}.Select(p=>Vector3.Dot(point,new Vector3(p.X,p.Y,p.Z))).ToArray();
                        if(distances.Any(d=>Math.Abs(d)<.003)) continue;
                        Require((image.GetPixel(x,y).R>240)==distances.All(d=>d>=0),"Native shader did not clip world-depth text to the projected wrist aperture");
                    }
                }
            }
            log("PASS production GPU window clipping: both eyes, rotated aperture, distant quad, header and outside pixels excluded.");
        }
        internal static void Render(Device device,string output,Action<string> log)
        {
            GlyphAlpha(device,output,log);
            WindowClip(device,output,log);
            HudProfileTests.Render(device,output,log);
            SharedWaypointRenders(device,output,log);
            var now=DateTime.UtcNow;
            using(var scene=new OverlayCanvas("Signal production scenarios",1920,1080,1,false,device))
            {
                foreach(string scenario in new[] {"sparse-dark","sparse-bright","dense-dark","dense-bright","dense-full","dense-ungrouped","close-pair","close-pair-bright","corners","corners-left","corners-right","categories","rings-dark","rings-bright","long-edge","icons","off","gps-only","contacts-only","no-rings-edges"})
                {
                    var head=MatrixD.Identity; head.Translation=new Vector3D(1e9,2e9,-3e9);
                    var source=Scene(scenario.Contains("dense") || scenario.Contains("cluster") || scenario.StartsWith("sparse") || scenario=="categories" ? scenario:scenario.StartsWith("rings") ? "sparse":"long-edge",head,now);
                    var options=new SignalLayout.Options(); options.Projection(VrMath.Projection(-.971204f,.971204f,-.546302f,.546302f,.05));
                    if(scenario.StartsWith("close-pair")) {source=ClosePair(head,now); options.Group=false;}
                    if(scenario.StartsWith("corners"))
                    {
                        var points=new List<WorldMarkers.Marker>();
                        foreach(float x in new[] {-.82f,0,.82f}) foreach(float y in new[] {-.40f,0,.40f})
                            points.Add(Marker("corner"+x+":"+y,"GPS","Owner",head.Translation+new Vector3D(x*1000,y*1000,-1000),"Waypoint"));
                        foreach(var marker in points) marker.Distance=Vector3D.Distance(marker.Position,head.Translation);
                        source=new WorldMarkers.View(points.ToArray(),now) {Mode=MyHudMarkerRender.SignalMode.FullDisplay}; options.Group=false;
                    }
                    if(scenario.StartsWith("rings"))
                    {
                        var markers=source.Markers.ToList();
                        for(int i=0;i<3;i++)
                        {
                            var marker=Marker("focus"+i,"OffscreenTarget",i==1 ? "Enemies":i==2 ? "FactionShare":"Neutral",Vector3D.Transform(new Vector3D((i-1)*.31*500,.2*500,-500),head),"Small Grid");
                            marker.Ring=NativeSignalProbe.Ring(i==0 ? "Neutral":i==1 ? "Enemy":"Friendly",i==2 ? .65f:0); marker.Pinned=true; marker.Cluster=false; marker.LockState="Focused"; marker.Color=marker.Ring.LockColor; marker.Distance=Vector3D.Distance(marker.Position,head.Translation); markers.Add(marker);
                        }
                        source=new WorldMarkers.View(markers.ToArray(),now) {Mode=MyHudMarkerRender.SignalMode.NoNames};
                    }
                    if(scenario=="icons") source.Mode=MyHudMarkerRender.SignalMode.NoNames;
                    if(scenario=="off") source.Mode=MyHudMarkerRender.SignalMode.Off;
                    if(scenario.StartsWith("sparse") || scenario=="categories" || scenario=="long-edge" || scenario.Contains("full") || scenario.Contains("ungrouped")) source.Mode=MyHudMarkerRender.SignalMode.FullDisplay;
                    if(scenario=="categories" || scenario.Contains("ungrouped")) options.Group=false;
                    if(scenario=="gps-only") options.Contacts=options.Resources=false;
                    if(scenario=="contacts-only") options.Gps=options.Resources=false;
                    if(scenario=="no-rings-edges") options.Rings=options.Edges=false;
                    SignalLayout.Build(source,head,options,now);
                    var entries=SignalLayout.Build(source,head,options,now.AddSeconds(1));
                    Action paint=()=> { Background(scene,scenario.Contains("bright")); SignalPainter.Draw(scene.Texture,entries,head,MatrixD.Invert(MatrixD.CreateTranslation(scenario=="corners-left" ? -.032:scenario=="corners-right" ? .032:0,0,0)*head),VrMath.Projection(-.971204f,.971204f,-.546302f,.546302f,.05)); };
                    paint(); WaitIcons(); paint();
                    UiTests.Save(scene.Texture,Path.Combine(output,"signals-"+scenario+".png"));
                    log("RENDER "+scenario+": "+source.Markers.Length+" source, "+entries.Length+" indicators, "+entries.Sum(e=>e.Labels.Length)+" label rows");
                }
                foreach(string scenario in new[] {"wrist-dark","wrist-bright","wrist-dense","wrist-detailed","wrist-long-edge","wrist-left-eye","wrist-right-eye","wrist-lock","wrist-locking","wrist-locked","wrist-empty","wrist-off"})
                {
                    var head=MatrixD.Identity;
                    var source=Scene((scenario=="wrist-dense" || scenario=="wrist-detailed") ? "dense":scenario=="wrist-long-edge" ? "long-edge":"sparse",head,now);
                    source.Mode=scenario=="wrist-detailed" ? MyHudMarkerRender.SignalMode.FullDisplay:MyHudMarkerRender.SignalMode.NoNames;
                    var panel=new SurfaceView { Id="Signal window fixture",Width=.4f,Height=.225f,Pose=MatrixD.CreateTranslation(0,-.0315,-.64),Style=SurfaceStyle.WristMenu,SignalWindow=true };
                    if(scenario=="wrist-empty") source=new WorldMarkers.View(new WorldMarkers.Marker[0],now) {Mode=MyHudMarkerRender.SignalMode.DefaultMode};
                    if(scenario=="wrist-off") source.Mode=MyHudMarkerRender.SignalMode.Off;
                    if(scenario.StartsWith("wrist-lock"))
                    {
                        var ring=Marker("lock-fixture","OffscreenTarget","Enemies",new Vector3D(0,-40,-1200),"");
                        ring.LockState=scenario=="wrist-lock" ? "Focused":scenario=="wrist-locking" ? "Locking":"Locked";
                        ring.Ring=NativeSignalProbe.Ring("Enemy",scenario=="wrist-lock" ? 0:scenario=="wrist-locking" ? .5f:1); ring.Cluster=false;
                        source=new WorldMarkers.View(new[] {ring},now) {Mode=MyHudMarkerRender.SignalMode.NoNames};
                    }
                    WristSignals.Reset();
                    panel=WristSignals.Apply(panel,source,head,new SignalLayout.Options(),now);
                    WristPanel.Show(3); panel.Keys=WristPanel.Keys(null,false,false,false,true,null); WristPanel.Show(0);
                    var entries=SignalLayout.Build(source,head,new SignalLayout.Options(),now);
                    var projection=VrMath.Projection(-.43f,.43f,-.242f,.242f,.05);
                    var eyeView=MatrixD.CreateTranslation(scenario=="wrist-left-eye" ? .032:scenario=="wrist-right-eye" ? -.032:0,0,0);
                    Background(scene,scenario=="wrist-bright");
                    SignalPainter.Draw(scene.Texture,WristSignals.OutsideWindow(entries,panel,head),head,eyeView,projection);
                    PhysicalSurface.Draw(scene.Texture,new[] {panel},eyeView,projection,null);
                    UiTests.Save(scene.Texture,Path.Combine(output,scenario+".png"));
                    using(var face=new OverlayCanvas("Signal window face",1024,640,1,false,device))
                    { PhysicalSurface.Paint(face,panel); face.Upload(); UiTests.Save(face.Texture,Path.Combine(output,scenario+"-face.png")); }
                }
            }
            foreach(var inspected in new[] {false,true})
            {
                WristPanel.Show(0);
                UiTests.Save(MenuHands.PreviewGlove(device,true,0,new Vector3(0,-1,0),1,preview:inspected ? (Action<SurfaceView[],MatrixD>)((panels,head)=>InspectPanel(panels,head,DateTime.UtcNow)) : null),
                    Path.Combine(output,inspected ? "wrist-installed-glove.png":"wrist-controls-glove.png"));
            }
            foreach(float tint in new[] {0f,.5f,1f}) foreach(bool hand in new[] {false,true})
                UiTests.Save(MenuHands.PreviewKnob(device,tint,hand),Path.Combine(output,"wrist-knob-"+tint+"-"+(hand ? "grip":"detail")+".png"));
            CrosshairRenders(device,output,log);
            RingRollRenders(device,output,log);
            MotionRenders(device,output,log);
            HudContexts(device,output,log);
            WristPanel.Show(0); WristSignals.Reset();
            log("PASS production signal renders: sparse/dense, categories, details/grouped/ungrouped, edges, long names, visibility and transparent wrist scenarios.");
        }
        internal static void InspectPanel(SurfaceView[] panels,MatrixD head,DateTime now)
        {
            var panel=panels.First(s=>s.Style==SurfaceStyle.WristMenu);
            panel.SignalWindow=true;
            var uv=WristSignals.Aperture.Center;
            var hit=Vector3D.Transform(new Vector3D((uv.X-.5)*panel.Width,(.5-uv.Y)*panel.Height,0),panel.Pose);
            var ray=Vector3D.Normalize(hit-head.Translation);
            var a=Marker("inspection","GPS","Owner",head.Translation+ray*1240,"Base"); a.Distance=1240; a.Encounter=true; a.Remaining="8 min";
            panel.Signals=WristSignals.Resolve(new WorldMarkers.View(new[] {a},now) {Mode=MyHudMarkerRender.SignalMode.NoNames},panel,head.Translation,new SignalLayout.Options(),now);
            WristPanel.Show(3); panel.Keys=WristPanel.Keys(null,false,false,false,true,null); WristPanel.Show(0);
        }
        private static void SharedWaypointRenders(Device device,string output,Action<string> log)
        {
            using(var scene=new OverlayCanvas("Scaled stereo waypoints",1600,900,1,false,device))
            foreach(double forward in new[] {-.05,0,.05})
            {
                var position=new Vector3D(0,10,forward);
                var head=MatrixD.CreateWorld(Vector3D.Zero,Vector3D.Normalize(position),Vector3D.Backward);
                var marker=Marker("pole-gps","GPS","Owner",position,"Waypoint"); marker.Distance=10;
                var source=new WorldMarkers.View(new[] {marker},DateTime.UtcNow) {Mode=MyHudMarkerRender.SignalMode.FullDisplay};
                var entries=SignalLayout.Build(source,head,new SignalLayout.Options(),DateTime.UtcNow);
                foreach(int eye in new[] {-1,1})
                {
                    var view=MatrixD.Invert(MatrixD.CreateTranslation(eye*3.2,0,0)*head);
                    var projection=VrMath.Projection(-.8f,.8f,-.45f,.45f,.5);
                    Action paint=()=> { Background(scene,false); SignalPainter.Draw(scene.Texture,entries,head,view,projection,true,Vector3D.Up); };
                    paint(); WaitIcons(); paint();
                    UiTests.Save(scene.Texture,Path.Combine(output,"waypoint-pole-"+forward.ToString("0.00",System.Globalization.CultureInfo.InvariantCulture)+"-"+eye+".png"));
                }
            }
            log("PASS production waypoint renders: shared center-facing orientation near and at the up-axis pole with third-person scale 100 eye separation.");
        }

        private static void CrosshairRenders(Device device,string output,Action<string> log)
        {
            using(var scene=new OverlayCanvas("Ship crosshair scenarios",1600,900,1,false,device))
            {
                var description=scene.Texture.Description; description.Format=SharpDX.DXGI.Format.B8G8R8A8_UNorm_SRgb;
                using(var eyeTarget=new SharpDX.Direct3D11.Texture2D(device,description))
                foreach(string scenario in new[] {"forward","look-right","third-person","bright","lead","lead-range","lead-overlap","lead-bright"})
                {
                    var ship=MatrixD.CreateTranslation(1e12,-2e12,3e12);
                    var head=ship;
                    if(scenario=="look-right") head=MatrixD.CreateRotationY(-.3)*ship;
                    if(scenario=="third-person") head=MatrixD.CreateTranslation(8,5,18)*ship;
                    var aim=ShipCrosshair.Read(new Sandbox.Game.Gui.MyHudCrosshair(),ship);
                    var source=Scene("sparse",ship,DateTime.UtcNow); source.Mode=MyHudMarkerRender.SignalMode.NoNames;
                    NativeLead.View lead=null;
                    if(scenario.StartsWith("lead"))
                    {
                        var target=new Vector3D(80,0,-700);
                        var prediction=MarkerTests.Predict(target,scenario=="lead-overlap" ? Vector3D.Zero:new Vector3D(80,0,0));
                        bool inRange=scenario!="lead-range";
                        var ring=Marker("lead-target","OffscreenTarget","Enemies",ship.Translation+target,"");
                        ring.Ring=NativeSignalProbe.Ring("Enemy",1); ring.Cluster=false; ring.LockState="Locked"; ring.Distance=target.Length();
                        source=new WorldMarkers.View(new[] {ring},DateTime.UtcNow) {Mode=MyHudMarkerRender.SignalMode.NoNames};
                        lead=new NativeLead.View {Position=ship.Translation+prediction,Target=ring.Position,InRange=inRange,CircleSize=ring.Ring.Size,RangeTextSize=inRange ? Vector2.Zero:NativeLead.MeasureRangeText(),
                            Color=MyHudMarkerRender.MyTargetIndicatorRender.GetTargetingColor(Sandbox.Game.GUI.MyStatControlTargetingProgressBar.ProgressBarTargetType.Enemy,inRange).ToVector4()};
                    }
                    var entries=SignalLayout.Build(source,head,new SignalLayout.Options(),DateTime.UtcNow);
                    var projection=VrMath.Projection(-.8f,.8f,-.45f,.45f,.05);
                    foreach(int eye in new[] {-1,1})
                    {
                        var view=MatrixD.Invert(MatrixD.CreateTranslation(eye*.032,0,0)*head);
                        Action paint=()=>
                        {
                            Background(scene,scenario.Contains("bright"));
                            device.ImmediateContext.CopyResource(scene.Texture,eyeTarget);
                            SignalPainter.Draw(eyeTarget,entries,head,view,projection);
                            ShipCrosshair.Draw(eyeTarget,aim,head,view,projection);
                            NativeLead.Draw(eyeTarget,lead,view,projection);
                        };
                        paint(); WaitIcons(); paint();
                        UiTests.Save(eyeTarget,Path.Combine(output,"ship-crosshair-"+scenario+"-"+eye+".png"));
                    }
                }
            }
            log("PASS production ship aim renders: stereo crosshair, head turns, observer offset, large coordinates; native lead, overlap, range warning and dark/bright backgrounds.");
        }
        // Rings and lead previously used the eye basis while ordinary markers used Character roll and viewer facing.
        private static void RingRollRenders(Device device,string output,Action<string> log)
        {
            using(var scene=new OverlayCanvas("Ring roll scenarios",1600,900,1,false,device))
            {
                var description=scene.Texture.Description; description.Format=SharpDX.DXGI.Format.B8G8R8A8_UNorm_SRgb;
                using(var eyeTarget=new SharpDX.Direct3D11.Texture2D(device,description))
                foreach(bool corrected in new[] {false,true})
                {
                    var ship=MatrixD.CreateTranslation(1e9,-2e9,3e9);
                    var head=MatrixD.CreateRotationZ(.45)*MatrixD.CreateRotationY(.12)*ship;
                    var up=ship.Up;
                    var target=Vector3D.Transform(new Vector3D(260,-60,-700),ship);
                    var ring=Marker("roll-target","OffscreenTarget","Enemies",target,"");
                    ring.Ring=NativeSignalProbe.Ring("Enemy",.5f); ring.Cluster=false; ring.LockState="Locking"; ring.Distance=Vector3D.Distance(target,head.Translation);
                    var gps=Marker("roll-gps","GPS","Owner",Vector3D.Transform(new Vector3D(-200,90,-700),ship),"Waypoint");
                    gps.Distance=Vector3D.Distance(gps.Position,head.Translation);
                    var source=new WorldMarkers.View(new[] {ring,gps},DateTime.UtcNow) {Mode=MyHudMarkerRender.SignalMode.NoNames};
                    var lead=new NativeLead.View {Position=target+Vector3D.TransformNormal(new Vector3D(70,10,0),ship),Target=target,InRange=false,CircleSize=ring.Ring.Size,RangeTextSize=NativeLead.MeasureRangeText(),
                        Color=MyHudMarkerRender.MyTargetIndicatorRender.GetTargetingColor(Sandbox.Game.GUI.MyStatControlTargetingProgressBar.ProgressBarTargetType.Enemy,false).ToVector4()};
                    var entries=SignalLayout.Build(source,head,new SignalLayout.Options(),DateTime.UtcNow);
                    var projection=VrMath.Projection(-.8f,.8f,-.45f,.45f,.05);
                    var view=MatrixD.Invert(MatrixD.CreateTranslation(-.032,0,0)*head);
                    var rings=entries.Where(e=>e.Ring).ToArray();
                    if(!corrected) foreach(var e in rings) e.Ring=false;
                    var signalHead=MarkerBillboard.WithUp(head,up);
                    Action paint=()=>
                    {
                        Background(scene,false);
                        device.ImmediateContext.CopyResource(scene.Texture,eyeTarget);
                        if(corrected)
                        {
                            SignalPainter.Draw(eyeTarget,entries,signalHead,view,projection,true,up);
                            NativeLead.Draw(eyeTarget,lead,view,projection,up,true);
                        }
                        else
                        {
                            SignalPainter.Draw(eyeTarget,entries,signalHead,view,projection,true,up);
                            var sprites=new List<NativeSprite>();
                            foreach(var e in rings) e.Primary.Ring?.AddNative(sprites,e.Position,view,projection,eyeTarget.Description.Width);
                            NativeSprites.Draw(eyeTarget,sprites);
                            NativeLead.Draw(eyeTarget,lead,view,projection);
                        }
                    };
                    paint(); WaitIcons(); paint();
                    UiTests.Save(eyeTarget,Path.Combine(output,"ring-roll-"+(corrected ? "after":"before")+".png"));
                }
            }
            log("RENDER ring roll: rolled head, Character up, viewer-facing ring/lead/range text before and after.");
        }
        private static void MotionRenders(Device device,string output,Action<string> log)
        {
            using(var scene=new OverlayCanvas("Signal motion stereo",1280,720,1,false,device))
            {
                var baseline=new Dictionary<int,Vector2>();
                for(int tick=0;tick<=120;tick+=60)
                {
                    var head=MatrixD.Identity; head.Translation=new Vector3D(1e12+tick*5,-2e12,3e12);
                    var now=DateTime.UtcNow;
                    var m=Marker("moving","LargeEntity","FactionShare",head.Translation+new Vector3D(20,0,-120),"Small Grid"); m.Distance=Math.Sqrt(14800);
                    var gps=Marker("fixed","GPS","Owner",new Vector3D(1e12-180,-2e12,3e12-1400),"Base"); gps.Distance=Vector3D.Distance(gps.Position,head.Translation);
                    var source=new WorldMarkers.View(new[] {m,gps},now) {Mode=MyHudMarkerRender.SignalMode.FullDisplay};
                    var options=new SignalLayout.Options();
                    var entries=SignalLayout.Build(source,head,options,now);
                    foreach(int eye in new[] {-1,1})
                    {
                        var view=MatrixD.Invert(MatrixD.CreateTranslation(eye*.032,0,0)*head);
                        var projection=VrMath.Projection(-.6f,.6f,-.3375f,.3375f,.05);
                        Require(WorldMarkers.Project(entries.First(e=>e.Primary.Id=="moving").Position,view,projection,out var point),"Moving escort disappeared");
                        if(tick==0) baseline[eye]=point;
                        else Require(Vector2.Distance(point,baseline[eye])<.00001f,"Moving signal gained capture lag or large-coordinate wobble");
                        Background(scene,false); SignalPainter.Draw(scene.Texture,entries,head,view,projection);
                        UiTests.Save(scene.Texture,Path.Combine(output,"signals-motion-"+tick+"-"+eye+".png"));
                    }
                }
            }
            log("PASS production stereo motion: 300 m/s at trillion-metre coordinates, moving escort and fixed GPS, both eyes at 0/60/120 ticks; marker offset stable within 0.00001 normalized units.");
        }
        private static void HudContexts(Device device,string output,Action<string> log)
        {
            using(var scene=new OverlayCanvas("Signal HUD contexts",1920,1080,1,false,device))
            using(var hud=new OverlayCanvas("Combined production HUD",1280,560,1,false,device))
            using(var texture=new ShaderResourceView(device,hud.Texture))
            foreach(string context in new[] {"on-foot","cockpit","third-person","state-1","state-2","state-3","state-4"})
            {
                bool seated=context=="cockpit";
                var head=MatrixD.Identity; head.Translation=new Vector3D(1e8,-2e8,3e8);
                if(context=="third-person") head=MatrixD.CreateRotationY(.45)*head;
                var now=DateTime.UtcNow;
                int state=context.StartsWith("state-") ? int.Parse(context.Substring(6)):0;
                var source=Scene(state>0 ? "sparse":"dense-long-edge",head,now);
                source.Mode=state==1 ? MyHudMarkerRender.SignalMode.Off:state==2 || state==3 ? MyHudMarkerRender.SignalMode.NoNames:MyHudMarkerRender.SignalMode.FullDisplay;
                var projection=VrMath.Projection(-.971204f,.971204f,-.546302f,.546302f,.05);
                float scale=EssentialHud.FitScale(projection,projection);
                var options=new SignalLayout.Options(); options.Projection(projection);
                var entries=SignalLayout.Build(source,head,options,now);
                var status=new EssentialHud.View {Levels=new[] {.85f,.7f,.9f,.6f},Values=new[] {"85","70","90","60"},Icons=new[] {NativeSprites.Hud("EnergyIcon")},
                    Selected=seated ? "Gatling Gun":"Enhanced Welder",Ammo=seated ? "2,400":"",Speed="24.6",SpeedLevel=.246f,Helmet=true,Jetpack=!seated,Dampeners=true,Piloting=seated,
                    ShipPower=true,ShipBroadcasting=true,ShipBattery="61%  12.4 MWh",ShipBatteryLevel=.61f,ShipHydrogen="78%",ShipHydrogenLevel=.78f,ShipLoad="43%",ShipLoadLevel=.43f,
                    ShipMass="872,000 kg",ShipEndurance="2 h 12 min",OxygenBottles="2",HydrogenBottles="3",EnvironmentOxygen="High",Temperature="Warm",NaturalGravity="1.00",ArtificialGravity="0.00",Down=Vector3.Down};
                EssentialHud.Paint(hud,status); WaitIcons(); EssentialHud.Paint(hud,status); hud.Upload();
                Background(scene,context=="on-foot"); SignalPainter.Draw(scene.Texture,entries,head,MatrixD.Invert(head),projection);
                var pose=MatrixD.CreateTranslation(0,EssentialHud.OverlayY*scale,-EssentialHud.OverlayDepth)*head;
                if(state==0 || state==3) NativeSprites.Draw(scene.Texture,new[] {PhysicalSurface.Quad(texture,pose,new VRageMath.RectangleF(-EssentialHud.OverlayWidth*scale/2,EssentialHud.OverlayHeight*scale/2,
                    EssentialHud.OverlayWidth*scale,EssentialHud.OverlayHeight*scale),new Vector4(0,0,1,1),Vector4.One,MatrixD.Invert(head),projection)});
                UiTests.Save(scene.Texture,Path.Combine(output,"signals-context-"+context+".png"));
            }
            log("PASS combined production HUD: on-foot/cockpit/third-person fixtures, existing gauges/equipment, four default states and native focus/reveal placement.");
        }
        private static void GlyphAlpha(Device device,string output,Action<string> log)
        {
            using(var source=new OverlayCanvas("Glyph coverage",32,32,1,false,device))
            using(var atlas=new OverlayCanvas("Cached glyph",32,32,1,false,device))
            using(var target=new OverlayCanvas("Glyph composite",32,32,1,false,device))
            using(var sourceTexture=new ShaderResourceView(device,source.Texture))
            using(var atlasTexture=new ShaderResourceView(device,atlas.Texture))
            {
                source.Clear(Color.FromArgb(128,255,255,255)); source.Upload();
                atlas.Clear(Color.Transparent); atlas.Upload();
                NativeSprites.Draw(atlas.Texture,new[] {new NativeSprite(null,new VRageMath.RectangleF(0,0,32,32),Vector4.One) {Texture=sourceTexture}});
                target.Clear(Color.Black); target.Upload();
                NativeSprites.Draw(target.Texture,new[] {new NativeSprite(null,new VRageMath.RectangleF(0,0,32,32),Vector4.One) {Texture=atlasTexture,Premultiplied=true}});
                string path=Path.Combine(output,"signal-glyph-alpha.png"); UiTests.Save(target.Texture,path);
                using(var image=new Bitmap(path)) Require(Math.Abs(image.GetPixel(16,16).R-128)<=2,"Cached glyph coverage applied twice");
            }
            log("PASS signal glyph compositing: cached half-coverage glyph retains 50% brightness over black.");
        }
        internal static void WaitIcons()
        {
            var deadline=DateTime.UtcNow.AddSeconds(15);
            while(NativeSprites.Pending && DateTime.UtcNow<deadline) { NativeSprites.Poll(); Thread.Sleep(10); }
            Require(!NativeSprites.Pending,"Signal icon load timeout");
        }
        private static void Background(OverlayCanvas scene,bool bright)
        {
            scene.Clear(bright ? Color.FromArgb(255,221,224,214):Color.FromArgb(255,7,12,20));
            if(bright)
                using(var brush=new System.Drawing.Drawing2D.LinearGradientBrush(new System.Drawing.Rectangle(0,0,1920,1080),Color.FromArgb(244,240,222),Color.FromArgb(95,138,162),90)) scene.Graphics.FillRectangle(brush,0,0,1920,1080);
            scene.Upload();
        }
        internal static void NativeOverlay(Texture2D target,MatrixD view,MatrixD projection,string output,string context)
        {
            // Match the runtime infinite reverse-depth projection, preserving the fixture field of view.
            projection.M43=projection.M43/projection.M33; projection.M33=0;
            var head=MatrixD.Invert(view); var now=DateTime.UtcNow;
            var source=Scene("dense-long-edge",head,now);
            var options=new SignalLayout.Options(); options.Projection(projection);
            SignalLayout.Build(source,head,options,now);
            var entries=SignalLayout.Build(source,head,options,now.AddSeconds(1));
            using(var warm=new OverlayCanvas("Native signal uploads",16,16,1,false,target.Device))
            { SignalPainter.Draw(warm.Texture,entries,head,view,projection); WaitIcons(); }
            SignalPainter.Draw(target,entries,head,view,projection);
            if(context=="cockpit") ShipCrosshair.Draw(target,ShipCrosshair.Read(new Sandbox.Game.Gui.MyHudCrosshair(),head),head,view,projection);
            UiTests.Save(target,Path.Combine(output,"signals-native-"+context+".png"));
        }
    }
}
