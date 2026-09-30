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

namespace SpaceEngineersVR.Diagnostics
{
    internal static class PlacementTests
    {
        private static void Require(bool value,string message) { if (!value) throw new Exception(message); }
        public static void Run(Action<string> log)
        {
            foreach (var mode in new[] { InputMode.Building,InputMode.Clipboard,InputMode.Jetpack,InputMode.Walking,InputMode.Menu,InputMode.Blocked,InputMode.Radial,InputMode.Piloting })
            foreach (bool primary in new[] { false,true }) foreach (bool secondary in new[] { false,true }) foreach (bool alternate in new[] { false,true })
            {
                var frame=new ActionFrame();
                PlacementControls.Queue(frame,mode,primary,secondary,alternate); frame.Advance(true);
                bool Read(MyStringId action) => frame.Read(action,MyControlStateType.NEW_PRESSED);
                Require(Read(MyControlsSpace.SECONDARY_TOOL_ACTION)==(mode==InputMode.Building && secondary),"Building remove ownership depends on flight/alternate trigger");
                Require(Read(MyControlsSpace.PRIMARY_TOOL_ACTION)==(mode==InputMode.Building && primary && !secondary),"Building trigger changed to implicit remove");
                Require(Read(MyControlsSpace.COPY_PASTE_ACTION)==(mode==InputMode.Clipboard && primary && !secondary),"Clipboard paste leaks or conflicts with cancel");
                Require(Read(MyControlsSpace.COPY_PASTE_CANCEL)==(mode==InputMode.Clipboard && secondary),"Clipboard cancel unavailable");
            }
            foreach (var mode in new[] { InputMode.Building,InputMode.Clipboard })
            {
                var gate=new InputGate(); var frame=new ActionFrame();
                var action=mode==InputMode.Building ? MyControlsSpace.SECONDARY_TOOL_ACTION : MyControlsSpace.COPY_PASTE_ACTION;
                for(int cycle=0;cycle<6;cycle++)
                {
                    gate.Block(); frame.Reset(); // menu, tracking, owner, recenter, radial, preview change
                    gate.Update(true,true);
                    PlacementControls.Queue(frame,mode,mode==InputMode.Clipboard && gate.Held,mode==InputMode.Building && gate.Held,false); frame.Advance(true);
                    Require(!frame.Read(action,MyControlStateType.PRESSED),"Held action escaped transition");
                    gate.Update(true,false); gate.Update(true,true);
                    PlacementControls.Queue(frame,mode,mode==InputMode.Clipboard && gate.Held,mode==InputMode.Building && gate.Held,false); frame.Advance(true);
                    Require(frame.Read(action,MyControlStateType.NEW_PRESSED),"Fresh placement action lost");
                    PlacementControls.Queue(frame,mode,mode==InputMode.Clipboard,mode==InputMode.Building,false); frame.Advance(true);
                    Require(!frame.Read(action,MyControlStateType.NEW_PRESSED),"Held placement repeated destructive action");
                    frame.Advance(true); Require(frame.Read(action,MyControlStateType.NEW_RELEASED),"Native placement release lost");
                }
            }
            // Verify the installed engine still consumes these exact action IDs and
            // retains the vanilla removal/permission path; no destructive world calls.
            CheckFields(typeof(MyCubeBuilder),"HandleCurrentGridInput",MyControlsSpace.SECONDARY_TOOL_ACTION,MyControlsSpace.PRIMARY_TOOL_ACTION);
            CheckFields(typeof(MyClipboardComponent),"HandleLeftMouseButton",MyControlsSpace.COPY_PASTE_ACTION);
            CheckFields(typeof(MyClipboardComponent),"HandleEscape",MyControlsSpace.COPY_PASTE_CANCEL);
            CheckFields(typeof(MyClipboardComponent),"HandleMouseScrollInput",MyControlsSpace.MOVE_CLOSER,MyControlsSpace.MOVE_FURTHER);
            var native=PatchProcessor.GetOriginalInstructions(AccessTools.Method(typeof(MyCubeBuilder),"HandleAdminAndCreativeInput")).ToList();
            Require(native.Any(i=>i.operand is System.Reflection.MethodInfo m && m.Name=="get_CreativeMode") &&
                native.Any(i=>i.operand is System.Reflection.MethodInfo m && m.Name=="CreativeToolsEnabled"),"Installed Creative permission path changed");
            Require(AccessTools.Method(typeof(MyGridClipboard),"GetPasteMatrix")?.ReturnType==typeof(VRageMath.MatrixD),"Native clipboard pose signature changed");
            Require(PatchProcessor.GetOriginalInstructions(AccessTools.Method(typeof(NativeActions),nameof(NativeActions.Reset)))
                .Any(i=>i.operand is System.Reflection.MethodInfo m && m.DeclaringType==typeof(MyCubeBuilder) && m.Name=="InputLost"),"VR transition omitted native stroke cancellation");
            log("PASS placement: build/clipboard/tool ownership, simultaneous cancel priority, alternate-trigger isolation, transition release gates; installed native remove/paste/cancel/distance IDs, Creative checks and clipboard pose API. No world modified.");
        }
        internal static void RunNativeFixture(Action<string> log)
        {
            // CubeBuilder's type initializer needs the game's object-builder registry.
            // Run only inside the initialized diagnostic game, still without a world.
            // Exercise the native cancellation method against its real gizmo. No
            // session/grid exists, so this cannot place or remove world blocks.
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
            log("PASS native build cancellation: InputLost clears all eight symmetry spaces' pending build/remove starts in a disposable gizmo fixture.");
        }
        private static void CheckFields(Type type,string method,params MyStringId[] actions)
        {
            var fields=PatchProcessor.GetOriginalInstructions(AccessTools.Method(type,method)).Where(i=>i.operand is System.Reflection.FieldInfo)
                .Select(i=>(System.Reflection.FieldInfo)i.operand).Where(f=>f.DeclaringType==typeof(MyControlsSpace)).Select(f=>(MyStringId)f.GetValue(null)).ToArray();
            foreach(var action in actions) Require(fields.Contains(action),"Installed "+method+" no longer reads "+action);
        }
    }
}
