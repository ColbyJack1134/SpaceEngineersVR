using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Sandbox.Game.Gui;
using Sandbox.Game.Screens.Helpers;
using SpaceEngineersVR.Plugin;
using VRageMath;

namespace SpaceEngineersVR.Player
{
    internal static class MenuPointer
    {
        [StructLayout(LayoutKind.Sequential)] private struct Point { public int X,Y; }
        [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left,Top,Right,Bottom; }
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window,out uint process);
        [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr window,out Rect rect);
        [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr window,ref Point point);
        [DllImport("user32.dll")] private static extern bool SetCursorPos(int x,int y);
        [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
        [DllImport("user32.dll")] private static extern void mouse_event(uint flags,uint x,uint y,uint data,UIntPtr extra);
        private static readonly uint ProcessId=(uint)Process.GetCurrentProcess().Id;
        [DllImport("user32.dll")] private static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);
        private static bool held, secondaryHeld,shiftHeld,controlHeld;
        private static bool checkTextFocus;
        private static readonly PointerHand pointer=new PointerHand();
        private static readonly Control.InputGate leftGrip=new Control.InputGate();
        private static readonly Control.PartialClick rightClick=new Control.PartialClick(),leftClick=new Control.PartialClick();
        internal static Controller Hand => pointer.Hand;
        private static byte pulseKey;
        private static int pulseTicks;
        private static DateTime scrollAfter;
        public static void Key(byte key)
        {
            if (!GameFocused || pulseKey != 0) return;
            keybd_event(key, 0, 0, UIntPtr.Zero);
            pulseKey = key;
            pulseTicks = 2;
        }
        private static void ReleaseKey()
        {
            if (pulseKey != 0) keybd_event(pulseKey, 0, 2, UIntPtr.Zero);
            pulseKey = 0;
        }

