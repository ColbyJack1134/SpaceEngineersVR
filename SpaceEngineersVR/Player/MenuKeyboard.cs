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
        private static MyGuiControlBase target;
        private static MyGuiScreenBase screen;
        private static Func<bool> valid;
        private static Action closed;
        private static string done="DONE";
        internal static bool Hotkeys { get; private set; }
        public static bool Standalone => target!=null && valid!=null;
        public static bool IsOpen => target != null;
        private static bool shift;
        private static DateTime navigateAfter;
        public static int Selected { get; private set; }
        public static string Preview => target==null ? "" : Hotkeys ? HotkeyInput.Status : TextPreview(target);
        internal static string TextPreview(MyGuiControlBase control)
        {
            string text=control is MyGuiControlTextbox box ? box.Text:((MyGuiControlMultilineEditableText)control).Text.ToString();
            if(control is MyGuiControlTextbox password && password.Type==MyGuiControlTextboxType.Password) return new string('*',text.Length);
            int caret=control is MyGuiControlTextbox single ? single.CarriagePositionIndex:(int)CockpitRender.Member(control,"CarriagePositionIndex");
            caret=Math.Max(0,Math.Min(text.Length,caret));
            if(control is MyGuiControlMultilineEditableText)
            {
                int start=caret>0 ? text.LastIndexOf('\n',caret-1)+1:0;
                int end=text.IndexOf('\n',caret); if(end<0) end=text.Length;
                start=Math.Max(start,caret-32); end=Math.Min(end,caret+32);
                text=text.Substring(start,end-start); caret-=start;
            }
            return text.Insert(caret,"|");
        }
        internal static void Insert(MyGuiControlBase control,char value)
        {
            if(control is MyGuiControlTextbox box) box.InsertChar(true,value);
            else if(control is MyGuiControlMultilineEditableText editor) editor.InsertChar(true,value);
        }
        internal static void Backspace(MyGuiControlBase control)
        {
            if(control is MyGuiControlTextbox box) box.KeypressBackspace(true);
            else if(control is MyGuiControlMultilineEditableText editor) editor.KeypressBackspace(true);
        }
        public static SurfaceKey[] Keys { get; private set; }=MakeKeys(false);
        internal static SurfaceKey[] MakeHotkeys()
        {
            var keys=new System.Collections.Generic.List<SurfaceKey>();
            const float step=.0725f,gap=.0065f,height=.115f,nav=.76f,navStep=.0745f;
            void Add(string label,float left,float top,float units) => keys.Add(new SurfaceKey(label,left,top,units*step-gap,height));
            string[] function={"Esc","F1","F2","F3","F4","F5","F6","F7","F8","F9","F10","F11","F12"};
            for(int i=0;i<function.Length;i++) keys.Add(new SurfaceKey(function[i],.015f+i*.074f,.195f,.068f,height));
            string[] rows={"1234567890","qwertyuiop","asdfghjkl;","zxcvbnm,./"};
            for(int row=0;row<rows.Length;row++)
                for(int col=0;col<rows[row].Length;col++) Add(rows[row][col].ToString(),.015f+col*step,.335f+row*.13f,1);
            float x=.015f;
            foreach(var key in new[] {("Tab",1f),("SHIFT",1.5f),("CTRL",1.25f),("ALT",1.25f),("SPACE",2.5f),("ENTER",1.25f),("DONE",1.25f)})
            { Add(key.Item1,x,.855f,key.Item2); x+=key.Item2*step; }
            string[,] cluster={{"Ins","Home","PgUp"},{"Del","End","PgDn"},{null,null,null},{null,"↑",null},{"←","↓","→"}};
            for(int row=0;row<5;row++)
                for(int col=0;col<3;col++)
                    if(cluster[row,col]!=null) keys.Add(new SurfaceKey(cluster[row,col],nav+col*navStep,.335f+row*.13f,.068f,height));
            return keys.ToArray();
        }
        internal static SurfaceKey[] MakeKeys(bool caps,string done="DONE",bool multiline=false)
        {
            var keys=new System.Collections.Generic.List<SurfaceKey>();
            string[] rows=caps ? new[] { "!@#$%^&*()","QWERTYUIOP","ASDFGHJKL:","ZXCVBNM<>?" } : new[] { "1234567890","qwertyuiop","asdfghjkl;","zxcvbnm,./" };
            for(int row=0;row<rows.Length;row++)
                for(int col=0;col<rows[row].Length;col++) keys.Add(new SurfaceKey(rows[row][col].ToString(),.025f+col*.095f,.28f+row*.125f,.085f,.108f));
            keys.AddRange(new[] { new SurfaceKey(caps ? "⇧ 123" : "⇧ #@",.025f,.79f,.14f,.115f),new SurfaceKey("SPACE",.18f,.79f,multiline ? .115f:.25f,.115f),
                new SurfaceKey("'",.445f,.79f,.08f,.115f),new SurfaceKey("-",.54f,.79f,.08f,.115f),
                new SurfaceKey("BKSP",.635f,.79f,.155f,.115f),new SurfaceKey(done,.805f,.79f,.17f,.115f) });
            if(multiline) keys.Add(new SurfaceKey("ENTER",.31f,.79f,.115f,.115f));
            foreach(var key in keys)
            {
                var b=key.Bounds;
                key.Bounds=new RectangleF(b.X,(b.Y-KeyboardWindow.TopTrim)/(1-KeyboardWindow.TopTrim),b.Width,b.Height/(1-KeyboardWindow.TopTrim));
            }
            return keys.ToArray();
        }
        internal static bool Repeatable(int key) => !Hotkeys && key>=0 && key<Keys.Length &&
            (Keys[key].Label.Length==1 || Keys[key].Label=="SPACE" || Keys[key].Label=="BKSP");

        public static void Activate(int key)
        {
            if(!IsOpen || key<0 || key>=Keys.Length || !TargetValid() || !target.Enabled || !target.Visible) return;
            string label=Keys[key].Label;
            if(Hotkeys)
            {
                if(label=="DONE") { Close(); return; }
                HotkeyInput.Press(label);
                foreach(var k in Keys) k.Active=HotkeyInput.Latched(k.Label);
                return;
            }
            switch(label)
            {
                case "⇧ 123": case "⇧ #@": shift=!shift; Keys=MakeKeys(shift,done,target is MyGuiControlMultilineEditableText); break;
                case "BKSP": Backspace(target); break;
                case "DONE": Close(); break;
                case "SEND":
                    var chat=screen; var box=target as MyGuiControlTextbox;
                    Close(); VrChat.Send(chat,box); break;
                case "SPACE": Insert(target,' '); break;
                case "ENTER": Insert(target,'\n'); break;
                default: Insert(target,label[0]); break;
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

        internal static MyGuiControlBase EditTarget(MyGuiScreenBase current)
        {
            if(current?.FocusedControl is MyGuiControlMultilineEditableText editor)
                return editor.Enabled && editor.Visible && editor.Selectable ? editor:null;
            return TextTarget(current);
        }
        internal static bool TextHovered(MyGuiScreenBase current) => EditTarget(current)?.IsMouseOver==true;

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
        internal static void OpenHotkeys()
        {
            Open("",_=>{},()=>Sandbox.Game.World.MySession.Static!=null,null);
            if(!IsOpen) return;
            Hotkeys=true; HotkeyInput.Reset(); Keys=MakeHotkeys();
        }
        internal static MyGuiControlTextbox CreateTextTarget(string text,Action<string> changed)
        {
            var textbox=new MyGuiControlTextbox(defaultText:text,maxLength:60);
            textbox.MoveCarriageToEnd();
            textbox.TextChanged+=box=>changed(box.Text);
            return textbox;
        }

        public static void Open(bool reposition = false)
        {
            if(IsOpen) { if(reposition) FloatingKeyboard.Show(true); return; }
            if(!FloatingKeyboard.Available || !Player.Headset.pose.isTracked) return;
            var current=VRGUIManager.TopScreen;
            var textbox=EditTarget(current);
            if(textbox==null) return;
            current.FocusedControl=textbox;
            screen=current; target=textbox; Selected=0;
            done=VrChat.IsChat(current) ? "SEND":"DONE"; Keys=MakeKeys(false,done,target is MyGuiControlMultilineEditableText);
            FloatingKeyboard.Show(reposition,FloatingMenu.Current?.Pose.Translation);
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
            valid=null; closed=null; Hotkeys=false;
            shift=false; done="DONE"; Keys=MakeKeys(false);
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
