using System;
using System.Linq;
using System.Runtime.Serialization;
using HarmonyLib;
using Sandbox.Game.Entities;
using SpaceEngineersVR.Multiplayer;
using SpaceEngineersVR.Player;
using SpaceEngineersVR.Player.Control;
using SpaceEngineersVR.Patches;
using VRageMath;

namespace SpaceEngineersVR.Diagnostics
{
    internal static class TargetingTests
    {
        private static void Require(bool value,string message) { if(!value) throw new Exception(message); }
        private static bool SkipTriangle() => false;
        private static bool SkipInputUpdate() => false;
        private static void SearchInvocationTests(Action<string> log)
        {
            var field=AccessTools.Field(typeof(ActionCatalog),"history");
            var previous=field.GetValue(null);
            bool menuOpen=Plugin.Main.MenuOpen;
            var history=new ActionHistory();
            var harmony=new Harmony("SEVR.SearchInvocationFixture");
            try
            {
                field.SetValue(null,history);
                harmony.Patch(AccessTools.Method(typeof(InputRouter),nameof(InputRouter.Update)),
                    prefix:new HarmonyMethod(typeof(TargetingTests),nameof(SkipInputUpdate)));
                int invoked=0;
                var first=new ActionChoice("First",()=>invoked++,opensMenu:true);
                var second=new ActionChoice("Second",()=>invoked++,opensMenu:true);
                GameActions.Execute(first);
                Require(invoked==1 && history.Keys.Length==0,"Ordinary action entered Search history");
                GameActions.Execute(first,fromSearch:true);
                GameActions.Execute(second,fromSearch:true);
                GameActions.Execute(first);
                Require(invoked==4 && history.Keys.SequenceEqual(new[] {"Second","First"}),"Ordinary action reordered Search clicks");
                GameActions.Execute(new ActionChoice("Disabled",()=>invoked++,enabled:()=>false),fromSearch:true);
                try { GameActions.Execute(new ActionChoice("Failed",()=>throw new InvalidOperationException()),fromSearch:true); }
                catch(InvalidOperationException) { }
                Require(invoked==4 && history.Keys.SequenceEqual(new[] {"Second","First"}),"Unexecuted Search result entered history");
                GameActions.Execute(first,fromSearch:true);
                Require(history.Keys.SequenceEqual(new[] {"First","Second"}),"Repeated Search click did not move to the front");
            }
            finally { harmony.UnpatchAll(harmony.Id); field.SetValue(null,previous); Plugin.Main.MenuOpen=menuOpen; }
            log("PASS Search clicks record successful actions; wheel/hotkey, disabled and failed actions leave recents unchanged");
        }
        internal static void Run(Action<string> log)
        {
            var input=new SelectionInput();
            input.Arm();
            for(int i=0;i<12;i++) Require(!input.Update(true,true,false,false),"Menu trigger confirmed a world selection");
            Require(!input.Update(true,false,false,false) && input.Update(true,true,false,false),"Fresh selection pull did not confirm");
            Require(!input.Update(true,true,false,false),"Held selection trigger repeated");
            Require(!input.Update(true,true,true,false),"Selection clicked through UI");
            Require(!input.Update(true,true,false,false),"UI release leaked a held trigger");
            input.Update(true,false,false,false);
            Require(input.Update(true,true,false,false),"Selection failed to rearm after UI withdrawal");
            input.Update(true,false,false,true);
            Require(!input.Active && !input.Update(true,true,false,false),"B cancellation left a live selection");
            foreach(bool tracking in new[] {true,false})
            {
                input.Arm(); input.Update(tracking,false,false,false);
                if(tracking) input.Update(false,true,false,false);
                Require(!input.Active,"Tracking/context interruption retained selection");
            }
            log("PASS selection menu-release, one-pull, UI capture/rearm, B and interruption gates");
            var history=new ActionHistory(new[] {"old","old",null,""});
            for(int i=0;i<30;i++) history.Record("item"+i);
            history.Record("item25");
            Require(history.Keys.Length==20 && history.Keys[0]=="item25" && history.Keys.Distinct().Count()==20,"Recent action history did not deduplicate or bound storage");
            history=new ActionHistory(new[] {"missing","Copy","Delete"});
            var freshCopy=new ActionChoice("Copy",()=>{});
            var choices=new[] {new ActionChoice("Blueprints",()=>{}),new ActionChoice("Delete",()=>{}),freshCopy,new ActionChoice("Pause",()=>{})};
            Require(history.Order(choices).Select(a=>a.Label).SequenceEqual(new[] {"Copy","Delete","Blueprints","Pause"}) &&
                ReferenceEquals(history.Order(choices)[0],freshCopy),"Recents retained stale actions or disturbed unused ordering");
            var dynamic=new ActionChoice(()=>"Current mode",()=>{},historyKey:"mode");
            history.Record(dynamic.HistoryKey);
            Require(ReferenceEquals(history.Order(choices.Concat(new[] {dynamic}).ToArray())[0],dynamic),"A changing label lost its recent action identity");
            var settings=new Config.PluginConfig {RecentSearchActions=history.Keys};
            var serializer=new System.Xml.Serialization.XmlSerializer(typeof(Config.PluginConfig));
            using(var xml=new System.IO.StringWriter())
            {
                serializer.Serialize(xml,settings);
                using(var reader=new System.IO.StringReader(xml.ToString()))
                    Require(((Config.PluginConfig)serializer.Deserialize(reader)).RecentSearchActions.SequenceEqual(history.Keys),"Recent actions did not survive settings serialization");
            }
            log("PASS recent action ordering, bounded persistence, fresh context choices and changing labels");
            using(var reader=new System.IO.StringReader("<PluginConfig><RecentActions><string>old-wheel-action</string></RecentActions></PluginConfig>"))
                Require(((Config.PluginConfig)serializer.Deserialize(reader)).RecentSearchActions.Length==0,"Mixed legacy history entered Search recents");
            SearchInvocationTests(log);
            var recent=history.Recent(choices,9);
            Require(recent.Select(a=>a.Label).SequenceEqual(new[] {"Copy","Delete"}),"Recent wheel included missing or unused actions");
            var emptyPages=ToolbarWheel.WithRecents(new[] {choices},Array.Empty<ActionChoice>());
            Require(emptyPages.Length==2 && ReferenceEquals(emptyPages[0],choices) && emptyPages[1].Length==9 && emptyPages[1].All(a=>a==null),"Empty recent page changed preceding actions");
            var filledPages=ToolbarWheel.WithRecents(new[] {choices},Enumerable.Range(0,15).Select(i=>new ActionChoice("Recent"+i,()=>{})).ToArray());
            Require(filledPages[1].Length==9 && filledPages[1][8].Label=="Recent8","Recent wheel did not fit one page");
            log("PASS recent wheel resolves current Search identities, excludes unused actions, retains an empty final page and limits to nine entries");
            var spectatorEntries=ActionCatalog.CommonEntries(true);
            Require(spectatorEntries.Count(a=>a.HistoryKey=="Pause")==1 && spectatorEntries.Count(a=>a.HistoryKey=="Camera mode")==1 &&
                spectatorEntries.Count(a=>a.HistoryKey=="Reset view")==1,"Spectator catalog contains duplicate common actions");
            Require(spectatorEntries.Single(a=>a.HistoryKey=="Reset view").Run==(Action)SpectatorView.ResetView &&
                ActionCatalog.CommonEntries(false).Single(a=>a.HistoryKey=="Reset view").Run==(Action)ThirdPersonView.ResetView,
                "Search resolved reset to the wrong camera context");
            var spectatorHistory=new ActionHistory(new[] {"Reset view","Pause","Camera mode"});
            Require(spectatorHistory.Recent(spectatorEntries,9).Length==3,"Duplicate spectator actions consumed recent wheel slots");
            log("PASS spectator Search and recents resolve one action per identity with the current camera reset");
            var savedView=(Diorama)AccessTools.Field(typeof(SpectatorView),"view").GetValue(null);
            var slotTools=(Sandbox.Game.SessionComponents.MySessionComponentSpectatorTools)FormatterServices.GetUninitializedObject(typeof(Sandbox.Game.SessionComponents.MySessionComponentSpectatorTools));
            var otherTools=(Sandbox.Game.SessionComponents.MySessionComponentSpectatorTools)FormatterServices.GetUninitializedObject(typeof(Sandbox.Game.SessionComponents.MySessionComponentSpectatorTools));
            try
            {
                savedView.Fit(8000,MatrixD.Identity,Vector3D.Zero);
                SpectatorView.SaveTrackedScale(slotTools);
                savedView.ChangeScale(.1,Vector3D.Zero);
                SpectatorView.RestoreTrackedScale(otherTools);
                Require(Math.Abs(savedView.UnitsPerMeter-1000)<1e-6,"Tracked scale leaked to another session component");
                SpectatorView.Reset();
                SpectatorView.RestoreTrackedScale(slotTools);
                Require(Math.Abs(savedView.UnitsPerMeter-10000)<1e-6,"Tracked scale was lost after leaving spectator");
                savedView.ChangeScale(.1,Vector3D.Zero);
                SpectatorView.Reset(forgetTracked:true);
                SpectatorView.RestoreTrackedScale(slotTools);
                Require(Math.Abs(savedView.UnitsPerMeter-1000)<1e-6,"World reset retained a previous tracked scale");
            }
            finally { SpectatorView.Reset(forgetTracked:true); savedView.Fit(.8,MatrixD.Identity,Vector3D.Zero); }
            log("PASS saved spectator scale restores after zoom and camera exit, excludes other sessions and clears on world reset");
            var origin=new Vector3D(1e8,-2e8,3e8);
            var ray=new LineD(origin,origin+new Vector3D(.6,0,-.8)*1000);
            var packet=DampenerRequests.Packet(17,23,41,ray);
            Require(DampenerRequests.Decode(packet,out var parsed) && parsed.Token==17 && parsed.Owner==23 && parsed.Target==41 &&
                parsed.Start==origin && Vector3D.Distance(parsed.Direction,ray.Direction)<1e-12,"Dampener target transport lost identity or ray precision");
            for(int n=0;n<packet.Length;n++) Require(!DampenerRequests.Decode(packet.Take(n).ToArray(),out _),"Truncated dampener request accepted");
            Require(!DampenerRequests.Decode(packet.Concat(new byte[] {0}).ToArray(),out _),"Trailing dampener request accepted");
            foreach(int offset in new[] {0,1})
            {
                var bad=(byte[])packet.Clone(); bad[offset]=255;
                Require(!DampenerRequests.Decode(bad,out _),"Unknown dampener protocol accepted");
            }
            foreach(var invalid in new[] {Vector3D.Zero,new Vector3D(double.NaN,0,1),new Vector3D(double.PositiveInfinity,0,1),new Vector3D(0,0,-2)})
                Require(!DampenerRequests.ValidRay(origin,invalid),"Invalid dampener direction accepted");
            Require(!DampenerRequests.Decode(DampenerRequests.Packet(0,23,0,ray),out _),"Untracked dampener request accepted");
            Require(!DampenerRequests.Decode(DampenerRequests.Packet(17,0,0,ray),out _),"Entityless dampener request accepted");
            Require(DampenerRequests.Owns(23,23) && !DampenerRequests.Owns(0,0) && !DampenerRequests.Owns(23,41) && !DampenerRequests.Owns(23,0),
                "Dampener authority accepted an uncontrolled or different player's entity");
            log("PASS dampener request transport at large coordinates, malformed packets and invalid rays");
            var head=Matrix.CreateFromYawPitchRoll(.4f,-.2f,.1f)*Matrix.CreateTranslation(.3f,1.5f,-.4f);
            var anchor=MatrixD.CreateFromYawPitchRoll(.9,-.1,.2); anchor.Translation=origin;
            var spectator=new CameraRig.Frame(anchor,Matrix.Invert(head),1000001,1,true);
            var eye=(MatrixD)head*spectator.TrackingToWorld;
            Require(Vector3D.Distance(eye.Translation,origin)<1e-5 && Vector3D.Distance(eye.Forward,anchor.Forward)<1e-5,"Spectator entry did not anchor the actual VR eye");
            var shifted=head; shifted.Translation+=new Vector3(.2f,0,0);
            var actual=(MatrixD)shifted*spectator.TrackingToWorld;
            Require(Vector3D.Distance(actual.Translation,eye.Translation)>.19,"Spectator room movement lost teleport destination");
            log("PASS spectator eye anchor and room movement destination");
            var navigation=new Diorama(); navigation.Fit(.8,MatrixD.Identity,Vector3D.Zero);
            navigation.ChangeScale(10000,Vector3D.Zero);
            var basis=anchor;
            var applied=navigation.Anchor(basis.Translation,basis.GetOrientation());
            Require(SpectatorView.AdvanceReference(basis,applied,applied)==basis,"Spectator reapplied its committed gesture offset");
            var native=applied; native.Translation+=new Vector3D(700,0,-300);
            var advanced=SpectatorView.AdvanceReference(basis,applied,native);
            Require(Vector3D.Distance(advanced.Translation,basis.Translation+new Vector3D(700,0,-300))<1e-5,"Native spectator follow did not advance the navigation reference");
            navigation.Input(true,0,0);
            navigation.Input(true,1,1);
            var l=MatrixD.CreateTranslation(-.2,-.2,-.4); var r=MatrixD.CreateTranslation(.2,-.2,-.4);
            navigation.Move(l,r,1d/90);
            var before=navigation.Anchor(advanced.Translation,advanced.GetOrientation());
            for(int i=0;i<30;i++) { l.Translation+=new Vector3D(.003,0,0); r.Translation+=new Vector3D(.003,0,0); navigation.Move(l,r,1d/90); }
            var moved=navigation.Anchor(advanced.Translation,advanced.GetOrientation());
            Require(Vector3D.Distance(moved.Translation,before.Translation)>400 && navigation.UnitsPerMeter>9000,"Existing spectator pan did not cover long distances at zoom scale");
            Require(SpectatorView.AdvanceReference(advanced,moved,moved)==advanced,"Committed pan was applied again on the next spectator tick");
            Require(!navigation.Input(false,1,1) && !navigation.Held && !navigation.Coasting,"Spectator menu capture left travel active");
            var follower=new ObserverFollow();
            follower.Reset(basis,Vector3D.Up);
            var adopted=new Diorama(); adopted.Fit(8000,MatrixD.Identity,Vector3D.Zero);
            var targetCenter=origin+new Vector3D(50,20,10);
            adopted.SetAnchor(moved,targetCenter,follower.Reference(ObserverMode.Ship));
            Require(Vector3D.Distance(adopted.Anchor(targetCenter,follower.Reference(ObserverMode.Ship)).Translation,moved.Translation)<.0001 && adopted.UnitsPerMeter==10000,"Entering follow changed view or travel scale: distance="+Vector3D.Distance(adopted.Anchor(targetCenter,follower.Reference(ObserverMode.Ship)).Translation,moved.Translation)+", scale="+adopted.UnitsPerMeter);
            var oldReference=follower.Reference(ObserverMode.Ship);
            var fixedReference=follower.Reference(ObserverMode.Fixed);
            var beforeMode=adopted.Anchor(targetCenter,oldReference);
            adopted.ChangeReference(oldReference,fixedReference);
            var afterMode=adopted.Anchor(targetCenter,fixedReference);
            Require(Vector3D.Distance(beforeMode.Translation,afterMode.Translation)<.001 && Vector3D.Distance(beforeMode.Forward,afterMode.Forward)<1e-6,"Follow camera mode changed the current view");
            var shiftedFollow=adopted.Anchor(targetCenter+new Vector3D(100,-30,10),fixedReference);
            Require(Vector3D.Distance(shiftedFollow.Translation-afterMode.Translation,new Vector3D(100,-30,10))<1e-5,"Follow translation was scaled or applied twice");
            var followTracking=Matrix.CreateTranslation(.2f,1.7f,-.1f);
            adopted.FitTarget(80,basis,Vector3D.Up,followTracking,oldReference,true);
            var fitted=adopted.Anchor(targetCenter,oldReference);
            var fittedEye=VrMath.Affine(followTracking)*MatrixD.CreateScale(adopted.UnitsPerMeter)*fitted;
            var fittedExpected=targetCenter-basis.Forward*115+basis.Up*20;
            Require(Vector3D.Distance(fittedEye.Translation,fittedExpected)<.001 && Vector3D.Distance(fitted.Forward,basis.Forward)<1e-6,"Follow reset did not use third-person framing and scale");
            log("PASS shared follow framing, continuous adoption/mode changes and unscaled target translation at large world coordinates");
            Require(!navigation.Input(true,1,1),"Held grips resumed spectator travel after menu capture");
            log("PASS spectator native reference commits, scaled third-person pan and menu-release safety");
            foreach(var axis in new[] {Sandbox.Game.Entities.Cube.MySymmetrySettingModeEnum.XPlane,Sandbox.Game.Entities.Cube.MySymmetrySettingModeEnum.YPlane,Sandbox.Game.Entities.Cube.MySymmetrySettingModeEnum.ZPlane})
            {
                var offset=SymmetrySetupControls.WithOffset(axis,true);
                Require(SymmetrySetupControls.Axis(offset)==axis && SymmetrySetupControls.Offset(offset),"Symmetry axis/offset selection changed axes");

            }
            log("PASS native symmetry axis/offset choices");
            var planeHarmony=new Harmony("SEVR.NativeSymmetryFixture");
            var debugTriangle=AccessTools.Method(typeof(VRageRender.MyRenderProxy),nameof(VRageRender.MyRenderProxy.DebugDrawTriangle));
            try
            {
                planeHarmony.CreateClassProcessor(typeof(SymmetryPlanePatch)).Patch();
                planeHarmony.CreateClassProcessor(typeof(SymmetryTrianglePatch)).Patch();
                planeHarmony.Patch(debugTriangle,prefix:new HarmonyMethod(typeof(TargetingTests),nameof(SkipTriangle)) {priority=Priority.Last});
                foreach(var axis in new[] {Sandbox.Game.Entities.Cube.MySymmetrySettingModeEnum.XPlane,Sandbox.Game.Entities.Cube.MySymmetrySettingModeEnum.YPlane,Sandbox.Game.Entities.Cube.MySymmetrySettingModeEnum.ZPlane})
                {
                    foreach(bool half in new[] {false,true})
                    {
                        SymmetryRenderTests.Capture(SymmetrySetupControls.WithOffset(axis,half));
                        Require(SymmetryPlanes.Current.Length==2,"Native symmetry plane triangles were lost or duplicated");
                        var plane=SymmetryPlanes.Current[0];
                        int coordinate=axis==Sandbox.Game.Entities.Cube.MySymmetrySettingModeEnum.XPlane ? 0:axis==Sandbox.Game.Entities.Cube.MySymmetrySettingModeEnum.YPlane ? 1:2;
                        double expected=half ? 2.5*.50025*(coordinate==2 ? 1:-1):0;
                        Require(Math.Abs(plane.A.GetDim(coordinate)-expected)<1e-5 && Math.Abs(plane.B.GetDim(coordinate)-expected)<1e-5 &&
                            Math.Abs(plane.Color.A/255d-.1)<.01,"Captured plane geometry or art differs from the installed game");
                    }
                }
                var unchanged=SymmetryPlanes.Current;
                debugTriangle.Invoke(null,new object[] {Vector3D.Zero,Vector3D.UnitX,Vector3D.UnitY,Color.White,false,true,false});
                SymmetryPlanes.Commit();
                Require(SymmetryPlanes.Current.Length==unchanged.Length,"Unrelated debug triangles entered symmetry capture");
            }
            finally { SymmetryPlanes.Capturing=false; SymmetryPlanes.Begin(); planeHarmony.UnpatchAll(planeHarmony.Id); }
            log("PASS installed-game symmetry geometry/art capture for all axes and offsets; unrelated debug geometry excluded");

            var nativeCamera=AccessTools.Field(typeof(Sandbox.Game.World.MySector),"m_camera");
            var capture=AccessTools.Field(typeof(SpectatorView),"<CapturePose>k__BackingField");
            var previousCamera=nativeCamera.GetValue(null);
            var targetCamera=(VRage.Game.Utils.MyCamera)FormatterServices.GetUninitializedObject(typeof(VRage.Game.Utils.MyCamera));
            targetCamera.WorldMatrix=MatrixD.CreateTranslation(-1,-2,-3);
            var subject=new VRage.Game.Entity.MyEntity();
            subject.EntityId=71; subject.DisplayName="Tracking fixture";
            subject.WorldMatrix=MatrixD.CreateTranslation(origin+new Vector3D(20,0,0));
            var tools=(Sandbox.Game.SessionComponents.MySessionComponentSpectatorTools)FormatterServices.GetUninitializedObject(typeof(Sandbox.Game.SessionComponents.MySessionComponentSpectatorTools));
            var trackingHarmony=new Harmony("SEVR.SpectatorTrackingFixture");
            try
            {
                trackingHarmony.CreateClassProcessor(typeof(SpectatorTargetPosePatch)).Patch();
                nativeCamera.SetValue(null,targetCamera); capture.SetValue(null,(MatrixD?)anchor);
                tools.SetTarget(subject);
                var state=AccessTools.Field(tools.GetType(),"m_cameraState").GetValue(tools);
                var local=(MatrixD)AccessTools.Field(state.GetType(),"LocalMatrix").GetValue(state);
                var vector=(Vector3D)AccessTools.Field(state.GetType(),"LocalVector").GetValue(state);
                var trackedAnchor=local*subject.WorldMatrix;
                var trackedEye=(MatrixD)shifted*new CameraRig.Frame(trackedAnchor,spectator.OriginInverse).TrackingToWorld;
                Require(Vector3D.Distance(trackedEye.Translation,actual.Translation)<1e-5 && Vector3D.Distance(trackedEye.Forward,actual.Forward)<1e-5 &&
                    Vector3D.Distance(vector,anchor.Translation-subject.PositionComp.WorldVolume.Center)<1e-5,"Native follow lock changed the VR eye or reused a stale camera pose");
                Require(targetCamera.WorldMatrix.Translation==new Vector3D(-1,-2,-3),"Follow lock mutated the shared game camera");
            }
            finally { capture.SetValue(null,null); nativeCamera.SetValue(null,previousCamera); trackingHarmony.UnpatchAll(trackingHarmony.Id); }
            log("PASS installed spectator target capture: fresh anchor, native local offsets, unchanged VR eye and shared camera");
            var regular=GameActions.WheelActions(false,false,false);
            Require(regular.Take(9).Contains(GameActions.BlueprintsAction) && !regular.Contains(GameActions.Options),"Regular wheel blueprint/options placement regressed");
            var building=GameActions.BuildingWheelActions(false);
            Require(building.Take(9).Contains(GameActions.SymmetryAction) && building.Take(9).Contains(GameActions.SymmetrySetupAction),"Building wheel lacks symmetry controls");
            var setup=GameActions.BuildingWheelActions(true);
            Require(setup.Length==9 && setup.Take(9).Contains(GameActions.ExitSymmetryAction) && new[] {"X plane","Y plane","Z plane","Remove plane"}.All(label=>setup.Any(a=>a.Label==label)),"Symmetry setup actions do not fit the first page");
            log("PASS regular/building/symmetry wheel availability and paging");
            var harmony=new Harmony("SEVR.SelectedTargetFixture");
            var executing=AccessTools.Field(typeof(GridSelection),"<Executing>k__BackingField");
            var confirmed=AccessTools.Field(typeof(GridSelection),"<Confirmed>k__BackingField");
            var grid=(MyCubeGrid)FormatterServices.GetUninitializedObject(typeof(MyCubeGrid));
            try
            {
                harmony.CreateClassProcessor(typeof(SelectedEntityPatch)).Patch();
                harmony.CreateClassProcessor(typeof(SelectedGridPatch)).Patch();
                executing.SetValue(null,true); confirmed.SetValue(null,grid);
                Require(ReferenceEquals(MyCubeGrid.GetTargetEntity(),grid) && ReferenceEquals(MyCubeGrid.GetTargetGrid(),grid),
                    "Native target methods replaced the confirmed grid with camera/placement targeting");
                confirmed.SetValue(null,null);
                Require(MyCubeGrid.GetTargetEntity()==null && MyCubeGrid.GetTargetGrid()==null,"Missing confirmed target fell through to a different native ray");
            }
            finally { executing.SetValue(null,false); confirmed.SetValue(null,null); harmony.UnpatchAll(harmony.Id); }
            log("PASS installed native entity/grid targeting uses only the scoped confirmation");
        }
    }
}
