using System;
using Sandbox.Game.World;
using VRage.Input;

namespace SpaceEngineersVR.Player
{
    // Keyboard taps from the VR hotkey keyboard, held for a few updates so native controls and mods both see them.
    internal static class HotkeyInput
    {
        private const int Hold=6;
        private static MyKeys key;
        private static int pressed=int.MinValue/2;
        private static bool shift,control,alt,heldShift,heldControl,heldAlt;
        private static string last="";
        private static int Frame => MySession.Static?.GameplayFrameCounter ?? 0;
        internal static string Status => (shift ? "Shift+":"")+(control ? "Ctrl+":"")+(alt ? "Alt+":"")+(shift || control || alt ? "" : last);
        internal static void Reset() { shift=control=alt=false; last=""; pressed=int.MinValue/2; }
        internal static bool Latched(string label) => label=="SHIFT" ? shift : label=="CTRL" ? control : label=="ALT" && alt;
        internal static void Press(string label)
        {
            switch(label)
            {
                case "SHIFT": shift=!shift; return;
                case "CTRL": control=!control; return;
                case "ALT": alt=!alt; return;
            }
            var mapped=Map(label);
            if(mapped==MyKeys.None) return;
            key=mapped; pressed=Frame;
            heldShift=shift; heldControl=control; heldAlt=alt;
            last=(shift ? "Shift+":"")+(control ? "Ctrl+":"")+(alt ? "Alt+":"")+(label.Length==1 ? label.ToUpperInvariant():label);
            shift=control=alt=false;
        }
        private static MyKeys Map(string label)
        {
            switch(label)
            {
                case "SPACE": return MyKeys.Space;
                case "ENTER": return MyKeys.Enter;
                case "Esc": return MyKeys.Escape;
                case "Tab": return MyKeys.Tab;
                case "Ins": return MyKeys.Insert;
                case "Del": return MyKeys.Delete;
                case "Home": return MyKeys.Home;
                case "End": return MyKeys.End;
                case "PgUp": return MyKeys.PageUp;
                case "PgDn": return MyKeys.PageDown;
                case "↑": return MyKeys.Up;
                case "↓": return MyKeys.Down;
                case "←": return MyKeys.Left;
                case "→": return MyKeys.Right;
            }
            if(label.Length>1 && label[0]=='F' && int.TryParse(label.Substring(1),out int f) && f>=1 && f<=12) return (MyKeys)((int)MyKeys.F1+f-1);
            if(label.Length!=1) return MyKeys.None;
            char c=char.ToUpperInvariant(label[0]);
            if(c>='A' && c<='Z' || c>='0' && c<='9') return (MyKeys)c;
            switch(c)
            {
                case ';': return MyKeys.OemSemicolon;
                case ',': return MyKeys.OemComma;
                case '.': return MyKeys.OemPeriod;
                case '/': return MyKeys.OemQuestion;
                default: return MyKeys.None;
            }
        }
        private static bool Matches(MyKeys k) => k==key ||
            heldShift && (k==MyKeys.Shift || k==MyKeys.LeftShift) || heldControl && (k==MyKeys.Control || k==MyKeys.LeftControl) ||
            heldAlt && (k==MyKeys.Alt || k==MyKeys.LeftAlt);
        internal static bool Down(MyKeys k) { int age=Frame-pressed; return age>=1 && age<=Hold && Matches(k); }
        internal static bool Was(MyKeys k) { int age=Frame-pressed; return age>=2 && age<=Hold+1 && Matches(k); }
        internal static bool NewPressed(MyKeys k) => Frame-pressed==1 && Matches(k);
        internal static bool NewReleased(MyKeys k) => Frame-pressed==Hold+1 && Matches(k);
    }
}
