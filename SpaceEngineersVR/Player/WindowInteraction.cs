using System;
using VRageMath;

namespace SpaceEngineersVR.Player
{
    internal sealed class WindowInteraction
    {
        private readonly MenuWindow window;
        private readonly InteractionPress[] presses={new InteractionPress(),new InteractionPress()};
        private Controller owner;
        internal int Hover { get; private set; }
        internal int HeldAction { get; private set; }
        internal bool Active => HeldAction!=0;
        internal bool Direct { get; private set; }
        internal bool Captured { get; private set; }
        internal bool Released { get; private set; }
        internal bool Changed { get; private set; }
        internal bool Hit { get; private set; }
        internal Vector3 Point { get; private set; }
        internal WindowInteraction(MenuWindow window) {this.window=window;}
        internal void Reset(bool cancel=false)
        {
            if(cancel) window.Cancel(); else window.Stop();
            foreach(var press in presses) press.Block();
            owner=null; Hover=HeldAction=0; Direct=Captured=Released=Changed=Hit=false;
        }
        internal bool Update(Controller hand,Matrix aim,float seconds,Func<Vector3,bool,int> extraHit=null,Func<Vector3,bool> reachable=null)
        {
            if(Active && hand!=owner) return false;
            if(!hand.pose.isTracked) {Reset(); return false;}
            bool near=Near(aim);
            var press=presses[hand==Player.HandL ? 1:0];
            var input=press.Read(hand,Active ? Direct:near);
            press.Update(true,input);
            var controls=Controls.Static;
            Vector2 navigation=InputRouter.Mode==InputMode.Menu ? controls.MenuNavigate.RawPosition:controls.ThrustRotate.RawPosition;
            int previous=Hover;
            bool wasDrag=window.Drag!=0;
            Sample(aim,near,press.Pressed,input.Down,navigation,seconds,extraHit,reachable);
            if(Captured) {owner=hand; CockpitFeedback.Click(hand);}
            else if(Hover!=0 && Hover!=previous && !Active) CockpitFeedback.Hover(hand);
            if(Active || Released) input.Consume();
            if(wasDrag || window.Drag!=0)
            {
                controls.MenuNavigate.BlockUntilRelease(); controls.ThrustRotate.BlockUntilRelease(); controls.WalkRotate.BlockUntilRelease();
            }
            return Hover!=0 || Active || Released;
        }
        private bool Near(Matrix aim)
        {
            var point=window.Local(aim.Translation);
            return point.Z>=-.025f && point.Z<=.05f;
        }
        internal void Sample(Matrix aim,bool near,bool pressed,bool down,Vector2 navigation,float seconds,Func<Vector3,bool,int> extraHit=null,Func<Vector3,bool> reachable=null)
        {
            Captured=Released=Changed=false;
            if(!aim.IsValid()) {Reset(); return;}
            if(!Active) Direct=near;
            var point=window.Local(aim.Translation); point.Z=0;
            Hit=near || window.Pointer(aim,out point);
            Point=point;
            Hover=Hit && (reachable==null || reachable(point)) ? window.Handle(point,true):0;
            if(Hit && Hover==0 && (reachable==null || reachable(point))) Hover=extraHit?.Invoke(point,near) ?? 0;
            if(Active)
            {
                if(!down) {Released=true; HeldAction=0; window.Stop(); return;}
                if(window.Drag!=0)
                {
                    point=window.Local(aim.Translation,true);
                    if(Direct || window.Pointer(aim,out point,true) || window.Drag==1)
                    {window.Move(aim,point,navigation,seconds); Changed=true;}
                    Hover=HeldAction;
                }
                return;
            }
            if(pressed && Hover!=0)
            {
                Direct=near; HeldAction=Hover; Captured=true;
                if(Hover<=2) window.Begin(Hover,aim,Point);
            }
        }
    }
}