        private static Point lastCursor;
        private static bool haveCursor;
        private static DateTime mouseUntil;
        public static bool GameFocused
        {
            get
            {
                GetWindowThreadProcessId(GetForegroundWindow(), out uint process);
                return process == ProcessId;
            }
        }
        public static void Release() { ReleaseMouse(); ReleaseKey(); Modifiers(false,false); }
        private static bool Aimed(Controller hand)
        {
            if(!hand.pose.isTracked) return false;
            var tip=MenuHands.PointerTracking(false,hand);
            return MenuKeyboard.IsOpen ? FloatingKeyboard.Hits(tip) : Components.VRGUIManager.TryPanelHit(tip,out _);
        }
        private static void Modifiers(bool shift,bool control)
        {
            if(shift!=shiftHeld) keybd_event(0xA0,0,shift ? 0u:2u,UIntPtr.Zero);
            if(control!=controlHeld) keybd_event(0xA2,0,control ? 0u:2u,UIntPtr.Zero);
            shiftHeld=shift; controlHeld=control;
        }
        private static void ReleaseMouse()
        {
            if (held) mouse_event(4,0,0,0,UIntPtr.Zero);
            if (secondaryHeld) mouse_event(16,0,0,0,UIntPtr.Zero);
            held = secondaryHeld = false;
        }
        public static void Update()
        {
            if (pulseKey != 0 && --pulseTicks <= 0) ReleaseKey();
            MenuKeyboard.Update();
            VrChat.Update();
            if(checkTextFocus)
            {
                checkTextFocus=false;
                var focused=Components.VRGUIManager.TopScreen?.FocusedControl;
                // A grid/category click may leave the search box focused. Require
                // the pointer to have actually reached the text field as well.
                if((focused is Sandbox.Graphics.GUI.MyGuiControlTextbox box && box.IsMouseOver) ||
                    (focused is MyGuiControlSearchBox search && search.TextBox.IsMouseOver))
                    MenuKeyboard.Open();
            }
            var controls = Controls.Static;
            if (InputRouter.Mode == InputMode.Menu && GameFocused)
            {
                if (controls.Unequip.HasPressed)
                {
                    if (SpatialUi.CollapseIfOpen()) controls.Unequip.BlockUntilRelease();
                    else if (MenuKeyboard.IsOpen) MenuKeyboard.Close();
                    else Key(27);
                }
                if (controls.Jetpack.HasPressed) MenuKeyboard.Open();
                if (controls.MenuKeyboardFallback.HasPressed) MenuKeyboard.Open(true);
                if (controls.Interact.HasPressed && !MenuKeyboard.IsOpen) Key(13);

            }
            if (InputRouter.Mode != InputMode.Menu) { Release(); pointer.Reset(); leftGrip.Block(); return; }
            leftGrip.Update(controls.LeftGripPressure.Active,controls.LeftGripPressure.RawPosition.X>.55f);
            pointer.Update(Aimed(Player.HandR),Aimed(Player.HandL),held || secondaryHeld || FloatingMenu.OwnsInput || FloatingKeyboard.Dragging);
            var hand=pointer.Hand;
            if (!Common.Config.ControllerMenuPointer || !hand.pose.isTracked || MenuKeyboard.IsOpen || FloatingMenu.OwnsInput)
            { ReleaseMouse(); Modifiers(false,false); return; }
            IntPtr window=GetForegroundWindow();
            GetWindowThreadProcessId(window,out uint process);
            if (process!=ProcessId) { Release(); return; }
            if(GetCursorPos(out Point current))
            {
                if(haveCursor && (Math.Abs(current.X-lastCursor.X)>3 || Math.Abs(current.Y-lastCursor.Y)>3) && !held)
                    mouseUntil=DateTime.UtcNow.AddSeconds(2);
                lastCursor=current; haveCursor=true;
            }
            var trigger=controls.Click(hand);
            var partial=hand==Player.HandR ? rightClick:leftClick;
            partial.Update(trigger.Held,trigger.Pressed,trigger.Pressure);
            bool click=partial.Pressed;
            bool clickHeld=partial.Held;
            bool menu=pointer.Left ? leftGrip.Pressed:controls.Secondary.HasPressed;
            bool menuHeld=pointer.Left ? leftGrip.Held:controls.Secondary.IsPressed;
            if(!held && DateTime.UtcNow<mouseUntil && !click) { Modifiers(false,false); return; }
            if (!Components.VRGUIManager.TryPanelHit(MenuHands.PointerTracking(false,hand),out Vector2 uv))
            { ReleaseMouse(); Modifiers(false,false); return; }
            if (!GetClientRect(window,out Rect rect)) { ReleaseMouse(); Modifiers(false,false); return; }
            var point=new Point { X=(int)(uv.X*(rect.Right-rect.Left-1)),Y=(int)(uv.Y*(rect.Bottom-rect.Top-1)) };
            if (!ClientToScreen(window,ref point)) { ReleaseMouse(); Modifiers(false,false); return; }
            SetCursorPos(point.X,point.Y);
            lastCursor=point;
            // Only a new trigger press over the panel starts a click. Hold supports dragging sliders.
            // The other hand's grip and trigger are Shift and Ctrl.
            var other=pointer.Other;
            bool otherLeft=other==Player.HandL;
            Modifiers(other.pose.isTracked && (otherLeft ? controls.LeftGripPressure:controls.RightGripPressure).Position.X>.55f,
                other.pose.isTracked && (otherLeft ? controls.LeftTriggerPressure:controls.PointerPressure).Position.X>.55f);
            if (click && !held) { mouse_event(2,0,0,0,UIntPtr.Zero); held=true; }
            if (!clickHeld && held) { mouse_event(4,0,0,0,UIntPtr.Zero); held=false; checkTextFocus=true; }
            if (menu && !secondaryHeld) { mouse_event(8,0,0,0,UIntPtr.Zero); secondaryHeld=true; }
            if (!menuHeld && secondaryHeld) { mouse_event(16,0,0,0,UIntPtr.Zero); secondaryHeld=false; }
            float scroll = controls.MenuNavigate.Position.Y;
            if (System.Math.Abs(scroll) > 0.45f && DateTime.UtcNow >= scrollAfter)
            {
                mouse_event(0x0800, 0, 0, unchecked((uint)(scroll > 0 ? 120 : -120)), UIntPtr.Zero);
                scrollAfter = DateTime.UtcNow.AddSeconds(0.13);
            }
        }
    }
}
