using System;
using VRageMath;

namespace SpaceEngineersVR.Player.Control
{
    internal sealed class Diorama
    {
        internal sealed class Scene
        {
            public readonly Vector3D Target;
            public readonly MatrixD Reference;
            public readonly long Timestamp;
            public Scene(Vector3D target,MatrixD reference,long timestamp)
            { Target=target; Reference=reference; Timestamp=timestamp; }
        }
        public const double MinScale=.1,MaxScale=100000;
        public double UnitsPerMeter { get; private set; }=1;
        public MatrixD Orientation { get; private set; }=MatrixD.Identity;
        public Vector3D Center { get; private set; }
        private MatrixD left,right,rawLeft,rawRight;
        private bool armed,poseReady;
        public int Hands { get; private set; }
        public bool Held => Hands!=0;
        public double LastTranslation { get; private set; }
        public double LastRotation { get; private set; }

        public void Fit(double diameter,MatrixD orientation,Vector3D center)
        {
            UnitsPerMeter=MathHelper.Clamp(diameter/.8,1,MaxScale);
            Orientation=orientation.GetOrientation(); Center=center;
            Cancel();
        }
        public void Cancel() { Hands=0; armed=poseReady=false; }
        public MatrixD Anchor(Vector3D target) => Anchor(target,MatrixD.Identity);
        public MatrixD Anchor(Vector3D target,MatrixD reference)
        {
            var result=Orientation*reference;
            result.Translation=target-Vector3D.TransformNormal(Center,result)*UnitsPerMeter;
            return result;
        }
        public void ChangeReference(MatrixD before,MatrixD after)
        {
            Orientation=VrMath.Rigid(Orientation*before*MatrixD.Transpose(after));
            Cancel();
        }
        public void Rebase(Matrix oldOrigin,Matrix newOrigin)
        {
            MatrixD change=(MatrixD)oldOrigin*MatrixD.Invert(newOrigin);
            Center=Vector3D.Transform(Center,change);
            Orientation=VrMath.Rigid(MatrixD.Transpose(change.GetOrientation())*Orientation);
            Cancel();
        }
        public bool Input(bool available,float leftPressure,float rightPressure)
        {
            if(!available || float.IsNaN(leftPressure) || float.IsNaN(rightPressure)) { Cancel(); return false; }
            int next=(leftPressure>.025f ? 1:0)|(rightPressure>.025f ? 2:0);
            if(next==0) { Hands=0; poseReady=false; armed=true; return false; }
            bool start=!Held;
            if(start && (!armed || leftPressure<=.55f || rightPressure<=.55f)) return false;
            if(next!=Hands) poseReady=false;
            Hands=next; armed=false;
            return start;
        }
        public bool Move(MatrixD newLeft,MatrixD newRight,double seconds)
        {
            LastTranslation=LastRotation=0;
            if(!Held) return false;
            if(!newLeft.Translation.IsValid() || !newRight.Translation.IsValid()) { Cancel(); return false; }
            if(!poseReady)
            {
                left=rawLeft=newLeft; right=rawRight=newRight; poseReady=true;
                return false;
            }
            if(((Hands&1)!=0 && Discontinuous(rawLeft,newLeft)) || ((Hands&2)!=0 && Discontinuous(rawRight,newRight)))
            { Cancel(); return false; }
            var filteredLeft=Filter(left,newLeft,seconds);
            var filteredRight=Filter(right,newRight,seconds);
            rawLeft=newLeft; rawRight=newRight;
            var oldCenter=Center;
            if(Hands==3)
            {
                var oldMid=(left.Translation+right.Translation)*.5;
                var newMid=(filteredLeft.Translation+filteredRight.Translation)*.5;
                var before=right.Translation-left.Translation;
                var after=filteredRight.Translation-filteredLeft.Translation;
                double scale=1;
                var rotation=MatrixD.Identity;
                if(before.Length()>=.08 && after.Length()>=.08)
                {
                    double nextScale=MathHelper.Clamp(UnitsPerMeter*before.Length()/after.Length(),MinScale,MaxScale);
                    scale=UnitsPerMeter/nextScale; UnitsPerMeter=nextScale;
                    rotation=PairRotation(before,after,left,right,filteredLeft,filteredRight);
                }
                Center=Vector3D.TransformNormal(Center-oldMid,rotation)*scale+newMid;
                Orientation=VrMath.Rigid(MatrixD.Transpose(rotation)*Orientation);
                var q=QuaternionD.CreateFromRotationMatrix(rotation);
                LastRotation=2*Math.Acos(Math.Min(1,Math.Abs(q.W)));
            }
            else Center+=Hands==1 ? filteredLeft.Translation-left.Translation : filteredRight.Translation-right.Translation;
            LastTranslation=Vector3D.Distance(Center,oldCenter);
            left=filteredLeft; right=filteredRight;
            return true;
        }
        private static MatrixD Filter(MatrixD previous,MatrixD next,double seconds)
        {
            if(seconds<=0) return next;
            seconds=Math.Min(seconds,.05);
            double speed=Vector3D.Distance(previous.Translation,next.Translation)/seconds;
            double alpha=1-Math.Exp(-seconds/(.018-.012*MathHelper.Clamp(speed/.5,0,1)));
            var a=QuaternionD.CreateFromRotationMatrix(previous);
            var b=QuaternionD.CreateFromRotationMatrix(next);
            var result=MatrixD.CreateFromQuaternion(QuaternionD.Slerp(a,b,alpha));
            result.Translation=Vector3D.Lerp(previous.Translation,next.Translation,alpha);
            return result;
        }
        private static bool Discontinuous(MatrixD before,MatrixD after)
        {
            var a=QuaternionD.CreateFromRotationMatrix(before);
            var b=QuaternionD.CreateFromRotationMatrix(after);
            return Vector3D.Distance(before.Translation,after.Translation)>.35 ||
                Math.Abs(a.X*b.X+a.Y*b.Y+a.Z*b.Z+a.W*b.W)<.707;
        }
        private static MatrixD PairRotation(Vector3D before,Vector3D after,MatrixD oldLeft,MatrixD oldRight,MatrixD newLeft,MatrixD newRight)
        {
            before.Normalize(); after.Normalize();
            var axis=Vector3D.Cross(before,after);
            double dot=MathHelper.Clamp(Vector3D.Dot(before,after),-1,1);
            var swing=MatrixD.Identity;
            if(axis.LengthSquared()>1e-12) swing=MatrixD.CreateFromAxisAngle(Vector3D.Normalize(axis),Math.Atan2(axis.Length(),dot));
            else if(dot<0) return MatrixD.Identity;
            var l=QuaternionD.CreateFromRotationMatrix(MatrixD.Transpose(oldLeft.GetOrientation())*newLeft.GetOrientation());
            var r=QuaternionD.CreateFromRotationMatrix(MatrixD.Transpose(oldRight.GetOrientation())*newRight.GetOrientation());
            var average=MatrixD.CreateFromQuaternion(QuaternionD.Slerp(l,r,.5));
            // Positions supply swing; common wrist rotation supplies the remaining twist axis.
            var residual=MatrixD.Transpose(swing)*average;
            return swing*MatrixD.CreateFromAxisAngle(after,ObserverFollow.Twist(residual,after));
        }
    }
}
