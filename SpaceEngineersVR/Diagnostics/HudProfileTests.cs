#if SEVR_DIAGNOSTICS
using System;
using System.IO;
using System.Linq;
using System.Xml.Serialization;
using Color=System.Drawing.Color;
using SharpDX.Direct3D11;
using SpaceEngineersVR.Config;
using SpaceEngineersVR.Player;
using VRageMath;

namespace SpaceEngineersVR.Diagnostics
{
    internal static class HudProfileTests
    {
        private static void Require(bool value,string message) {if(!value) throw new InvalidOperationException(message);}
        internal static SurfaceView Panel(PluginConfig c)
        {
            WristPanel.Show(0);
            var keys=WristPanel.Keys(null,false,false,false,true,null).Take(4).ToList();
            WristHud.Keys(keys,c);
            return new SurfaceView {Id="HUD profile fixture",Style=SurfaceStyle.WristMenu,Width=.4f,Height=.225f,
                Pose=MatrixD.CreateTranslation(0,0,-.6),HudSettings=WristHud.Snapshot(c),Keys=keys.ToArray()};
        }
        private static void Press(PluginConfig c,string label)
        {
            var panel=Panel(c); var key=panel.Keys.Single(k=>k.Label==label);
            Require(key.Enabled && panel.KeyAt(key.Bounds.Center)>=0,"HUD action is not reachable: "+label);
            key.Action.Run();
        }
        public static void Run(Action<string> log)
        {
            var oldDefaults=new PluginConfig {HudProfiles=Enumerable.Range(0,4).Select(i=>new HudProfile {Vitals=i>0,Markers=Math.Max(0,i-1),IconScale=1.25f,TextScale=.875f,Group=false}).ToArray()};
            oldDefaults.InitializeHudProfiles();
            Require(oldDefaults.HudProfiles.Select(p=>p.Markers).SequenceEqual(new[] {0,1,1,2}) &&
                oldDefaults.HudProfiles.Select(p=>p.Vitals).SequenceEqual(new[] {false,false,true,false}),"Old default states did not migrate");
            Require(oldDefaults.HudProfiles.All(p=>p.IconScale==1.25f && p.TextScale==.875f && p.Group),"Migration lost sizing or retained ungrouped states");
            Require(oldDefaults.HudProfiles.Select(p=>p.HitMarkers).SequenceEqual(new[] {false,true,true,true}),"Hit markers did not migrate off only for the empty state");
            var vitalsOnly=new PluginConfig {HudProfileVersion=2,HudProfiles=Enumerable.Range(0,4).Select(i=>new HudProfile {Vitals=i!=1,Markers=1}).ToArray()};
            vitalsOnly.InitializeHudProfiles();
            Require(vitalsOnly.HudProfiles.All(p=>p.Notifications==p.Vitals && p.Chat==p.Vitals),"Notices and chat did not migrate to follow each state's vitals");
            var c=new PluginConfig(); c.InitializeHudProfiles(); WristHud.Open(c);
            Require(c.HudProfiles.Select(p=>p.HitMarkers).SequenceEqual(new[] {false,true,true,true}),"Default hit markers are not off only for the empty state");
            Require(c.HudProfiles.All(p=>p.Notifications==p.Vitals && p.Chat==p.Vitals),"Default notices and chat do not follow vitals");
            Press(c,"2  Markers"); Press(c,"Notifications"); Press(c,"Chat");
            Require(c.ShowNotifications && c.ShowChat && c.HudProfiles[1].Notifications && c.HudProfiles[1].Chat && !c.HudProfiles[0].Chat,"Wrist notice and chat toggles did not change only the active state");
            Press(c,"Notifications"); Require(!c.ShowNotifications && c.ShowChat,"Notice toggle changed chat");
            Press(c,"4  Details");
            Require(c.HudProfileIndex==3 && !c.ShowVitals,"Selecting a state did not activate it immediately");
            Press(c,"Hit markers on foot");
            Require(!c.ShowHitMarkers && !c.HudProfiles[3].HitMarkers && c.HudProfiles[2].HitMarkers,"Wrist hit marker toggle did not change only the active state");
            Press(c,"Hit markers on foot");
             Require(!c.ShowVitals && c.WaypointMode==2,"Selected state failed to apply details without vitals");
            c.GroupSignals=false; c.SignalIconScale=2; c.SignalTextScale=1.25f;
            Press(c,"+ State"); Require(c.HudProfiles.Length==5 && c.HudProfileIndex==4,"Adding a state failed to activate it");
             Require(c.HudProfileIndex==4 && !c.ShowVitals && c.WaypointMode==1,"Fifth state unavailable");
            c.ShipCrosshair=false;
            c.WristSignalTint=.6f; c.HudWithVisorOpen=true; c.SignalEdges=false; c.ShowContacts=false;
            c.CycleHud(); Require(c.HudProfileIndex==0 && !c.ShowVitals && c.WaypointMode==0,"Five-state wrap skipped Off");
            c.SelectHudProfile(3); Require(c.GroupSignals && c.SignalIconScale==2 && c.SignalTextScale==1.25f,"Profile edits lost during cycling");
            Require(!c.ShipCrosshair && c.WristSignalTint==.6f && c.HudWithVisorOpen && !c.SignalEdges && !c.ShowContacts,"Global visibility/tint changed on profile selection");
            Press(c,"Distances"); Require(!c.ShowSignalDistances,"Selected state distance toggle ignored");
            c.SelectHudProfile(2); Require(c.ShowSignalDistances,"Distance visibility leaked across profiles"); c.SelectHudProfile(3);
            Require(c.CharacterMarkerRoll && c.FaceMarkersTowardViewer,"HUD orientation defaults lost");
            c.CharacterMarkerRoll=false; Require(!Panel(c).Keys.Any(k=>k.Label=="Grouping"),"Grouping removal failed");
            c.SetupCompleted=true;c.PlayerHeight=1.905f;c.MeasuredEyeHeight=1.837f;
            var serializer=new XmlSerializer(typeof(PluginConfig));
            c.FaceMarkersTowardViewer=false;
            using(var text=new StringWriter())
            {
                serializer.Serialize(text,c);
                string legacy=text.ToString().Replace("<Vitals>","<Tint>0.15</Tint><VisorOpen>false</VisorOpen><Vitals>");
                using(var input=new StringReader(legacy)) c=(PluginConfig)serializer.Deserialize(input);
            }
            Require(c.SetupCompleted && Math.Abs(c.PlayerHeight-1.905f)<.0001f && Math.Abs(c.MeasuredEyeHeight-1.837f)<.0001f,"Setup completion or height references lost after XML round trip");
            c.InitializeHudProfiles(); Require(!c.CharacterMarkerRoll && !c.ShowSignalDistances,"Marker roll or profile distances lost after XML round trip"); Require(c.HudProfiles.Length==5 && c.HudProfileIndex==3 && !c.ShowVitals && c.WaypointMode==2 && c.SignalIconScale==2,"Profile XML round trip lost the active state");
            Require(!c.FaceMarkersTowardViewer,"Explicit marker facing choice lost after XML round trip");
            c.SelectHudProfile(1); Require(!c.ShowNotifications && c.ShowChat,"Notice and chat choices lost after XML round trip"); c.SelectHudProfile(3);
            Require(!c.ShipCrosshair && c.WristSignalTint==.6f && c.HudWithVisorOpen && !c.SignalEdges && !c.ShowContacts,"Legacy profile fields overwrote global XML settings");
            c.SelectHudProfile(4); c.RemoveExtraHudProfile(); Require(c.HudProfiles.Length==4 && c.HudProfileIndex==3,"Removing active extra state left an invalid index");
            c.ResetHudProfile(3); Require(!c.ShowVitals && c.ShowSignalDistances && c.WaypointMode==2 && c.GroupSignals && c.SignalIconScale==1.5f,"Reset state failed");
            c.EditHudProfile(3,p=> {p.Markers=999; p.IconScale=float.NaN; p.TextScale=float.PositiveInfinity;});
            Require(c.WaypointMode==2 && c.SignalIconScale==1.5f && c.SignalTextScale==1,"Invalid profile values escaped normalization");
            WristHud.Open(c);
            var keys=Panel(c).Keys;
            foreach(var k in keys) Require(k.Bounds.X>=0 && k.Bounds.Y>=0 && k.Bounds.Right<=1 && k.Bounds.Bottom<=1,"HUD key leaves panel");
            for(int i=0;i<keys.Length;i++) for(int j=i+1;j<keys.Length;j++) Require(!SignalLayout.Overlaps(keys[i].Bounds,keys[j].Bounds),"HUD touch regions overlap");
            Require(c.WristSignalTint==.6f && c.HudWithVisorOpen && !c.ShowContacts,"Reset state changed global options");
            var turn=new WristKnob.Turn(); turn.Begin(Matrix.Identity,.5f);
            turn.Move(Matrix.CreateRotationZ(-MathHelper.PiOver4));
            Require(Math.Abs(turn.Value-2f/3)<.0001f,"Clockwise knob twist does not darken tint");
            Require(Math.Abs(turn.Wrist.Right.Y+Math.Sin(MathHelper.PiOver4))<.0001f,"Held hand did not turn with knob");
            turn.Move(Matrix.CreateRotationZ(-MathHelper.Pi)); turn.Move(Matrix.CreateRotationZ(MathHelper.PiOver2));
            Require(turn.Value==1,"Knob failed to clamp at its stop across angle wrap");
            turn.Move(Matrix.CreateRotationZ(MathHelper.Pi)); Require(turn.Value<1,"Knob cannot reverse away from its stop");
            turn.Begin(Matrix.CreateRotationX(.7f),.3f); turn.Move(Matrix.CreateRotationX(.9f));
            Require(Math.Abs(turn.Value-.3f)<.0001f,"Out-of-axis hand swing changes tint");
            var touch=new SurfaceTouch();
            Require(touch.Update("ring",new Vector3(0,0,.04f),0)<0 && touch.Update("ring",new Vector3(0,0,.008f),0)==0 &&
                touch.Update("ring",new Vector3(0,0,.005f),0)<0,"Ring poke failed to require withdrawal");
            WristPanel.Reset();
            log("PASS HUD profiles: immediate state selection, global visibility/tint, four/five-state cycle, remove/reset, legacy XML, hit marker and notice migration, wrist hit marker, notice and chat toggles, rotary tint and production touch regions.");
        }
        internal static void Render(Device device,string output,Action<string> log)
        {
            var c=new PluginConfig(); c.InitializeHudProfiles(); WristHud.Open(c);
            using(var scene=new OverlayCanvas("Wrist HUD settings scenarios",1600,1000,1,false,device))
            {
                foreach(string scenario in new[] {"display","markers","details","fifth","large","small"})
                {
                    if(scenario=="markers") Press(c,"2  Markers");
                    if(scenario=="details") Press(c,"4  Details");
                    if(scenario=="fifth") {Press(c,"+ State");}
                    if(scenario=="large") c.EditHudProfile(4,p=> {p.IconScale=2.5f; p.TextScale=1.5f;});
                    if(scenario=="small") c.EditHudProfile(4,p=> {p.IconScale=.75f; p.TextScale=.75f; p.Vitals=false; p.Markers=0;});
                    var panel=Panel(c);
                    scene.Clear(Color.FromArgb(255,6,11,17)); scene.Upload();
                    PhysicalSurface.Draw(scene.Texture,new[] {panel},MatrixD.Identity,VrMath.Projection(-.4f,.4f,-.25f,.25f,.05),null);
                    UiTests.Save(scene.Texture,Path.Combine(output,"hud-wrist-"+scenario+".png"));
                }
            }
            WristHud.Open(c);
            UiTests.Save(MenuHands.PreviewGlove(device,true,0,new Vector3(0,-1,0),1,preview:(panels,head)=> {
                var target=panels.First(p=>p.Style==SurfaceStyle.WristMenu); var source=Panel(c);
                target.SignalWindow=false; target.HudSettings=source.HudSettings; target.Keys=source.Keys;
            }),Path.Combine(output,"hud-wrist-glove.png"));
            WristPanel.Reset();
            log("PASS production wrist HUD renders: single page, details, fifth state, size limits and installed glove mount.");
        }
    }
}
#endif
