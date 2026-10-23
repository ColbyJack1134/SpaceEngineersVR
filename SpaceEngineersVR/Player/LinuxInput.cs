#if LINUX
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using VRage;
using VRage.Input;
using VRageMath;

namespace SpaceEngineersVR.Player
{
    // Wayland cannot warp the cursor or inject events, so the laser feeds the game's input state directly.
    internal static class LinuxInput
    {
        private static IVRageInput platform;
        private static Vector2? pending;
        private static Vector2 cursor;
        private static bool left,right;
        private static int wheel;
        private static readonly HashSet<MyKeys> keys=new HashSet<MyKeys>();
        private static MethodInfo setKey;

        // Laser point plus raw mouse deltas, so MenuPointer still yields to a moved mouse.
        internal static Vector2 Cursor => cursor;
        internal static Vector2 Area => platform?.MouseAreaSize ?? Vector2.Zero;

        internal static void Move(int x,int y)
        {
            pending=cursor=new Vector2(x,y);
        }

        // Windows mouse_event flags, so MenuPointer keeps one code path.
        internal static void Mouse(uint flags,int data)
        {
            if((flags&2)!=0) left=true;
            if((flags&4)!=0) left=false;
            if((flags&8)!=0) right=true;
            if((flags&16)!=0) right=false;
            if((flags&0x800)!=0) wheel+=data;
        }

        // MyKeys are Windows virtual-key codes; Shift and Control follow the left keys as on Windows.
        internal static void Key(byte key,bool down)
        {
            var k=(MyKeys)key;
            Set(k,down);
            if(k==MyKeys.LeftShift) Set(MyKeys.Shift,down);
            if(k==MyKeys.LeftControl) Set(MyKeys.Control,down);
        }

        private static void Set(MyKeys key,bool down) { if(down) keys.Add(key); else keys.Remove(key); }

        [HarmonyPatch]
        private static class UpdateStatesPatch
        {
            private static MethodBase TargetMethod() => AccessTools.Method(AccessTools.TypeByName("VRage.Input.MyVRageInput"),"UpdateStates",new System.Type[0]);
            private static void Postfix(IVRageInput ___m_platformInput,MyMouseState ___m_actualMouseStateRaw,ref MyMouseState ___m_actualMouseState,
                ref Vector2 ___m_absoluteMousePosition,object ___m_keyboardState)
            {
                platform=___m_platformInput;
                cursor+=new Vector2(___m_actualMouseStateRaw.X,___m_actualMouseStateRaw.Y);
                if(pending.HasValue) { ___m_absoluteMousePosition=pending.Value; pending=null; }
                if(left) ___m_actualMouseState.LeftButton=true;
                if(right) ___m_actualMouseState.RightButton=true;
                if(wheel!=0) { ___m_actualMouseState.ScrollWheelValue+=wheel; wheel=0; }
                if(keys.Count==0 || ___m_keyboardState==null) return;
                if(setKey==null) setKey=AccessTools.Method(___m_keyboardState.GetType(),"SetKey",new[] {typeof(MyKeys),typeof(bool)});
                foreach(var key in keys) setKey.Invoke(___m_keyboardState,new object[] {key,true});
            }
        }

        // The game skips UpdateStates without focus, and a headset player cannot focus the window.
        [HarmonyPatch]
        private static class FocusPatch
        {
            private static MethodBase TargetMethod() => AccessTools.Method(AccessTools.TypeByName("VRage.Input.MyVRageInput"),"Update",new[] {typeof(bool)});
            private static void Prefix(ref bool gameFocused) { if(Plugin.Main.VrPresent) gameFocused=true; }
        }
    }
}
#endif
