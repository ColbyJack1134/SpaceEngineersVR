using System;
using System.Linq;
using HarmonyLib;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.SessionComponents.Clipboard;
using SpaceEngineersVR.Player;
using SpaceEngineersVR.Player.Control;
using VRage.Input;
using VRage.Utils;
using VRageMath;

namespace SpaceEngineersVR.Diagnostics
{
    internal static class PlacementTests
    {
        private static bool SkipSphere() => false;
        private static void Require(bool value,string message) { if (!value) throw new Exception(message); }
        public static void Run(Action<string> log)
        {
            var updates=PatchProcessor.GetOriginalInstructions(AccessTools.Method(typeof(Plugin.Main),"CustomUpdate"))
                .Where(i=>i.operand is System.Reflection.MethodInfo m && m.Name=="Update")
                .Select(i=>((System.Reflection.MethodInfo)i.operand).DeclaringType).ToList();
            Require(updates.IndexOf(typeof(CockpitControls))>=0 && updates.IndexOf(typeof(CockpitControls))<updates.IndexOf(typeof(PlacementControls)) &&
                updates.IndexOf(typeof(PlacementControls))<updates.IndexOf(typeof(NativeActions)),"Physical grip capture must precede build adjustment and native input in the same frame");
            foreach(InputMode mode in Enum.GetValues(typeof(InputMode)))
            foreach(bool grip in new[] {false,true}) foreach(bool a in new[] {false,true}) foreach(bool blocked in new[] {false,true})
                Require(PlacementControls.PaintChord(mode,grip,a,blocked)==(mode==InputMode.Building && grip && a && !blocked),"Paint chord escaped building ownership");
            var gripGate=new InputGate(); var paintGate=new InputGate(); var actions=new ActionFrame();
            gripGate.Update(true,false); paintGate.Update(true,false);
            for(int cycle=0;cycle<4;cycle++)
            {
                gripGate.Update(true,true);
                for(int tick=0;tick<8;tick++)
                {
                    paintGate.Update(true,tick<6);
                    bool painting=PlacementControls.PaintChord(InputMode.Building,gripGate.Held,paintGate.Held,false);
                    if(painting) actions.Queue(MyControlsSpace.CUBE_COLOR_CHANGE);
                    actions.Advance(true);
                    Require(actions.Read(MyControlsSpace.CUBE_COLOR_CHANGE,MyControlStateType.PRESSED)==(tick<6),"Repeated/held A painting requires a grip release");
                    Require(!(paintGate.Pressed && !painting),"Painting falls through to use/interact");
                }
            }
            gripGate.Block(); paintGate.Block(); actions.Reset();
            gripGate.Update(true,true); paintGate.Update(true,true);
            Require(!PlacementControls.PaintChord(InputMode.Building,gripGate.Held,paintGate.Held,false),"Held painting crosses a menu/owner/view reset");
            gripGate.Update(true,false); paintGate.Update(true,false);
            gripGate.Update(true,true); paintGate.Update(true,true);
            Require(PlacementControls.PaintChord(InputMode.Building,gripGate.Held,paintGate.Held,false),"Painting did not rearm after transition release");
            var rotateTap=new GripTap(); var tapped=DateTime.MinValue.AddSeconds(1);
            rotateTap.Update(true,false,false,tapped);
            bool rotating=PlacementControls.Rotating(false,true,rotateTap.Update(false,true,false,tapped.AddMilliseconds(150)),false);
            Require(rotating,"Grip tap did not start rotate mode");
            Require(!PlacementControls.Rotating(rotating,true,false,true),"Placing a block did not end rotate mode");
            Require(!PlacementControls.Rotating(rotating,false,false,false),"Rotate mode survived leaving the build tool or opening a menu");
            rotateTap.Update(true,false,false,tapped.AddSeconds(1)); rotateTap.Update(true,false,true,tapped.AddSeconds(1.1));
            Require(!PlacementControls.Rotating(false,true,rotateTap.Update(false,true,false,tapped.AddSeconds(1.2)),false),"Grip chord started rotate mode");
            rotateTap.Update(true,false,false,tapped.AddSeconds(2));
            Require(!PlacementControls.Rotating(rotating,true,rotateTap.Update(false,true,false,tapped.AddSeconds(2.1)),false),"Second grip tap did not end rotate mode");
            foreach(double scale in new[] {.1,1,3,100,100000})
            foreach(double origin in new[] {0d,1e9})
            {
                var pose=VrMath.Rigid(MatrixD.CreateFromYawPitchRoll(.7,.3,-.2)); pose.Translation=new Vector3D(origin,origin+10,origin-30);
                double distance=5,search=PlacementControls.ObserverDistance(distance,scale);
                var target=pose.Translation+pose.Forward*search;
                Require(Math.Abs(pose.Forward.Length()-1)<1e-8,"Observer orientation is not rigid");
                Require(search>=distance && search<=20000,"Observer ray has unbounded reach");
                var grid=MatrixD.CreateRotationY(.3); grid.Translation=target;
                var box=new BoundingBoxD(-Vector3D.One,Vector3D.One);
                Require(PlacementControls.WithinReach(box,MatrixD.Invert(grid),target+grid.Right*5.9,5),"Reach rejects a nearby large-world block");
                Require(!PlacementControls.WithinReach(box,MatrixD.Invert(grid),target+grid.Right*6.1,5),"Observer position or scale extends Survival reach");
            }
            foreach(double scale in new[] {.1,1,3,100})
            foreach(double nativeDistance in new[] {1d,20,100})
                Require(Math.Abs(PlacementControls.CreativeObserverDistance(nativeDistance,scale,20)/scale-nativeDistance/20)<1e-8,
                    "Creative distance changes with observer scale");
            Require(PlacementControls.CreativeObserverDistance(100,100000,20)==20000,"Creative observer ray exceeds its world limit");
            foreach (var mode in new[] { InputMode.Building,InputMode.Clipboard,InputMode.Jetpack,InputMode.Walking,InputMode.Menu,InputMode.Blocked,InputMode.Radial,InputMode.Piloting })
            foreach (bool primary in new[] { false,true }) foreach (bool secondary in new[] { false,true }) foreach (bool alternate in new[] { false,true })
            {
                var frame=new ActionFrame();
                PlacementControls.Queue(frame,mode,primary,secondary,alternate); frame.Advance(true);
                bool Read(MyStringId action) => frame.Read(action,MyControlStateType.NEW_PRESSED);
                Require(Read(MyControlsSpace.SECONDARY_TOOL_ACTION)==(mode==InputMode.Building && primary && secondary),"Building remove ownership depends on flight/alternate trigger");
                Require(Read(MyControlsSpace.PRIMARY_TOOL_ACTION)==(mode==InputMode.Building && primary && !secondary),"Building trigger changed to implicit remove");
                Require(Read(MyControlsSpace.COPY_PASTE_ACTION)==(mode==InputMode.Clipboard && primary && !secondary),"Clipboard paste leaks or conflicts with cancel");
                Require(!Read(MyControlsSpace.COPY_PASTE_CANCEL),"Blueprint adjustment grip cancelled the preview");
            }
            foreach (var mode in new[] { InputMode.Building,InputMode.Clipboard })
            {
                var gate=new InputGate(); var frame=new ActionFrame();
                var action=mode==InputMode.Building ? MyControlsSpace.SECONDARY_TOOL_ACTION : MyControlsSpace.COPY_PASTE_ACTION;
                for(int cycle=0;cycle<6;cycle++)
                {
                    gate.Block(); frame.Reset(); // menu, tracking, owner, recenter, radial, preview change
                    gate.Update(true,true);
                    PlacementControls.Queue(frame,mode,gate.Held,mode==InputMode.Building && gate.Held,false); frame.Advance(true);
                    Require(!frame.Read(action,MyControlStateType.PRESSED),"Held action escaped transition");
                    gate.Update(true,false); gate.Update(true,true);
                    PlacementControls.Queue(frame,mode,gate.Held,mode==InputMode.Building && gate.Held,false); frame.Advance(true);
                    Require(frame.Read(action,MyControlStateType.NEW_PRESSED),"Fresh placement action lost");
                    PlacementControls.Queue(frame,mode,true,mode==InputMode.Building,false); frame.Advance(true);
                    Require(!frame.Read(action,MyControlStateType.NEW_PRESSED),"Held placement repeated destructive action");
                    frame.Advance(true); Require(frame.Read(action,MyControlStateType.NEW_RELEASED),"Native placement release lost");
                }
            }
            foreach(int hz in new[] {36,72,90}) foreach(bool continuous in new[] {false,true})
            {
                var frame=new ActionFrame(); var next=DateTime.MinValue;
                var rotation=MyControlsSpace.CUBE_ROTATE_ROLL_POSITIVE;
                int pressedFrames=0,edges=0;
                for(int i=0;i<hz;i++)
                {
                    if(PlacementControls.AxisDue(ref next,.9f,continuous,DateTime.MinValue.AddSeconds(1+(double)i/hz))) frame.Queue(rotation);
                    frame.Advance(true);
                    if(frame.Read(rotation,MyControlStateType.PRESSED)) pressedFrames++;
                    if(frame.Read(rotation,MyControlStateType.NEW_PRESSED)) edges++;
                }
                Require(continuous ? pressedFrames==hz && edges==1 : pressedFrames==5 && edges==5,"Free/grid rotation cadence lost at "+hz+" Hz");
                Require(!PlacementControls.AxisDue(ref next,0,continuous,DateTime.UtcNow),"Centered stick continues rotation");
                frame.Advance(true); Require(!frame.Read(rotation,MyControlStateType.PRESSED),"Rotation persists after release");
                Require(PlacementControls.AxisDue(ref next,-1,continuous,DateTime.UtcNow),"Rotation fails on reverse after neutral");
                frame.Reset(); Require(!frame.Read(rotation,MyControlStateType.PRESSED),"Rotation persists through cancellation");
            }
            foreach(int hz in new[] {36,72,90})
            {
                double distance=5;
                for(int i=0;i<hz;i++) distance*=PlacementControls.DistanceStep(1,1f/hz);
                Require(Math.Abs(distance-5*Math.Pow(1.1,1/.22))<.0001,"Distance speed depends on frame rate");
                Require(PlacementControls.DistanceStep(0,1f/hz)==1 && PlacementControls.DistanceStep(float.NaN,1f/hz)==1,"Distance moves at neutral/invalid input");
                for(int i=0;i<hz;i++) distance/=PlacementControls.DistanceStep(-1,1f/hz);
                Require(Math.Abs(distance-5)<.0001,"Distance reversal is asymmetric");
            }
            // Verify the installed engine still consumes these exact action IDs and
            // retains the vanilla removal/permission path; no destructive world calls.
            CheckFields(typeof(MyCubeBuilder),"HandleGameInput",MyControlsSpace.CUBE_COLOR_CHANGE);
            CheckFields(typeof(MyCubeBuilder),"HandleCurrentGridInput",MyControlsSpace.SECONDARY_TOOL_ACTION,MyControlsSpace.PRIMARY_TOOL_ACTION);
            CheckFields(typeof(MyClipboardComponent),"HandleLeftMouseButton",MyControlsSpace.COPY_PASTE_ACTION);
            CheckFields(typeof(MyClipboardComponent),"HandleEscape",MyControlsSpace.COPY_PASTE_CANCEL);
            CheckFields(typeof(MyClipboardComponent),"HandleMouseScrollInput",MyControlsSpace.MOVE_CLOSER,MyControlsSpace.MOVE_FURTHER);
            var native=PatchProcessor.GetOriginalInstructions(AccessTools.Method(typeof(MyCubeBuilder),"HandleAdminAndCreativeInput")).ToList();
            Require(native.Any(i=>i.operand is System.Reflection.MethodInfo m && m.Name=="get_CreativeMode") &&
                native.Any(i=>i.operand is System.Reflection.MethodInfo m && m.Name=="CreativeToolsEnabled"),"Installed Creative permission path changed");
            foreach(var type in new[] {typeof(MyGridClipboard),AccessTools.TypeByName("Sandbox.Game.Entities.MyVoxelClipboard"),AccessTools.TypeByName("Sandbox.Game.Entities.MyFloatingObjectClipboard")})
            {
                Require(AccessTools.Method(type,"GetPasteMatrix")?.ReturnType==typeof(MatrixD),"Native clipboard pose signature changed");
                foreach(string movement in new[] {"MoveEntityFurther","MoveEntityCloser"})
                    Require(PatchProcessor.GetOriginalInstructions(AccessTools.Method(type,movement)).Count(i=>i.opcode==System.Reflection.Emit.OpCodes.Ldc_R4 && i.operand is float factor && factor==1.1f)==1,"Native clipboard distance arithmetic changed");
            }
            Require(PlacementControls.ToolMode(InputMode.Spectator,true,false)==InputMode.Clipboard &&
                PlacementControls.ToolMode(InputMode.Spectator,false,false)==InputMode.Spectator &&
                PlacementControls.ToolMode(InputMode.Radial,true,false)==InputMode.Radial,"Clipboard overlay took camera or UI ownership");
            var planetHarmony=new Harmony("SEVR.NativePlanetFixture");
            try
            {
                planetHarmony.CreateClassProcessor(typeof(Patches.PlanetPreviewSpherePatch)).Patch();
                var sphere=AccessTools.Method(typeof(VRageRender.MyRenderProxy),nameof(VRageRender.MyRenderProxy.DebugDrawSphere),new[] {typeof(Vector3D),typeof(float),typeof(Color),typeof(float),typeof(bool),typeof(bool),typeof(bool),typeof(bool)});
                planetHarmony.Patch(sphere,prefix:new HarmonyMethod(typeof(PlacementTests),nameof(SkipSphere)) {priority=Priority.Last});
                foreach(bool valid in new[] {true,false})
                {
                    var center=new Vector3D(1e9,-1e9,1e9);
                    var spheres=PlanetRenderTests.Capture(center,65000,valid);
                    Require(spheres.Length==1 && spheres[0].Position==center && Math.Abs(spheres[0].Radius-71500)<.1 &&
                        spheres[0].Color==(valid ? Color.Green:Color.Red) && spheres[0].DepthRead && !spheres[0].Smooth,"Planet preview differs from the installed game's placement sphere");
                    var unchanged=PlanetPreview.Current;
                    sphere.Invoke(null,new object[] {Vector3D.Zero,1f,Color.White,1f,true,false,true,false});
                    PlanetPreview.Commit(); Require(PlanetPreview.Current.Length==unchanged.Length,"Unrelated developer spheres entered planet capture");
                }
                PlanetPreview.Begin(); Require(PlanetPreview.Current.Length==0,"Planet preview retained a prior frame");
            }
            finally { PlanetPreview.Capturing=false; PlanetPreview.Begin(); planetHarmony.UnpatchAll(planetHarmony.Id); }
            Require(AccessTools.Method(AccessTools.TypeByName("VRageRender.MyRender11"),"ProcessDebugMessages",new[] {typeof(System.Collections.Generic.List<VRageRender.Messages.MyRenderMessageBase>)} )?.ReturnType==typeof(bool),"Native gameplay gizmo processor signature changed");
            log("PASS native planet preview capture: exact position/radius/validity art at billion-metre origins, unrelated debug exclusion and frame clearing; grid/voxel/item clipboards share installed pose and distance APIs");
            Require(PatchProcessor.GetOriginalInstructions(AccessTools.Method(typeof(NativeActions),nameof(NativeActions.Reset)))
                .Any(i=>i.operand is System.Reflection.MethodInfo m && m.DeclaringType==typeof(MyCubeBuilder) && m.Name=="InputLost"),"VR transition omitted native stroke cancellation");
            foreach(var type in new[] {typeof(Sandbox.Game.Entities.MyCockpit),typeof(Sandbox.Game.Entities.Character.MyCharacter)})
            {
                Require(AccessTools.GetDeclaredMethods(type).Count(m=>m.Name.EndsWith(".ControlCamera"))==1,"Native camera ownership API changed");
                Require(AccessTools.PropertyGetter(type,"ForceFirstPersonCamera")?.ReturnType==typeof(bool),"Native collision camera API changed");
            }
            var nativeReach=PatchProcessor.GetOriginalInstructions(AccessTools.Method(typeof(MyCubeBuilderGizmo),"DefaultGizmoCloseEnough"));
            Require(nativeReach.Any(i=>i.operand is System.Reflection.MethodInfo m && m.Name=="GetHeadMatrix"),"Installed reach no longer uses character head");
            Require(AccessTools.Field(typeof(MyCubeBuilder),"m_gizmo")?.FieldType==typeof(MyCubeBuilderGizmo) && AccessTools.Method(typeof(MyCubeBuilder),"Change")!=null,"Native paint/gizmo contract changed");
            log("PASS observer building: consistent observer distances at five scales and billion-metre origins; independent character reach; paint chord ownership; installed character/cockpit camera, paint and reach APIs.");
            log("PASS placement: continuous free-space and five grid steps/second at 36/72/90 Hz with neutral/reverse/cancel; build/clipboard/tool ownership, simultaneous cancel priority, alternate-trigger isolation, transition release gates; installed native remove/paste/cancel/distance IDs, Creative checks and clipboard pose API. No world modified.");
        }
        internal static void RunNativeFixture(Action<string> log)
        {
            // CubeBuilder's type initializer needs the game's object-builder registry.
            // Run only inside the initialized diagnostic game, still without a world.
            // Exercise unregistered objects only; no placement request is sent.
            var builder=(MyCubeBuilder)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(MyCubeBuilder));
            var gizmo=new MyCubeBuilderGizmo();
            AccessTools.Field(typeof(MyCubeBuilder),"m_gizmo").SetValue(builder,gizmo);
            foreach(var space in gizmo.Spaces)
            {
                space.m_startBuild=VRageMath.Vector3I.One;
                space.m_startRemove=VRageMath.Vector3I.One;
                space.m_continueBuild=VRageMath.Vector3I.One*2;
            }
            builder.InputLost();
            Require(gizmo.Spaces.All(s=>!s.m_startBuild.HasValue && !s.m_startRemove.HasValue),"Native InputLost retained a deferred build/remove stroke");
            NativeCellPicking(builder,log);
            NativeEmptyCellPicking(log);
            NativeCockpitToolbar(log);
            log("PASS native build cancellation: InputLost clears all eight symmetry spaces' pending build/remove starts in a disposable gizmo fixture.");
        }
        private sealed class RayProvider : IMyPlacementProvider
        {
            public Vector3D RayStart { get; set; }
            public Vector3D RayDirection { get; set; }
            public Sandbox.Engine.Physics.MyPhysics.HitInfo? HitInfo => null;
            public MyCubeGrid ClosestGrid => null;
            public MyVoxelBase ClosestVoxelMap => null;
            public bool CanChangePlacementObjectSize => false;
            public float IntersectionDistance { get; set; }
            public void RayCastGridCells(MyCubeGrid grid,System.Collections.Generic.List<Vector3I> cells,Vector3I inflate,float distance) => throw new NotSupportedException();
            public void UpdatePlacement() => throw new NotSupportedException();
        }
        private static void NativeCellPicking(MyCubeBuilder builder,Action<string> log)
        {
            var previous=MyBlockBuilderBase.PlacementProvider;
            var provider=new RayProvider();
            var method=AccessTools.Method(typeof(MyBlockBuilderBase),"GetCubeAddAndRemovePositions");
            var grid=new MyCubeGrid();
            AccessTools.Field(typeof(MyBlockBuilderBase),"m_currentGrid").SetValue(builder,grid);
            int cases=0;
            try
            {
                MyBlockBuilderBase.PlacementProvider=provider;
                foreach(float size in new[] {.5f,2.5f})
                foreach(double origin in new[] {0d,1e9})
                foreach(double scale in new[] {.1,1,3,100,100000})
                foreach(var normal in new[] {Vector3D.Left,Vector3D.Right,Vector3D.Up,Vector3D.Down,Vector3D.Forward,Vector3D.Backward})
                foreach(double slant in new[] {0d,.7})
                {
                    AccessTools.PropertySetter(typeof(MyCubeGrid),nameof(MyCubeGrid.GridSize)).Invoke(grid,new object[] {size});
                    var world=MatrixD.CreateFromYawPitchRoll(.4,.2,-.3); world.Translation=new Vector3D(origin,origin+5,origin-7);
                    grid.PositionComp.SetWorldMatrix(ref world);
                    var tangent=Math.Abs(normal.Y)>.5 ? Vector3D.Right : Vector3D.Up;
                    var direction=Vector3D.Normalize(-normal+tangent*slant);
                    var hit=normal*(size/2);
                    provider.RayDirection=Vector3D.TransformNormal(direction,world);
                    provider.RayStart=Vector3D.Transform(hit-direction*.05,world);
                    object[] args={Vector3I.Zero,false,Vector3I.Zero,Vector3I.Zero,Vector3I.Zero};
                    Require(!(bool)method.Invoke(builder,args),"Old surface-offset regression no longer reproduced by installed cell picking");
                    provider.RayStart=Vector3D.Transform(hit-direction*PlacementControls.ObserverDistance(5,scale),world);
                    Require((bool)method.Invoke(builder,args),"Native attachment failed with full observer ray");
                    Require((Vector3I)args[2]==Vector3I.Round(normal) && (Vector3I)args[3]==Vector3I.Round(normal) && (Vector3I)args[4]==Vector3I.Zero,
                        "Native attachment selected the wrong face or cell");
                    cases++;
                }
            }
            finally { MyBlockBuilderBase.PlacementProvider=previous; AccessTools.Field(typeof(MyBlockBuilderBase),"m_currentGrid").SetValue(builder,null); }
            log("PASS native grid attachment: "+cases+" flat/oblique face, small/large cell, rotated billion-metre origin and observer-scale cases; old 5cm origin rejected, full ray returns the correct adjacent cell. No world loaded or modified.");
        }
        private static void NativeEmptyCellPicking(Action<string> log)
        {
            var grid=new MyCubeGrid();
            AccessTools.Field(typeof(MyCubeGrid),"m_min").SetValue(grid,Vector3I.Zero);
            AccessTools.Field(typeof(MyCubeGrid),"m_max").SetValue(grid,Vector3I.Zero);
            var cubes=AccessTools.Field(typeof(MyCubeGrid),"m_cubes").GetValue(grid);
            var cubeType=cubes.GetType().GetGenericArguments()[1];
            var cube=System.Runtime.Serialization.FormatterServices.GetUninitializedObject(cubeType);
            var block=(MySlimBlock)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(MySlimBlock));
            AccessTools.Field(cubeType,"CubeBlock").SetValue(cube,block);
            cubes.GetType().GetMethod("TryAdd").Invoke(cubes,new[] {(object)Vector3I.Zero,cube});
            int cases=0;
            foreach(float size in new[] {.5f,2.5f})
            foreach(double origin in new[] {0d,1e9})
            foreach(var face in new[] {Vector3I.Left,Vector3I.Right,Vector3I.Up,Vector3I.Down,Vector3I.Forward,Vector3I.Backward})
            {
                grid.GridSizeEnum=size<1 ? VRage.Game.MyCubeSize.Small:VRage.Game.MyCubeSize.Large;
                // Main-menu definitions have no cube sizes until a world is loaded.
                AccessTools.PropertySetter(typeof(MyCubeGrid),nameof(MyCubeGrid.GridSize)).Invoke(grid,new object[] {size});
                AccessTools.PropertySetter(typeof(MyCubeGrid),nameof(MyCubeGrid.GridSizeHalfVector)).Invoke(grid,new object[] {new Vector3(size/2)});
                var world=MatrixD.CreateFromYawPitchRoll(.4,.2,-.3); world.Translation=new Vector3D(origin,origin+5,origin-7);
                grid.PositionComp.SetWorldMatrix(ref world);
                var tangent=face.Z==0 ? Vector3D.Backward:Vector3D.Right;
                var start=Vector3D.Transform(((Vector3D)face+tangent*4)*size,world);
                var end=Vector3D.Transform(((Vector3D)face-tangent*4)*size,world);
                Require(CreativePlacement.TryCell(grid,start,end,out var cell,out var direction,out double distance),"Empty adjacent destination missed: size="+size+", origin="+origin+", face="+face);
                Require(cell==face && direction==face && Math.Abs(distance-3.5*size)<.00001,"Empty destination selected wrong cell, face or distance");
                start=Vector3D.Transform(((Vector3D)face*2+tangent*4)*size,world);
                end=Vector3D.Transform(((Vector3D)face*2-tangent*4)*size,world);
                Require(!CreativePlacement.TryCell(grid,start,end,out _,out _,out _),"Disconnected destination snapped to grid");
                start=Vector3D.Transform(Vector3D.Zero,world);
                Require(!CreativePlacement.TryCell(grid,start,end,out _,out _,out _),"Occupied origin snapped through a block");
                cases++;
            }
            var gizmo=new MyCubeBuilderGizmo();
            var definition=new Sandbox.Definitions.MyCubeBlockDefinition {Size=Vector3I.One,CubeSize=VRage.Game.MyCubeSize.Large};
            var mirror=AccessTools.Method(typeof(MyCubeBuilderGizmo),"MirrorGizmoSpace");
            foreach(string axis in new[] {"XPlane","YPlane","ZPlane"})
            {
                var source=gizmo.SpaceDefault; var target=gizmo.Spaces[1];
                source.m_blockDefinition=definition;
                source.m_addPos=Vector3I.One; source.m_addDir=Vector3I.Right; source.m_removePos=Vector3I.Zero; source.m_removeBlock=null;
                mirror.Invoke(gizmo,new object[] {target,source,Enum.Parse(mirror.GetParameters()[2].ParameterType,axis),Vector3I.Zero,false,definition,grid});
                Require(target.m_removeBlock==block,"Native symmetry no longer reproduces an empty-ray removal target");
                var add=target.m_addPos;
                Patches.CreativeSymmetryPatch.ClearRemoval(gizmo);
                Require(gizmo.Spaces.All(s=>s.m_removeBlock==null && s.m_removeBlocksInMultiBlock.Count==0) && target.m_addPos==add,
                    "Empty-cell symmetry retains removal targets or loses placement");
            }
            log("PASS Creative empty-cell attachment: "+cases+" native grid traversals across all six faces, both cell sizes and rotated billion-metre origins; disconnected cells and occupied origins rejected. No world loaded.");
            log("PASS native empty-cell symmetry: all three mirror axes clear removal targets while retaining mirrored placement.");
        }
        private static void NativeCockpitToolbar(Action<string> log)
        {
            var cockpit=(MyCockpit)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(MyCockpit));
            var ship=new Sandbox.Game.Screens.Helpers.MyToolbar(VRage.Game.MyToolbarType.Ship);
            var build=new Sandbox.Game.Screens.Helpers.MyToolbar(VRage.Game.MyToolbarType.BuildCockpit);
            AccessTools.Field(typeof(MyShipController),"m_toolbar").SetValue(cockpit,ship);
            AccessTools.Field(typeof(MyShipController),"m_buildToolbar").SetValue(cockpit,build);
            for(int i=0;i<4;i++)
            {
                cockpit.BuildingMode=true; Require(ReferenceEquals(cockpit.Toolbar,build),"Native cockpit lost its separate build toolbar");
                cockpit.BuildingMode=false; Require(ReferenceEquals(cockpit.Toolbar,ship),"Native cockpit did not restore its ship toolbar");
            }
            Require(!CockpitBuilding.Shortcut(false,cockpit) && CockpitBuilding.Shortcut(true,cockpit),"Native Ctrl+G state was overridden without a request");
            log("PASS native cockpit toolbar: repeated build/ship toolbar switching retains both native instances; no world or toolbar action executed.");
        }
        private static void CheckFields(Type type,string method,params MyStringId[] actions)
        {
            var fields=PatchProcessor.GetOriginalInstructions(AccessTools.Method(type,method)).Where(i=>i.operand is System.Reflection.FieldInfo)
                .Select(i=>(System.Reflection.FieldInfo)i.operand).Where(f=>f.DeclaringType==typeof(MyControlsSpace)).Select(f=>(MyStringId)f.GetValue(null)).ToArray();
            foreach(var action in actions) Require(fields.Contains(action),"Installed "+method+" no longer reads "+action);
        }
    }
}
