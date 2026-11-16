#if SEVR_DIAGNOSTICS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using HarmonyLib;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.Gui;
using Sandbox.Game.GUI.HudViewers;
using Sandbox.Game.Weapons;
using Sandbox.Game.World;
using Sandbox.Game.GameSystems;
using Sandbox.Game.Multiplayer;
using SpaceEngineersVR.Multiplayer;
using SpaceEngineersVR.Player;
using VRage.Game;
using VRageMath;

namespace SpaceEngineersVR.Diagnostics
{
    public static class Warfare3Tests
    {
        private static bool disrupted;
        private static Sandbox.Game.Entities.IMyControllableEntity controlled;
        private static bool panning,activeCamera,working;
        private static Vector2 cameraAim;
        private static float cameraZoom;
        private static readonly List<string> cameraCalls=new List<string>();
        private static bool hostile,creative,inRange;
        private static int rotation;
        private static bool Emp(ref bool __result) { __result=disrupted; return false; }
        private static bool NoLadder(ref bool __result) { __result=false; return false; }
        private static bool Head(ref MatrixD __result) { __result=MatrixD.Identity; return false; }
        private static bool Rotation(ref int __result) { __result=rotation; return false; }
        private static bool Multiplier(ref float __result) { __result=1; return false; }
        private static bool LookAt(ref Vector3 __result) { __result=new Vector3(.001f,.004f,0); return false; }
        private static bool Hostile(ref bool __result) { __result=hostile; return false; }
        private static bool Creative(ref bool __result) { __result=creative; return false; }
        private static bool InRange(ref bool __result) { __result=inRange; return false; }
        private static bool Steam(ref ulong __result) { __result=7; return false; }
        private static bool Controlled(ref Sandbox.Game.Entities.IMyControllableEntity __result) { __result=controlled; return false; }
        private static bool Panning(ref bool __result) { __result=panning; return false; }
        private static bool ActiveCamera(ref bool __result) { __result=activeCamera; return false; }
        private static bool Working(ref bool __result) { __result=working; return false; }
        private static bool Aim(ref Vector2 aim,ref float zoom) { aim=new Vector2(-3,4); zoom=.5f; return false; }
        private static bool CameraRotate(Vector2 __0) { cameraAim=__0; cameraCalls.Add("pan"); return false; }
        private static bool CameraZoom(float __0) { cameraZoom=__0; return false; }
        private static bool Move(Vector3 __0,Vector2 __1,float __2)
        { Require(__0==Vector3.Zero && __1==Vector2.Zero && __2==0,"Camera panning retained movement"); cameraCalls.Add("neutral"); return false; }
        private static bool Stop() { cameraCalls.Add("stop"); return false; }
        private static bool ReleaseCamera() { cameraCalls.Add("release"); return false; }
        private static bool RouteCamera() { cameraCalls.Add("route"); return false; }
        private static int clock;
        private static bool Clock(ref int __result) { __result=clock; return false; }
        internal static void WithEmpClock(int milliseconds,Action action)
        {
            var harmony=new Harmony("SEVR.Warfare3.EmpClock");
            try
            {
                clock=milliseconds;
                harmony.Patch(AccessTools.PropertyGetter(typeof(Sandbox.MySandboxGame),nameof(Sandbox.MySandboxGame.TotalGamePlayTimeInMilliseconds)),prefix:new HarmonyMethod(typeof(Warfare3Tests),nameof(Clock)));
                action();
            }
            finally { harmony.UnpatchAll(harmony.Id); }
        }
        internal static void EmpReadings(EssentialHud.View view,Action paint)
        {
            WithEmpClock(1320,()=> {
                var notices=view.Notices;
                float health=MyHudEmpDistortion.ScrambleReading(view.Levels[0],0,1,(int)VRage.Utils.MyStringHash.GetOrCompute("player_health"));
                HudEmp.Apply(view);
                Require(view.Levels[0]==health && ReferenceEquals(notices,view.Notices),"EMP differs from native reading noise or changes notices");
                paint();
            });
        }
        private static void EmpSlices(Action<string> log)
        {
            var original=new NativeSprite("atlas",new RectangleF(30,40,24,36),new Vector4(.1f,.2f,.3f,.4f)) {UV=new Vector4(.2f,.3f,.4f,.5f),NativeGui=true,Premultiplied=true};
            var slices=HudEmp.Slices(original,33,.5f).ToArray();
            var repeated=HudEmp.Slices(original,33,.5f).ToArray();
            var later=HudEmp.Slices(original,34,.5f).ToArray();
            Require(slices.Length==6 && slices.Where((s,i)=>s.Bounds.X!=later[i].Bounds.X).Any(),"EMP icon tearing is missing or frozen");
            for(int i=0;i<6;i++)
            {
                var s=slices[i];
                Require(s.Bounds==repeated[i].Bounds && s.UV==repeated[i].UV,"Stereo EMP slices disagree");
                Require(Math.Abs(s.Bounds.X-original.Bounds.X)<=2 && s.Bounds.Y==40+i*6 && s.Bounds.Height==6 && s.Bounds.Width==24,"EMP slice geometry has gaps or exceeds native displacement");
                Require(Math.Abs(s.UV.Y-(.3f+.5f*i/6))<1e-6 && Math.Abs(s.UV.W-.5f/6)<1e-6 && s.UV.X==.2f && s.UV.Z==.4f,"EMP slice lost its atlas crop");
                Require(s.Tint==original.Tint && s.Path==original.Path && s.NativeGui && s.Premultiplied,"EMP slice lost native rendering flags");
            }
            var recovered=HudEmp.Slices(original,null,.5f).Single();
            Require(recovered.Bounds==original.Bounds && recovered.UV==original.UV,"EMP recovery retained icon tearing");
            log("PASS EMP icon tearing: cropped atlas coverage, bounded displacement, stable stereo slices, animation and intact recovery");
        }
        internal static void EmpCanvas(OverlayCanvas canvas,Action<string> log)
        {
            var sprites=(List<NativeSprite>)AccessTools.Field(typeof(OverlayCanvas),"icons").GetValue(canvas);
            var original=new NativeSprite("atlas",new RectangleF(10,20,24,36),Vector4.One);
            WithEmpClock(1320,()=> {
                canvas.Clear(System.Drawing.Color.Transparent);
                var expected=new List<NativeSprite>();
                var offsets=new List<Vector2>();
                canvas.Distort(HudEmp.Tick,.5f,()=> {
                    foreach(int x in new[] {55,155,255,355})
                    {
                        var offset=MyHudEmpDistortion.GetPositionJitter(new Vector2(x,422),.5f); offsets.Add(offset);
                        foreach(var slice in HudEmp.Slices(original,HudEmp.Tick,.5f))
                        { var s=slice; s.Bounds.X+=offset.X; s.Bounds.Y+=offset.Y; expected.Add(s); }
                        canvas.Instrument(x,422,()=>canvas.Sprite(original));
                    }
                });
                Require(offsets.Distinct().Count()>1,"EMP controls moved as one cluster");
                Require(sprites.Count==expected.Count && sprites.Where((s,i)=>s.Bounds!=expected[i].Bounds || s.UV!=expected[i].UV).Count()==0,"Canvas EMP disagrees with native per-control jitter or slice placement");
                try { canvas.Distort(HudEmp.Tick,.5f,()=> { throw new InvalidOperationException(); }); }
                catch(InvalidOperationException) { }
                canvas.Instrument(55,422,()=>canvas.Sprite(original));
                Require(sprites.Count==expected.Count+1 && sprites.Last().Bounds==original.Bounds,"EMP leaked into notices or a subsequent paint");
            });
            log("PASS EMP canvas: independent native control jitter, icon placement and scope restoration after normal/failed paints");
        }
        internal static HudEmp.Frame EmpPhantoms()
        {
            var harmony=new Harmony("SEVR.Warfare3.Phantoms");
            var field=AccessTools.Field(typeof(MyHudEmpDistortion),"m_markers"); var markers=(Array)field.GetValue(null); var saved=(Array)markers.Clone();
            var type=markers.GetType().GetElementType(); var visible=AccessTools.Field(type,"Visible"); var position=AccessTools.Field(type,"WorldPosition");
            var previous=HudEmp.Current; bool previousDisrupted=disrupted;
            try
            {
                disrupted=true;
                harmony.Patch(AccessTools.PropertyGetter(typeof(Plugin.Main),nameof(Plugin.Main.VrActive)),prefix:new HarmonyMethod(typeof(Warfare3Tests),nameof(Emp)));
                harmony.Patch(AccessTools.PropertyGetter(typeof(MyHudEmpDistortion),nameof(MyHudEmpDistortion.IsActive)),prefix:new HarmonyMethod(typeof(Warfare3Tests),nameof(Emp)));
                harmony.Patch(AccessTools.PropertyGetter(typeof(HelmetHud),nameof(HelmetHud.NativeSuppressed)),prefix:new HarmonyMethod(typeof(Warfare3Tests),nameof(NoLadder)));
                for(int i=0;i<markers.Length;i++)
                {
                    object item=markers.GetValue(i); visible.SetValue(item,i==0); position.SetValue(item,new Vector3D(0,0,-900)); markers.SetValue(item,i);
                }
                HudEmp.Capture(); var frame=HudEmp.Current;
                Require(frame.Markers.Length==1 && frame.Markers[0].Position==new Vector3D(0,0,-900),"EMP phantom capture changed native visibility/world anchor");
                foreach(float offset in frame.Markers[0].Slices) Require(Math.Abs(offset)<=4f/24,"EMP tearing exceeded native slice displacement");
                disrupted=false; HudEmp.Capture();
                Require(HudEmp.Current==null,"EMP recovery retained phantom markers");
                return frame;
            }
            finally
            {
                harmony.UnpatchAll(harmony.Id); Array.Copy(saved,markers,markers.Length); disrupted=previousDisrupted;
                AccessTools.Field(typeof(HudEmp),"captured").SetValue(null,previous);
            }
        }
        private static T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
        private static void Set(object owner,string name,object value) => AccessTools.Field(owner.GetType(),name).SetValue(owner,value);
        private static void Require(bool value,string message) { if(!value) throw new Exception(message); }
        public static void Run(Action<string> log)
        {
            Dampeners(log);
            Camera(log);
            EmpSlices(log);
            EmpPhantoms();
            log("PASS EMP phantoms: installed native visibility/positions, bounded torn slices, independent capture and recovery");
            var harmony=new Harmony("SEVR.Warfare3.Tests");
            var local=HeldItemPose.Local; var mirrored=HeldItemPose.LocalMirrored;
            bool enabled=HeldItemPose.Enabled;
            try
            {
                harmony.Patch(AccessTools.PropertyGetter(typeof(MyCharacter),nameof(MyCharacter.IsElectricityDisrupted)),prefix:new HarmonyMethod(typeof(Warfare3Tests),nameof(Emp)));
                harmony.Patch(AccessTools.Method(typeof(MyCharacter),nameof(MyCharacter.GetHeadMatrix)),prefix:new HarmonyMethod(typeof(Warfare3Tests),nameof(Head)));
                var character=Empty<MyCharacter>(); var gun=Empty<MyAutomaticRifleGun>();
                AccessTools.Property(typeof(VRage.Game.Entity.MyEntity),"Components").SetValue(character,new VRage.Game.Components.MyEntityComponentContainer(character));
                Set(gun,"m_render",new Sandbox.Game.Components.MyRenderComponent {Model=VRage.Game.Models.MyModels.GetModelOnlyData(@"Models\Weapons\RocketLauncher_Precision.mwm")});
                gun.PhysicalObject=new MyObjectBuilder_PhysicalGunObject {SubtypeName="AdvancedHandHeldLauncherItem"};
                var definition=new MyHandItemDefinition {TargetLockingEnabled=true};
                Set(gun,"m_handItemDef",definition); Set(character,"m_currentWeapon",gun);
                Set(character,"m_zoomMode",MyZoomModeEnum.IronSight);
                disrupted=false; HeldItemPose.Enabled=true;
                var model=MatrixD.CreateRotationY(.7); model.Translation=new Vector3D(.2,.1,-.3);
                HeldItemPose.Local=_=>model; HeldItemPose.LocalMirrored=_=>false;
                Require(character.IsHandWeaponTargetLockingActive && NativeTargeting.Enabled(character),"Native PRO-1 sights did not enable VR lock indicators");
                Require(ReferenceEquals(NativeTargeting.Origin(character),gun),"Handheld lead does not use the installed weapon origin");
                Require(HeldTargeting.TryAim(character,out var aim),"Valid held PRO-1 pose rejected");
                var profile=HeldItemPose.Profile(character);
                Require(Vector3D.Distance(aim.Translation,Vector3D.Transform(profile.Sight.Value,model))<1e-7 &&
                    Vector3D.Distance(aim.Forward,model.Forward)<1e-7,"Lock focus did not follow the held sight");
                Require(HeldTargeting.LockAim(character,true,true,false,false,false)==aim,"Host lock cone differs from local held aim");
                HeldItemPose.Local=_=>MatrixD.CreateTranslation(10,0,0);
                Require(!HeldTargeting.TryAim(character,out _) && HeldTargeting.LockAim(character,true,true,false,false,false)==MatrixD.Identity,"Out-of-reach pose changed native lock aim");
                HeldItemPose.Local=_=>model; HeldItemPose.Enabled=false;
                Require(HeldTargeting.LockAim(character,true,true,false,false,false)==MatrixD.Identity,"Disabled held tools changed native lock aim");
                HeldItemPose.Enabled=true; disrupted=true;
                Require(!NativeTargeting.Enabled(character),"EMP retained handheld lock indicators");
                disrupted=false; Set(character,"m_zoomMode",MyZoomModeEnum.Classic);
                Require(NativeTargeting.Enabled(character) && !character.IsHandWeaponTargetLockingActive,"Lowered handheld sights lost native focus preview or kept acquisition active");
                Set(character,"m_zoomMode",MyZoomModeEnum.IronSight); definition.TargetLockingEnabled=false;
                Require(!NativeTargeting.Enabled(character) && !HeldTargeting.TryAim(character,out _),"Ordinary handheld weapon acquired lock support");
                log("PASS handheld targeting: installed PRO-1 gates, held sight focus and host aim, native lead origin, lowered sights, EMP, ordinary weapons and invalid/disabled pose fallback");

                harmony.Patch(AccessTools.PropertyGetter(typeof(MyLargeTurretBase),nameof(MyLargeTurretBase.ViewRotation)),prefix:new HarmonyMethod(typeof(Warfare3Tests),nameof(Rotation)));
                harmony.Patch(AccessTools.Method(typeof(MyLargeTurretBase),"GetRotationMultiplier"),prefix:new HarmonyMethod(typeof(Warfare3Tests),nameof(Multiplier)));
                harmony.Patch(AccessTools.Method(typeof(MyLargeTurretBase),"LookAt"),prefix:new HarmonyMethod(typeof(Warfare3Tests),nameof(LookAt)));
                var turret=(MyLargeTurretBase)FormatterServices.GetUninitializedObject(AccessTools.TypeByName("SpaceEngineers.Game.Entities.Weapons.MyLargeGatlingTurret"));
                Set(turret,"m_rotationSpeed",.001f); Set(turret,"m_elevationSpeed",.001f);
                foreach(int angle in new[] {0,90,180,270})
                {
                    rotation=angle;
                    var input=TurretHeadAim.Indicator(turret,Vector3D.Forward*100);
                    var mount=MyViewRotation.RotateInput(angle,input);
                    Require(Vector2.Distance(mount,new Vector2(-1.25f,-5))<1e-5,"Native view rotation changed head aim at "+angle);
                }
                log("PASS turret head aim: native screen-to-mount rotation at 0/90/180/270 degrees preserves pitch/yaw convergence");
                harmony.Patch(AccessTools.PropertyGetter(typeof(MyHudEmpDistortion),nameof(MyHudEmpDistortion.AreSignalsFlickeredOff)),prefix:new HarmonyMethod(typeof(Warfare3Tests),nameof(Emp)));
                disrupted=true;
                Require(WorldMarkers.Read(null,MyHudMarkerRender.SignalMode.FullDisplay,Vector3D.Zero,DateTime.UtcNow)==null,"EMP retained collected signals");
                var previous=new WorldMarkers.View(new[] {new WorldMarkers.Marker {Name="stale"}},DateTime.UtcNow) {Lead=new NativeLead.View()};
                var hidden=WorldMarkers.WithWeaponCore(previous);
                Require(hidden.Markers.Length==0 && hidden.Lead==null,"EMP retained cached or WeaponCore markers/lead");
                disrupted=false;
                Require(ReferenceEquals(WorldMarkers.WithWeaponCore(previous),previous),"Signal recovery replaced an intact snapshot");
                log("PASS EMP signals: native flicker clears collected, cached and WeaponCore markers/lead; recovery restores normal capture");
            }
            finally
            {
                harmony.UnpatchAll(harmony.Id); HeldItemPose.Local=local; HeldItemPose.LocalMirrored=mirrored; HeldItemPose.Enabled=enabled; disrupted=false;
            }
        }
        private static void Dampeners(Action<string> log)
        {
            var harmony=new Harmony("SEVR.Warfare3.Dampeners");
            var session=AccessTools.Field(typeof(MySession),"m_static"); var previous=session.GetValue(null);
            try
            {
                var world=Empty<MySession>(); world.Settings=new MyObjectBuilder_SessionSettings(); world.Players=Empty<MyPlayerCollection>();
                session.SetValue(null,world);
                harmony.Patch(AccessTools.Method(typeof(MyPlayer),nameof(MyPlayer.IsGridClusterHostileToIdentity)),prefix:new HarmonyMethod(typeof(Warfare3Tests),nameof(Hostile)));
                harmony.Patch(AccessTools.Method(typeof(MySession),nameof(MySession.CreativeToolsEnabled)),prefix:new HarmonyMethod(typeof(Warfare3Tests),nameof(Creative)));
                harmony.Patch(AccessTools.Method(typeof(MyPlayerCollection),nameof(MyPlayerCollection.TryGetSteamId)),prefix:new HarmonyMethod(typeof(Warfare3Tests),nameof(Steam)));
                harmony.Patch(AccessTools.Method(typeof(MyEntityThrustComponent),nameof(MyEntityThrustComponent.IsInRangeOfRelativeDampening)),prefix:new HarmonyMethod(typeof(Warfare3Tests),nameof(InRange)));
                var character=Empty<MyCharacter>(); var grid=Empty<MyCubeGrid>();
                foreach(bool allowed in new[] {false,true})
                foreach(bool enemy in new[] {false,true})
                foreach(bool tools in new[] {false,true})
                foreach(bool near in new[] {false,true})
                {
                    world.Settings.AllowDampeningToHostileGrids=allowed; hostile=enemy; creative=tools; inRange=near;
                    Require(DampenerRequests.Allowed(character,grid)==(near && (allowed || !enemy || tools)),"Dampener selection/request disagrees with the native world rule");
                }
                world.Settings.AllowDampeningToHostileGrids=false; hostile=true; creative=false; inRange=true;
                Require(DampenerRequests.Allowed(Empty<MyCockpit>(),grid),"Character hostile-grid restriction changed ship dampening");
                Require(!DampenerRequests.Allowed(character,null) && !DampenerRequests.Allowed(null,grid),"Missing dampener owner/target accepted");
            }
            finally { harmony.UnpatchAll(harmony.Id); session.SetValue(null,previous); }
            log("PASS native dampener permissions: friendly/hostile, world option, creative exemption, range, ship exemption and missing entities through the shared selection/server gate");
        }
        private static void Camera(Action<string> log)
        {
            var harmony=new Harmony("SEVR.Warfare3.Camera");
            var session=AccessTools.Field(typeof(MySession),"m_static"); var previous=session.GetValue(null);
            var source=AccessTools.Field(typeof(RemoteView),"source"); var previousSource=source.GetValue(null);
            var pan=AccessTools.Field(typeof(RemoteView),"cameraPanning"); var previousPan=pan.GetValue(null);
            var config=AccessTools.Field(typeof(Plugin.Common),"<Config>k__BackingField"); var previousConfig=config.GetValue(null);
            void Patch(System.Reflection.MethodInfo method,string prefix) => harmony.Patch(method,prefix:new HarmonyMethod(typeof(Warfare3Tests),prefix));
            try
            {
                session.SetValue(null,Empty<MySession>()); config.SetValue(null,new Config.PluginConfig {EnableKeyboardAndMouseControls=false,CameraZoomSensitivity=1});
                controlled=Empty<MyCharacter>(); var camera=Empty<MyCameraBlock>(); source.SetValue(null,camera);
                Patch(AccessTools.PropertyGetter(typeof(MySession),nameof(MySession.ControlledEntity)),nameof(Controlled));
                Patch(AccessTools.PropertyGetter(typeof(MyCameraBlock),nameof(MyCameraBlock.EnablePanning)),nameof(Panning));
                Patch(AccessTools.PropertyGetter(typeof(MyCameraBlock),nameof(MyCameraBlock.IsActive)),nameof(ActiveCamera));
                Patch(AccessTools.PropertyGetter(typeof(MyCubeBlock),nameof(MyCubeBlock.IsWorking)),nameof(Working));
                Patch(AccessTools.Method(typeof(RemoteView),"ReadAim"),nameof(Aim));
                Patch(AccessTools.Method(typeof(MyCameraBlock),nameof(MyCameraBlock.Rotate)),nameof(CameraRotate));
                Patch(AccessTools.Method(typeof(MyCameraBlock),nameof(MyCameraBlock.ChangeZoomPrecise)),nameof(CameraZoom));
                Patch(AccessTools.Method(typeof(MyCharacter),nameof(MyCharacter.MoveAndRotate)),nameof(Move));
                Patch(AccessTools.Method(typeof(MyCharacter),nameof(MyCharacter.MoveAndRotateStopped)),nameof(Stop));
                Patch(AccessTools.Method(typeof(MyShipController),nameof(MyShipController.MoveAndRotate),new[] {typeof(Vector3),typeof(Vector2),typeof(float)}),nameof(Move));
                Patch(AccessTools.Method(typeof(MyShipController),nameof(MyShipController.MoveAndRotateStopped)),nameof(Stop));
                Patch(AccessTools.Method(typeof(RemoteView),nameof(RemoteView.ReleaseInput)),nameof(ReleaseCamera));
                Patch(AccessTools.Method(typeof(InputRouter),nameof(InputRouter.Update)),nameof(RouteCamera));
                panning=activeCamera=working=true; cameraCalls.Clear(); cameraZoom=0;
                pan.SetValue(null,false);
                Require(ReferenceEquals(RemoteView.PannableCamera,camera) && RemoteView.PanningCamera==null,"Camera took flight controls before Pan was selected");
                RemoteView.ControlCamera(10,false);
                Require(cameraCalls.Count==0,"Fly mode sent camera or movement input");
                RemoteView.TogglePanning();
                Require(string.Join(",",cameraCalls)=="release,route","Pan toggle omitted input release/routing"); cameraCalls.Clear();
                Require(ReferenceEquals(RemoteView.PanningCamera,camera) && InputRouter.Flying,"On-foot panning did not select flying action bindings");
                RemoteView.ControlCamera(10,false);
                Require(string.Join(",",cameraCalls)=="neutral,stop,pan" && cameraAim==new Vector2(-3,4) && Math.Abs(cameraZoom-.005f)<1e-7,"Camera input failed neutral-before-pan or shared turret zoom");
                RemoteView.TogglePanning(); cameraCalls.Clear();
                controlled=Empty<MyRemoteControl>();
                RemoteView.ControlCamera(10,false);
                Require(RemoteView.PanningCamera==null && InputRouter.Flying && cameraCalls.Count==0,"Remote control did not retain flight with Pan off");
                RemoteView.TogglePanning(); cameraCalls.Clear(); RemoteView.ControlCamera(10,false);
                Require(string.Join(",",cameraCalls)=="neutral,stop,pan","Remote-grid Pan did not suspend both movement and rotation");
                RemoteView.Reset(); source.SetValue(null,camera);
                Require(RemoteView.PanningCamera==null,"Camera re-entry retained Pan mode");
                RemoteView.TogglePanning();
                foreach(int unavailable in new[] {0,1,2,3})
                {
                    panning=unavailable!=0; activeCamera=unavailable!=1; working=unavailable!=2;
                    controlled=unavailable==3 ? (Sandbox.Game.Entities.IMyControllableEntity)Empty<SpaceEngineers.Game.Entities.Blocks.MyTurretControlBlock>():Empty<MyCharacter>();
                    cameraCalls.Clear(); RemoteView.ControlCamera(10,false);
                    Require(RemoteView.PanningCamera==null && cameraCalls.Count==0,"Inactive, disabled, unpowered or custom-turret camera stole controls");
                }
            }
            finally
            {
                harmony.UnpatchAll(harmony.Id); session.SetValue(null,previous); source.SetValue(null,previousSource); pan.SetValue(null,previousPan); config.SetValue(null,previousConfig); controlled=null;
            }
            log("PASS camera panning: default flight, toggle release/routing, on-foot and remote-grid pan, shared zoom, re-entry reset, disabled/inactive/power-loss gates and custom turret exclusion");
        }
    }
}
#endif
