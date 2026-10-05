using System;
using SpaceEngineersVR.Player.Control;
using VRageMath;

namespace SpaceEngineersVR.Player
{
    internal sealed class MenuWindow
    {
        public const float DefaultWidth=2.4f, MinWidth=.7f, MaxWidth=3.2f;
        public Matrix Pose=Matrix.Identity;
        public float Width=DefaultWidth,Aspect=9f/16,BarOffset=.035f;
        public float MinimumWidth=MinWidth,MaximumWidth=MaxWidth,PointerRange=4;
        public float Height => Width*Aspect;
        public int Drag { get; private set; }
        private Matrix startPose,relative;
        private Vector3 startPoint;
        private float startWidth,startAspect;
        private readonly InputGate navigation=new InputGate();
        public void Place(Matrix head)
        { Place(head,DefaultWidth,new Vector3(0,0,-2.2f)); }
        internal void Place(Matrix head,float width,Vector3 offset,float pitch=0)
        { Stop(); Width=width; Pose=Matrix.CreateRotationX(pitch)*Matrix.CreateTranslation(offset)*VrMath.TrackingOrigin(head); }
        internal Vector3 Local(Vector3 point,bool captured=false) => Vector3.Transform(point,Matrix.Invert(captured ? startPose:Pose));
        internal Vector2 UV(Vector3 point) => new Vector2(.5f+point.X/Width,.5f-point.Y/Height);
        public int Handle(Vector3 point,bool outside=false)
        {
            if(outside && point.Y>=-Height/2) return 0;
            float y=point.Y+Height/2+BarOffset;
            if(Math.Abs(y)<.045f)
            {
                if(Math.Abs(point.X)<.19f) return 1;
            }
            if(point.X>Width/2-.065f && point.X<Width/2+.065f && Math.Abs(y)<.065f) return 2;
            return 0;
        }
        public bool Pointer(Matrix aim,out Vector3 point,bool captured=false)
        {
            var local=aim*Matrix.Invert(captured ? startPose : Pose); point=Vector3.Zero;
            if(!local.IsValid() || local.Translation.Z<=0 || local.Forward.Z>=-.0001f) return false;
            float distance=-local.Translation.Z/local.Forward.Z;
            if(distance>PointerRange) return false;
            point=local.Translation+local.Forward*distance; return point.IsValid();
        }
        public void Begin(int kind,Matrix aim,Vector3 point)
        {
            if(kind<1 || kind>2 || !aim.IsValid() || !point.IsValid()) return;
            startPose=Pose; startWidth=Width; startAspect=Aspect; startPoint=point;
            relative=Pose*Matrix.Invert(aim); Drag=kind; navigation.Block();
        }
        public void Move(Matrix aim,Vector3 point,Vector2 stick=default(Vector2),float seconds=0)
        {
            if(!aim.IsValid() || !point.IsValid()) { Cancel(); return; }
            if(Drag==1)
            {
                var axis=stick.IsValid() ? new Vector2(VrMath.Deadzone(stick.X),VrMath.Deadzone(stick.Y)) : Vector2.Zero;
                navigation.Update(stick.IsValid(),axis!=Vector2.Zero);
                if(navigation.Held && seconds>0 && seconds.IsValid())
                {
                    if(axis.LengthSquared()>1) axis.Normalize();
                    float step=.8f*Math.Min(seconds,.05f);
                    var p=relative.Translation;
                    if(axis.X!=0)
                    {
                        float oldWidth=Width;
                        Width=MathHelper.Clamp(Width+axis.X*step,MinimumWidth,MaximumWidth);
                        p+=relative.Up*((Width-oldWidth)*Aspect/2);
                    }
                    if(axis.Y!=0) p.Z=MathHelper.Clamp(p.Z-axis.Y*step,-3.5f,-.35f);
                    relative.Translation=p;
                }
                Pose=VrMath.Affine(relative*aim);
            }
            else if(Drag==2)
            {
                var delta=point-startPoint;
                Width=MathHelper.Clamp(startWidth+(delta.X-delta.Y*startAspect)/(1+startAspect*startAspect),MinimumWidth,MaximumWidth);
                Pose=startPose;
                Pose.Translation+=Pose.Right*((Width-startWidth)/2)-Pose.Up*((Width-startWidth)*startAspect/2);
            }
        }
        public void Cancel() { if(Drag!=0) { Pose=startPose; Width=startWidth; } Stop(); }
        public void Stop() { Drag=0; navigation.Block(); }
    }
}
