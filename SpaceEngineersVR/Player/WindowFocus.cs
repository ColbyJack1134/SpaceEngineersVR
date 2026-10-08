using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using SpaceEngineersVR.Player.Control;
using SpaceEngineersVR.Plugin;
using Valve.VR;

namespace SpaceEngineersVR.Player
{
    internal static class WindowFocus
    {
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll")] private static extern bool ShowWindowAsync(IntPtr window,int command);
        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll",SetLastError=true)] private static extern uint SendInput(uint count,Input[] inputs,int size);
        [StructLayout(LayoutKind.Sequential)] private struct Input { public uint Type; public InputData Data; }
        [StructLayout(LayoutKind.Explicit)] private struct InputData
        {
            [FieldOffset(0)] public KeyboardInput Keyboard;
            // The mouse member preserves the native INPUT union size.
            [FieldOffset(0)] public MouseInput Mouse;
        }
        [StructLayout(LayoutKind.Sequential)] private struct KeyboardInput
        { public ushort Key,Scan; public uint Flags,Time; public UIntPtr Extra; }
        [StructLayout(LayoutKind.Sequential)] private struct MouseInput
        { public int X,Y; public uint Data,Flags,Time; public UIntPtr Extra; }
        private static readonly FocusTrigger left=new FocusTrigger(),right=new FocusTrigger();

        internal static void Update(Controls controls)
        {
            bool available=Player.Headset.pose.isTracked && OpenVR.System.IsInputAvailable() && !OpenVR.Overlay.IsDashboardVisible();
            bool focused=MenuPointer.GameFocused;
            bool requestLeft=left.Update(available && Player.HandL.pose.isTracked,focused,controls.LeftClick.Active,controls.LeftClick.RawPressed);
            bool requestRight=right.Update(available && Player.HandR.pose.isTracked,focused,controls.Primary.Active,controls.Primary.RawPressed);
            // Hide the focus squeeze from raw physical contacts as well as gated actions.
            if(left.Suppress)
            {
                Suppress(controls.LeftClick); Suppress(controls.LeftTriggerPressure);
                Suppress(controls.ThrustUp); Suppress(controls.WheelNextPage);
            }
            if(right.Suppress) { Suppress(controls.Primary); Suppress(controls.PointerPressure); }
            if(!requestLeft && !requestRight) return;
            using(var process=Process.GetCurrentProcess())
            {
                IntPtr window=process.MainWindowHandle;
                if(window==IntPtr.Zero) return;
                bool accepted=Request(window,out bool keyboardFallback);
                Logger.Info("VR trigger focus request: "+(accepted ? "accepted":"denied")+"; keyboard fallback="+keyboardFallback);
            }
        }

        internal static bool Request(IntPtr window,out bool keyboardFallback)
        {
            keyboardFallback=false;
            if(window==IntPtr.Zero) return false;
            if(IsIconic(window)) ShowWindowAsync(window,9);
            if(SetForegroundWindow(window)) return true;
            foreach(int key in new[] {0x10,0x11,0x12,0x5B,0x5C})
                if(GetAsyncKeyState(key)<0) return false;
            // SteamVR triggers are not desktop input. An Alt pulse unlocks foreground activation.
            var inputs=new[] {
                new Input {Type=1,Data=new InputData {Keyboard=new KeyboardInput {Key=0x12}}},
                new Input {Type=1,Data=new InputData {Keyboard=new KeyboardInput {Key=0x12,Flags=2}}}
            };
            uint sent=SendInput(2,inputs,Marshal.SizeOf(typeof(Input)));
            if(sent!=2)
            {
                if(sent!=0) SendInput(1,new[] {inputs[1]},Marshal.SizeOf(typeof(Input)));
                return false;
            }
            keyboardFallback=true;
            return SetForegroundWindow(window);
        }

        internal static void Suppress(Button button) => button.AcceptSample(new InputDigitalActionData_t {bActive=true});
        internal static void Suppress(Analog analog) => analog.AcceptSample(new InputAnalogActionData_t {bActive=true});
    }
}
