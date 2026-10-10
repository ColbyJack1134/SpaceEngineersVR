using System;
using HarmonyLib;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Cube;

namespace SpaceEngineersVR.Player
{
    internal static class SymmetrySetupControls
    {
        private static readonly Func<MyCubeBuilder,MySymmetrySettingModeEnum> getMode=AccessTools.MethodDelegate<Func<MyCubeBuilder,MySymmetrySettingModeEnum>>(AccessTools.PropertyGetter(typeof(MyCubeBuilder),"SymmetrySettingMode"));
        private static readonly Action<MyCubeBuilder,MySymmetrySettingModeEnum> setMode=AccessTools.MethodDelegate<Action<MyCubeBuilder,MySymmetrySettingModeEnum>>(AccessTools.PropertySetter(typeof(MyCubeBuilder),"SymmetrySettingMode"));
        internal static bool Active => Sandbox.Game.World.MySession.Static!=null && MyCubeBuilder.Static?.IsSymmetrySetupMode()==true;
        internal static MySymmetrySettingModeEnum Mode => Sandbox.Game.World.MySession.Static==null || MyCubeBuilder.Static==null ? MySymmetrySettingModeEnum.NoPlane:getMode(MyCubeBuilder.Static);
        internal static bool Offset(MySymmetrySettingModeEnum mode) => mode==MySymmetrySettingModeEnum.XPlaneOdd || mode==MySymmetrySettingModeEnum.YPlaneOdd || mode==MySymmetrySettingModeEnum.ZPlaneOdd;
        internal static MySymmetrySettingModeEnum Axis(MySymmetrySettingModeEnum mode) => Offset(mode) ? (MySymmetrySettingModeEnum)((int)mode/2):mode;
        internal static MySymmetrySettingModeEnum WithOffset(MySymmetrySettingModeEnum axis,bool offset) => (MySymmetrySettingModeEnum)((int)axis*(offset ? 2:1));
        private static void Select(MySymmetrySettingModeEnum mode)
        {
            if(!Active) return;
            NativeActions.Reset(); Controls.Static.Primary.BlockUntilRelease(); Controls.Static.Secondary.BlockUntilRelease();
            setMode(MyCubeBuilder.Static,mode);
        }
        private static ActionChoice Plane(string name,MySymmetrySettingModeEnum axis) => new ActionChoice(name,()=>Select(WithOffset(axis,Offset(Mode))),enabled:()=>Active,icon:GameActions.SymmetrySetupAction.Icon);
        internal static ActionChoice[] Actions() => new[] {
            GameActions.PauseAction,
            Plane("X plane",MySymmetrySettingModeEnum.XPlane),Plane("Y plane",MySymmetrySettingModeEnum.YPlane),Plane("Z plane",MySymmetrySettingModeEnum.ZPlane),
            new ActionChoice(()=>Offset(Mode) ? "Offset: half block":"Offset: block center",()=>Select(WithOffset(Axis(Mode),!Offset(Mode))),enabled:()=>Active,historyKey:"Symmetry offset"),
            new ActionChoice("Remove plane",()=>NativeActions.Pulse(MyControlsSpace.SYMMETRY_SETUP_REMOVE),enabled:()=>Active,icon:GameActions.ExitSymmetryAction.Icon),
            GameActions.ExitSymmetryAction,GameActions.SymmetryAction,GameActions.AllActions
        };
    }
}
