using System;
using System.Drawing;
using System.Linq;
using HarmonyLib;
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
        private static Action submitted;
        private static string done="DONE";
        internal static bool Hotkeys { get; private set; }
        public static bool Standalone => target!=null && valid!=null;
        public static bool IsOpen => target != null;
        private static bool shift;
        private static DateTime navigateAfter;
        public static int Selected { get; private set; }
        public static string Preview => target==null ? "" : Hotkeys ? HotkeyInput.Status : TextPreview(target);
        private static string PreviewLine(MyGuiControlBase control,out int start,out int caret)
        {
            string value=control is MyGuiControlTextbox box ? box.Text:((MyGuiControlMultilineEditableText)control).Text.ToString();
            if(control is MyGuiControlTextbox password && password.Type==MyGuiControlTextboxType.Password) value=new string('*',value.Length);
            caret=control is MyGuiControlTextbox single ? single.CarriagePositionIndex:(int)CockpitRender.Member(control,"CarriagePositionIndex");
            caret=Math.Max(0,Math.Min(value.Length,caret));
            start=0;
            int end=value.Length;
            if(control is MyGuiControlMultilineEditableText)
            {
                start=caret>0 ? value.LastIndexOf('\n',caret-1)+1:0;
                end=value.IndexOf('\n',caret); if(end<0) end=value.Length;
            }
            if(end-start>36)
            {
                start=Math.Max(start,caret-18);
                end=Math.Min(end,start+36);
                start=Math.Max(0,Math.Min(start,caret));
            }
            return value.Substring(start,end-start);
        }
        internal static string TextPreview(MyGuiControlBase control)
        {
            string line=PreviewLine(control,out int start,out int caret);
            return line.Insert(caret-start,"|");
        }
        internal static void MoveCaret(MyGuiControlBase control,int position)
        {
            if(control==null) return;
            int length=control is MyGuiControlTextbox box ? box.Text.Length:((MyGuiControlMultilineEditableText)control).Text.Length;
            position=Math.Max(0,Math.Min(length,position));
            var type=control is MyGuiControlTextbox ? typeof(MyGuiControlTextbox):typeof(MyGuiControlMultilineText);
            AccessTools.Property(type,"CarriagePositionIndex").SetValue(control,position);
            var selection=AccessTools.Field(type,"m_selection").GetValue(control);
            AccessTools.Method(selection.GetType(),"Reset").Invoke(selection,new object[] {control});
        }
        // Keeps the column when stepping to the previous or next line of a multiline editor.
        internal static int CaretLine(string text,int caret,int step)
        {
            caret=Math.Max(0,Math.Min(text.Length,caret));
            int lineStart=text.LastIndexOf('\n',Math.Max(0,caret-1))+1;
            if(caret==0) lineStart=0;
            int column=caret-lineStart;
            if(step<0)
            {
                if(lineStart==0) return caret;
                int previousStart=lineStart>=2 ? text.LastIndexOf('\n',lineStart-2)+1 : 0;
                return Math.Min(previousStart+column,lineStart-1);
            }
            int lineEnd=text.IndexOf('\n',caret);
            if(lineEnd<0) return caret;
            int nextEnd=text.IndexOf('\n',lineEnd+1);
            return Math.Min(lineEnd+1+column,nextEnd<0 ? text.Length:nextEnd);
        }
        internal static void MoveCaretLine(MyGuiControlBase control,int step)
        {
            if(!(control is MyGuiControlMultilineEditableText editor)) return;
            MoveCaret(control,CaretLine(editor.Text.ToString(),(int)CockpitRender.Member(control,"CarriagePositionIndex"),step));
        }
        internal static int PreviewCaretAt(MyGuiControlBase control,float x)
        {
            string line=PreviewLine(control,out int start,out _);
            using(var bitmap=new Bitmap(1,1))
            using(var graphics=Graphics.FromImage(bitmap))
            using(var font=new Font("Segoe UI",27,FontStyle.Regular,GraphicsUnit.Pixel))
            {
                float width=graphics.MeasureString(line,font).Width;
                float local=x*1024-(1024-width)/2;
                int best=0; float distance=float.MaxValue;
                for(int i=0;i<=line.Length;i++)
                {
                    float offset=i==0 ? 0:graphics.MeasureString(line.Substring(0,i),font).Width;
                    float next=Math.Abs(local-offset);
                    if(next<distance) { distance=next; best=i; }
                }
                return start+best;
            }
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
            string[] rows={"1234567890","qwertyuiop","asdfghjkl;","zxcvbnm,./","`-=[]\\'"};
            for(int row=0;row<rows.Length;row++)
                for(int col=0;col<rows[row].Length;col++) keys.Add(new SurfaceKey(rows[row][col].ToString(),.015f+col*step,.31f+row*.107f,step-gap,.098f));
            float x=.015f;
            foreach(var key in new[] {("Tab",1f),("SHIFT",1.5f),("CTRL",1.25f),("ALT",1.25f),("SPACE",2.5f),("ENTER",1.25f),("DONE",1.25f)})
            { Add(key.Item1,x,.855f,key.Item2); x+=key.Item2*step; }
            string[,] cluster={{"Ins","Home","PgUp"},{"Del","End","PgDn"},{"RMB","MMB","Scr↑"},{null,"↑","Scr↓"},{"←","↓","→"}};
            for(int row=0;row<5;row++)
                for(int col=0;col<3;col++)
                    if(cluster[row,col]!=null) keys.Add(new SurfaceKey(cluster[row,col],nav+col*navStep,.31f+row*.107f,.068f,.098f));
            return keys.ToArray();
        }
        internal static SurfaceKey[] MakeKeys(bool caps,string done="DONE",bool multiline=false)
        {
            var keys=new System.Collections.Generic.List<SurfaceKey>();
            // Full US layout, each row staggered half a key like a physical keyboard; the shift page carries the symbols.
            string[] rows=caps ? new[] { "~!@#$%^&*()_+","QWERTYUIOP{}|","ASDFGHJKL:\"","ZXCVBNM<>?" } : new[] { "`1234567890-=","qwertyuiop[]\\","asdfghjkl;'","zxcvbnm,./" };
            const float pitch=.068f;
            for(int row=0;row<rows.Length;row++)
                for(int col=0;col<rows[row].Length;col++) keys.Add(new SurfaceKey(rows[row][col].ToString(),.025f+row*pitch*.5f+col*pitch,.28f+row*.125f,.061f,.108f));
            string[] navigation={"<<","◀","▶",">>"};
            for(int i=0;i<navigation.Length;i++) keys.Add(new SurfaceKey(navigation[i],.29f+i*.11f,.23f,.10f,.04f));
            keys.AddRange(new[] { new SurfaceKey(caps ? "⇧ abc" : "⇧ ABC",.025f,.79f,.14f,.115f),new SurfaceKey("SPACE",.18f,.79f,multiline ? .115f:.43f,.115f),
                new SurfaceKey("BKSP",.635f,.79f,.155f,.115f),new SurfaceKey(done,.805f,.79f,.17f,.115f) });
            if(multiline) keys.AddRange(new[] { new SurfaceKey("ENTER",.31f,.79f,.115f,.115f),new SurfaceKey("↑",.445f,.79f,.08f,.115f),new SurfaceKey("↓",.54f,.79f,.08f,.115f) });
            keys.Add(new SurfaceKey("Preview",.028f,KeyboardWindow.TopTrim+.035f*(1-KeyboardWindow.TopTrim),
                .944f,.10f*(1-KeyboardWindow.TopTrim)) {Invisible=true});
            foreach(var key in keys)
            {
                var b=key.Bounds;
                key.Bounds=new VRageMath.RectangleF(b.X,(b.Y-KeyboardWindow.TopTrim)/(1-KeyboardWindow.TopTrim),b.Width,b.Height/(1-KeyboardWindow.TopTrim));
            }
            return keys.ToArray();
        }
        internal static bool Repeatable(int key) => !Hotkeys && key>=0 && key<Keys.Length &&
            (Keys[key].Label.Length==1 || Keys[key].Label=="SPACE" || Keys[key].Label=="BKSP" || Keys[key].Label=="◀" || Keys[key].Label=="▶");

        public static void Activate(int key,float previewX=float.NaN)
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
                case "⇧ ABC": case "⇧ abc": shift=!shift; Keys=MakeKeys(shift,done,target is MyGuiControlMultilineEditableText); break;
                case "↑": MoveCaretLine(target,-1); break;
                case "↓": MoveCaretLine(target,1); break;
                case "BKSP": Backspace(target); break;
                case "DONE": Close(); break;
                case "SEND":
                    var chat=screen; var box=target as MyGuiControlTextbox; var send=submitted;
                    Close();
                    if(send!=null) send(); else VrChat.Send(chat,box);
                    break;
                case "<<": MoveCaret(target,0); break;
                case ">>": MoveCaret(target,target is MyGuiControlTextbox single ? single.Text.Length:((MyGuiControlMultilineEditableText)target).Text.Length); break;
                case "◀": MoveCaret(target,(target is MyGuiControlTextbox left ? left.CarriagePositionIndex:(int)CockpitRender.Member(target,"CarriagePositionIndex"))-1); break;
                case "▶": MoveCaret(target,(target is MyGuiControlTextbox right ? right.CarriagePositionIndex:(int)CockpitRender.Member(target,"CarriagePositionIndex"))+1); break;
                case "Preview": if(!float.IsNaN(previewX)) MoveCaret(target,PreviewCaretAt(target,previewX)); break;
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
        internal static void Open(string text,Action<string> changed,Func<bool> isValid,Action onClosed,string submitLabel="DONE",Action onSubmit=null,int maxLength=60)
        {
            if(!FloatingKeyboard.Available || !Player.Headset.pose.isTracked) return;
            Close();
            target=CreateTextTarget(text,changed,maxLength); screen=VRGUIManager.TopScreen;
            valid=isValid; closed=onClosed; submitted=onSubmit; done=submitLabel; Keys=MakeKeys(false,done); Selected=0;
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
        internal static MyGuiControlTextbox CreateTextTarget(string text,Action<string> changed,int maxLength=60)
        {
            var textbox=new MyGuiControlTextbox(defaultText:text,maxLength:maxLength);
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
                while(Keys[Selected].Invisible) Selected=(Selected+(delta>0 ? 1:-1)+Keys.Length)%Keys.Length;
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
            valid=null; closed=null; submitted=null; Hotkeys=false;
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
