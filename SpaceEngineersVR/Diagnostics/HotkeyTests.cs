#if SEVR_DIAGNOSTICS
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using HarmonyLib;
using Sandbox.Game;
using Sandbox.Game.Gui;
using Sandbox.Game.SessionComponents.Clipboard;
using Sandbox.Game.World;
using Sandbox.Graphics.GUI;
using SpaceEngineersVR.Patches;
using SpaceEngineersVR.Player;
using SpaceEngineersVR.Player.Components;
using SpaceEngineersVR.Plugin;
using Valve.VR;
using VRage.Input;
using VRage.Utils;

namespace SpaceEngineersVR.Diagnostics
{
    public static class HotkeyTests
    {
        private sealed class ControlFixture : MyGuiControlBase { }
        private sealed class ScreenFixture : MyGuiScreenBase
        {
            public override string GetFriendlyName() => "Hotkey fixture";
        }
        private static T Bare<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
        private static void Require(bool value,string message) { if(!value) throw new Exception(message); }
        private static void Set(Type type,string field,object value) => AccessTools.Field(type,field).SetValue(null,value);
        private static MyGuiScreenBase nativeScreen;
        private static Controls controls;
        private static int releaseBlocks;
        private static readonly CVROverlay overlay=Bare<CVROverlay>();
        private static bool Overlay(ref CVROverlay __result) { __result=overlay; return false; }
        private static bool OverlaySuccess(ref EVROverlayError __result) { __result=EVROverlayError.None; return false; }
        private static bool True(ref bool __result) { __result=true; return false; }
        private static bool Screen(ref MyGuiScreenBase __result) { __result=nativeScreen; return false; }
        private static bool NoDialog(ref bool __result) { __result=nativeScreen!=null; return false; }
        private static bool ControlsFixture(ref Controls __result) { __result=controls; return false; }
        private static bool Block() { releaseBlocks++; return false; }
        private static bool Route()
        {
            Set(typeof(InputRouter),"<Mode>k__BackingField",Main.MenuOpen ? InputMode.Menu:InputMode.Walking);
            return false;
        }
        private static bool SkipPaste(ref bool __result) { __result=false; return false; }
        private static void Keyboard(bool hotkeys=true,MyGuiScreenBase screen=null)
        {
            Set(typeof(MenuKeyboard),"target",Bare<ControlFixture>());
            Set(typeof(MenuKeyboard),"valid",(Func<bool>)(()=>true));
            Set(typeof(MenuKeyboard),"screen",screen);
            Set(typeof(MenuKeyboard),"<Hotkeys>k__BackingField",hotkeys);
            Main.MenuOpen=true; Route(); releaseBlocks=0;
        }
        public static void Run(Action<string> log)
        {
            var session=MySession.Static;
            var input=MyInput.Static;
            bool menu=Main.MenuOpen;
            var mode=InputRouter.Mode;
            var harmony=new Harmony("SEVR.HotkeyFixture");
            var keyboardType=AccessTools.TypeByName("VRage.Input.Keyboard.MyGuiLocalizedKeyboardState");
            var locals=AccessTools.Field(keyboardType,"m_localKeys"); var previousLocals=locals.GetValue(null);
            try
            {
                MySession.Static=Bare<MySession>();
                var inputType=AccessTools.TypeByName("VRage.Input.MyVRageInput");
                var native=FormatterServices.GetUninitializedObject(inputType);
                AccessTools.Field(inputType,"m_keyboardState").SetValue(native,FormatterServices.GetUninitializedObject(keyboardType));
                var priorities=AccessTools.Field(inputType,"m_keysPriorityMap");
                priorities.SetValue(native,Activator.CreateInstance(priorities.FieldType));
                locals.SetValue(null,new HashSet<byte>());
                MyInput.Static=(IMyInput)native;
                harmony.CreateClassProcessor(typeof(HotkeyInputPatch)).Patch();
                foreach(var command in new[] {("c",MyKeys.C,MyControlsSpace.COPY_OBJECT), ("x",MyKeys.X,MyControlsSpace.CUT_OBJECT),
                    ("v",MyKeys.V,MyControlsSpace.PASTE_OBJECT),("b",MyKeys.B,MyControlsSpace.CREATE_BLUEPRINT),
                    ("Del",MyKeys.Delete,MyControlsSpace.DELETE_OBJECT),(",",MyKeys.OemComma,MyControlsSpace.COPY_OBJECT)})
                {
                    var binding=new MyControl(command.Item3,command.Item3,MyGuiControlTypeEnum.General,null,command.Item2,keyModifiers:VRage.ModAPI.MyKeyboardModifiers.Control);
                    HotkeyInput.Reset(); HotkeyInput.Press("CTRL"); HotkeyInput.Press(command.Item1);
                    Require(!binding.IsNewPressed(),"Hotkey fired on its keyboard-click frame");
                    Set(typeof(HotkeyInput),"pressed",-1);
                    Require(binding.IsNewPressed() && binding.IsPressed(),"Native control lost a virtual chord: "+command.Item3);
                    Set(typeof(HotkeyInput),"pressed",-2);
                    Require(!binding.IsNewPressed() && binding.IsPressed(),"Virtual hold repeated or released early");
                    Set(typeof(HotkeyInput),"pressed",-7);
                    Require(binding.IsNewReleased() && !binding.IsPressed(),"Native control lost virtual release");
                    Set(typeof(HotkeyInput),"pressed",-8);
                    Require(!binding.IsNewReleased() && !MyInput.Static.IsAnyCtrlKeyPressed(),"Virtual modifier stayed held");
                }
                log("PASS virtual keyboard chords through native MyControl: clipboard commands, remapped key, one press, hold and release.");

                controls=Bare<Controls>();
                void Patch(MethodBase method,string prefix) => harmony.Patch(method,prefix:new HarmonyMethod(typeof(HotkeyTests),prefix));
                Patch(AccessTools.PropertyGetter(typeof(OpenVR),nameof(OpenVR.Overlay)),nameof(Overlay));
                foreach(string method in new[] {"CreateOverlay","SetOverlayWidthInMeters","SetOverlayTexelAspect"})
                    Patch(AccessTools.Method(typeof(CVROverlay),method),nameof(OverlaySuccess));
                Patch(AccessTools.PropertyGetter(typeof(Main),nameof(Main.VrActive)),nameof(True));
                Patch(AccessTools.PropertyGetter(typeof(VRGUIManager),"TopScreen"),nameof(Screen));
                Patch(AccessTools.Method(typeof(VRGUIManager),nameof(VRGUIManager.IsAnyDialogOpen)),nameof(NoDialog));
                Patch(AccessTools.PropertyGetter(typeof(Controls),nameof(Controls.Static)),nameof(ControlsFixture));
                Patch(AccessTools.Method(typeof(Controls),nameof(Controls.BlockUntilRelease)),nameof(Block));
                Patch(AccessTools.Method(typeof(InputRouter),nameof(InputRouter.Update)),nameof(Route));
                harmony.CreateClassProcessor(typeof(GridSelectionPatch)).Patch();
                harmony.CreateClassProcessor(typeof(ClipboardHotkeyPatch)).Patch();
                harmony.Patch(AccessTools.Method(typeof(MyClipboardComponent),nameof(MyClipboardComponent.Paste)),
                    prefix:new HarmonyMethod(typeof(HotkeyTests),nameof(SkipPaste)) {priority=Priority.Last});
                var clipboard=Bare<MyClipboardComponent>();
                foreach(string command in new[] {"Copy","Cut","Delete","CreateBlueprint"})
                {
                    Keyboard();
                    Require(MenuKeyboard.WorldHotkeys && !InputRouter.Gameplay,"Hotkeys enabled controller gameplay before a command");
                    AccessTools.Method(typeof(MyClipboardComponent),command,new[] {typeof(bool),typeof(bool)}).Invoke(clipboard,new object[] {false,false});
                    Require(!MenuKeyboard.IsOpen && !Main.MenuOpen && InputRouter.Gameplay && releaseBlocks>0,"Native "+command+" failed to release keyboard ownership");
                    Require(!GridSelection.Active,"No-character fixture armed selection");
                }
                Keyboard(); clipboard.Paste();
                Require(!MenuKeyboard.IsOpen && InputRouter.Gameplay && releaseBlocks>0,"Paste failed to release keyboard ownership");
                Keyboard(); GridSelection.Arm("Spectator lock");
                Require(!MenuKeyboard.IsOpen && InputRouter.Gameplay,"Spectator selection left keyboard capture active");
                Keyboard();
                var arguments=new object[] {MyStringId.NullOrEmpty,MyControlsSpace.DAMPING_RELATIVE,MyControlStateType.NEW_PRESSED,true};
                AccessTools.Method(typeof(NativeActionPatch),"Postfix").Invoke(null,arguments);
                Require(!MenuKeyboard.IsOpen && !(bool)arguments[3],"Relative dampeners fell through to native targeting");
                Keyboard(); HotkeyInput.Press("l");
                Require(MenuKeyboard.IsOpen && !InputRouter.Gameplay,"Ordinary hotkey dismissed keyboard or enabled controller gameplay");
                Keyboard(false); MenuKeyboard.ReleaseForWorldAction();
                Require(MenuKeyboard.IsOpen && !InputRouter.Gameplay,"Text entry was dismissed by a world action");
                nativeScreen=Bare<ScreenFixture>();
                Keyboard(true,nativeScreen); MenuKeyboard.ReleaseForWorldAction();
                Require(MenuKeyboard.IsOpen && !InputRouter.Gameplay,"World action dismissed a native dialog");
                log("PASS native clipboard handoff, spectator and relative-dampener routes, typing capture, release blocking and native-dialog isolation. World/VR services are fixtures.");
            }
            finally
            {
                nativeScreen=null;
                Set(typeof(MenuKeyboard),"target",null); Set(typeof(MenuKeyboard),"screen",null); Set(typeof(MenuKeyboard),"valid",null);
                Set(typeof(MenuKeyboard),"<Hotkeys>k__BackingField",false);
                HotkeyInput.Reset(); GridSelection.Clear();
                harmony.UnpatchAll(harmony.Id);
                MyInput.Static=input; MySession.Static=session; Main.MenuOpen=menu;
                Set(typeof(InputRouter),"<Mode>k__BackingField",mode);
                locals.SetValue(null,previousLocals); controls=null;
            }
        }
    }
}
#endif
