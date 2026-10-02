using System;
using System.Linq;
using Sandbox.Game.Gui;
using Sandbox.Game.Screens.Helpers;
using Sandbox.Graphics.GUI;
using SpaceEngineersVR.Player.Components;
using SpaceEngineersVR.Plugin;
using VRageMath;

namespace SpaceEngineersVR.Player
{
    internal static class MenuKeyboard
    {
        private static MyGuiControlTextbox target;
        private static MyGuiScreenBase screen;
        private static Func<bool> valid;
        private static Action closed;
        public static bool Standalone => target!=null && valid!=null;
        public static bool IsOpen => target != null;
        private static bool shift;
        private static DateTime navigateAfter;
        public static int Selected { get; private set; }
        public static string Preview => target==null ? "" : target.Type==MyGuiControlTextboxType.Password ? new string('*',target.Text.Length) :
            target.Text.Insert(Math.Min(target.Text.Length,target.CarriagePositionIndex),"|");
        public static SurfaceKey[] Keys { get; private set; }=MakeKeys(false);
        internal static SurfaceKey[] MakeKeys(bool caps)
        {
            var keys=new System.Collections.Generic.List<SurfaceKey>();
            string[] rows=caps ? new[] { "!@#$%^&*()","QWERTYUIOP","ASDFGHJKL:","ZXCVBNM<>?" } : new[] { "1234567890","qwertyuiop","asdfghjkl;","zxcvbnm,./" };
            for(int row=0;row<rows.Length;row++)
                for(int col=0;col<rows[row].Length;col++) keys.Add(new SurfaceKey(rows[row][col].ToString(),.025f+col*.095f,.28f+row*.125f,.085f,.108f));
            keys.AddRange(new[] { new SurfaceKey("SHIFT",.025f,.79f,.14f,.115f),new SurfaceKey("SPACE",.18f,.79f,.25f,.115f),
                new SurfaceKey("'",.445f,.79f,.08f,.115f),new SurfaceKey("-",.54f,.79f,.08f,.115f),
                new SurfaceKey("BKSP",.635f,.79f,.155f,.115f),new SurfaceKey("DONE",.805f,.79f,.17f,.115f) });
            foreach(var key in keys)
            {
                var b=key.Bounds;
                key.Bounds=new RectangleF(b.X,(b.Y-KeyboardWindow.TopTrim)/(1-KeyboardWindow.TopTrim),b.Width,b.Height/(1-KeyboardWindow.TopTrim));
            }
            return keys.ToArray();
        }
        public static void Activate(int key)
        {
            if(!IsOpen || key<0 || key>=Keys.Length || !TargetValid() || !target.Enabled || !target.Visible) return;
            string label=Keys[key].Label;
            switch(label)
            {
                case "SHIFT": shift=!shift; Keys=MakeKeys(shift); break;
                case "BKSP": target.KeypressBackspace(true); break;
                case "DONE": Close(); break;
                case "SPACE": target.InsertChar(true,' '); break;
                default: target.InsertChar(true,label[0]); break;
            }
        }

        internal static MyGuiControlTextbox TextTarget(MyGuiScreenBase current)
        {
            var textbox=current?.FocusedControl as MyGuiControlTextbox;
            if (textbox==null && current?.FocusedControl is MyGuiControlSearchBox focusedSearch && focusedSearch.Enabled && focusedSearch.Visible)
                textbox=focusedSearch.TextBox;
            // G-menu search remains reachable after clicking a category, grid item or toolbar slot.
            if (textbox==null && (current is MyGuiScreenToolbarConfigBase || current is MyGuiBlueprintScreen_Reworked || current is GUI.ActionBrowser))
                textbox=current.Controls.OfType<MyGuiControlSearchBox>().FirstOrDefault(s=>s.Visible && s.Enabled)?.TextBox;
            return textbox!=null && textbox.Enabled && textbox.Visible ? textbox : null;
        }

        private static bool TargetValid() => screen==VRGUIManager.TopScreen && (valid?.Invoke() ?? true);
        internal static void Open(string text,Action<string> changed,Func<bool> isValid,Action onClosed)
        {
            if(!FloatingKeyboard.Available || !Player.Headset.pose.isTracked) return;
            Close();
            target=CreateTextTarget(text,changed); screen=VRGUIManager.TopScreen;
            valid=isValid; closed=onClosed; Selected=0;
            Main.MenuOpen=true;
            InputRouter.Update();
            FloatingKeyboard.Show(false);
            MenuPointer.Release(); Controls.Static.BlockUntilRelease();
        }
        internal static MyGuiControlTextbox CreateTextTarget(string text,Action<string> changed)
        {
            var textbox=new MyGuiControlTextbox(defaultText:text,maxLength:60);
            textbox.TextChanged+=box=>changed(box.Text);
            return textbox;
        }

        public static void Open(bool reposition = false)
        {
            if(IsOpen) { if(reposition) FloatingKeyboard.Show(true); return; }
            if(!FloatingKeyboard.Available || !Player.Headset.pose.isTracked) return;
            var current=VRGUIManager.TopScreen;
            var textbox=TextTarget(current);
            if(textbox==null) return;
            current.FocusedControl=textbox;
            screen=current; target=textbox; Selected=0;
            FloatingKeyboard.Show(reposition);
            MenuPointer.Release(); Controls.Static.BlockUntilRelease();
        }

        public static void Update()
        {
            if (target != null && (!Main.MenuOpen || !TargetValid() || !MenuPointer.GameFocused || !target.Enabled || !target.Visible)) Close();
            if(!IsOpen) return;
            var c=Controls.Static;
            var stick=c.MenuNavigate.Position;
            if(stick.LengthSquared()<.2f) navigateAfter=DateTime.MinValue;
            else if(DateTime.UtcNow>=navigateAfter)
            {
                int delta=Math.Abs(stick.X)>Math.Abs(stick.Y) ? (stick.X>0 ? 1 : -1) : (stick.Y>0 ? -10 : 10);
                Selected=(Selected+delta+Keys.Length)%Keys.Length;
                navigateAfter=DateTime.UtcNow.AddMilliseconds(210);
                Player.HandR.Vibrate(0,.012f,130,.15f);
            }
            if(c.Interact.HasPressed) Activate(Selected);
        }

        public static void Close()
        {
            if (target == null) return;
            bool standalone=Standalone; var callback=closed;
            FloatingKeyboard.Close();
            valid=null; closed=null;
            target = null;
            screen = null;
            callback?.Invoke();
            if(standalone)
            {
                Main.MenuOpen=Sandbox.Game.World.MySession.Static==null || VRGUIManager.IsAnyDialogOpen();
                InputRouter.Update();
            }
            Controls.Static.BlockUntilRelease();
        }
    }
}
