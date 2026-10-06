using System;
using VRageMath;

namespace SpaceEngineersVR.Player
{
    internal sealed class WindowInteraction
    {
        private readonly MenuWindow window;
        private readonly InteractionPress[] presses={new InteractionPress(),new InteractionPress()};
        private Controller owner,attached;
        private Matrix heldWrist;
        private DateTime grabbed;
        internal bool DirectHeld => attached!=null;
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
            owner=attached=null; Hover=HeldAction=0; Direct=Captured=Released=Changed=Hit=false;
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
            Vector2 navigation=Navigation(controls);
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
        internal void Grab(Controller hand,MatrixD trackingToWorld)
        {
            attached=Direct && window.Drag!=0 ? hand:null;
            if(attached==null) return;
            grabbed=DateTime.UtcNow;
            MatrixD wrist=ThirdPersonView.Active ? Alignment.Apply(Alignment.HandKey(hand),CockpitHandPose.GripWrist(hand.GripTracking)) :
                TrackedArms.FreeWristWorld(hand)*MatrixD.Invert(trackingToWorld);
            heldWrist=(Matrix)(wrist*MatrixD.Invert(MatrixD.CreateTranslation(window.GrabPoint)*(MatrixD)window.Pose));
        }
        internal bool TryAttachment(Controller hand,MatrixD trackingToWorld,bool tracking,out MatrixD wrist,out Vector3D point,out float blend)
        {
            MatrixD parent=MatrixD.CreateTranslation(window.GrabPoint)*(MatrixD)window.Pose*(tracking ? MatrixD.Identity:trackingToWorld);
            wrist=(MatrixD)heldWrist*parent; point=parent.Translation;
            blend=MathHelper.Clamp((float)(DateTime.UtcNow-grabbed).TotalSeconds/.09f,0,1);
            return attached!=null && attached==hand;
        }
        internal static MatrixD Aim(Controller hand,MatrixD trackingToWorld)
        {
            if(ThirdPersonView.Active) return MenuHands.TryPointPose(hand.GripTracking,out var tracked,hand) ? tracked:(MatrixD)hand.AimTracking;
            return TrackedArms.TryFreePointPose(hand,out var finger) ? finger*MatrixD.Invert(trackingToWorld):(MatrixD)hand.AimTracking;
        }
        private bool OnWindow(Vector3 point) => Math.Abs(point.X)<window.Width/2+.065f && point.Y<window.Height/2+.03f && point.Y> -window.Height/2-.10f;
        // Fingertip beats laser, a pressing laser beats an idle one; ties keep the current hand.
        internal Controller PickHand(Controller current,Func<Controller,bool> free,Func<Controller,MatrixD> aim)
        {
            if(Active) return current;
            int Score(Controller hand)
            {
                if(!free(hand)) return 0;
                var pose=(Matrix)aim(hand);
                var local=window.Local(pose.Translation);
                if(local.Z>=-.025f && local.Z<=.05f && OnWindow(local)) return 3;
                if((hand==Player.HandR || PointerHand.LeftRayAllowed) && window.Pointer(pose,out var hit) && OnWindow(hit))
                    return InteractionInput.Read(hand,false).Pressure>.25f ? 2:1;
                return 0;
            }
            int right=Score(Player.HandR),left=Score(Player.HandL);
            return left>right ? Player.HandL : right>left || right==0 ? Player.HandR : current;
        }
        internal static Func<Vector3,bool> Reachable(MenuWindow window,MatrixD aim,MatrixD trackingToWorld) => point =>
        {
            var hit=Vector3D.Transform(point,(MatrixD)window.Pose);
            float distance=(float)Vector3D.Distance(aim.Translation,hit);
            return ThirdPersonView.Active || HandInteraction.ObstacleDistance(aim*trackingToWorld,distance+.01f)>=distance-.02f;
        };
        internal bool Ray(MatrixD aim,Func<Vector3,bool> reachable,out Vector3D start,out Vector3D end)
        {
            start=aim.Translation; end=Vector3D.Transform(Point,(MatrixD)window.Pose);
            return Hit && !Direct && OnWindow(Point) && reachable(Point);
        }
        // The right stick is a different action in each action set.
        internal static Vector2 Navigation(Controls controls) => InputRouter.Mode==InputMode.Menu ? controls.MenuNavigate.RawPosition :
            controls.ThrustRotate.Active ? controls.ThrustRotate.RawPosition : controls.WalkRotate.RawPosition;
        // Aiming at a window claims the hand's trigger and grip until release, so near misses never fire.
        // Left trigger pressure stays unblocked because a laser click needs it to acquire.
        internal static void Claim(Controller hand)
        {
            var controls=Controls.Static;
            if(hand==Player.HandL) { controls.LeftClick.BlockUntilRelease(); controls.LeftGripPressure.BlockUntilRelease(false); controls.CrouchOrClimbDown.BlockUntilRelease(); controls.ThrustUp.BlockUntilRelease(); controls.ThrustDown.BlockUntilRelease(); }
            else { controls.Primary.BlockUntilRelease(); controls.Secondary.BlockUntilRelease(); }
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
                if(!down) {Released=true; HeldAction=0; attached=null; window.Stop(); return;}
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
                if(Hover<=2) window.Begin(Hover,aim,Point,near);
            }
        }
    }
}
