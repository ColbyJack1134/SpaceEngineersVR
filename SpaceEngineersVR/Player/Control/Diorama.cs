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
        private MatrixD rawLeft,rawRight;
        private bool armed,poseReady,releasePending;
        private int sampledHands;
        private Vector3D panVelocity,rotationVelocity,coastPan,coastRotation,coastPivot,splitPanVelocity,splitRotationVelocity,splitPivot;
        private double zoomVelocity,coastZoom,coastAge,splitZoomVelocity,splitAge;
        private const double PanGain=1.2,ZoomGain=1.15,RotationGain=1.45,CoastTime=.48,CoastDuration=1.8,SplitReleaseTime=.1;
        private const double PanReleaseGain=.8,ZoomReleaseGain=1.5,ReleaseGain=.85;
        private const double RotationReleaseGain=.30,RotationCoastTime=.24,RotationCoastDuration=.9;
        public double PanSensitivity=1,ZoomSensitivity=1,RotationSensitivity=1,PanGlide=1,ZoomGlide=1,RotationGlide=1;
        public int Hands { get; private set; }
        public bool Held => Hands!=0;
        public bool Coasting => coastPan.LengthSquared()>0 || coastZoom!=0 || coastRotation.LengthSquared()>0;
        private bool SplitRelease => splitZoomVelocity!=0 || splitRotationVelocity.LengthSquared()>0;
        public double LastTranslation { get; private set; }
        public double LastRotation { get; private set; }

        public void Fit(double diameter,MatrixD orientation,Vector3D center)
        {
            UnitsPerMeter=MathHelper.Clamp(diameter/.8,1,MaxScale);
            Orientation=orientation.GetOrientation(); Center=center;
            Cancel();
        }
        public void Cancel()
        {
            Hands=sampledHands=0; armed=poseReady=false;
            panVelocity=rotationVelocity=Vector3D.Zero; zoomVelocity=0; Brake();
        }
        private void Brake()
        {
            releasePending=false; coastPan=coastRotation=Vector3D.Zero; coastZoom=coastAge=0;
            ClearSplitRelease();
        }
        private void ClearSplitRelease() { splitZoomVelocity=splitAge=0; splitPanVelocity=splitRotationVelocity=splitPivot=Vector3D.Zero; }
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
        public bool Input(bool available,float leftPressure,float rightPressure,bool flightActive=false)
        {
            if(!available || float.IsNaN(leftPressure) || float.IsNaN(rightPressure)) { Cancel(); return false; }
            int next=(leftPressure>.025f ? 1:0)|(rightPressure>.025f ? 2:0);
            if(next==0)
            {
                if(Held) releasePending=sampledHands!=0;
                Hands=0; poseReady=false; armed=true;
                if(flightActive) Brake();
                return false;
            }
            bool start=!Held;
            if(start) Brake();
            if(start && (!armed || leftPressure<=.55f || rightPressure<=.55f)) return false;
            if(next!=Hands) poseReady=false;
            Hands=next; armed=false;
            return start;
        }
        public bool Move(MatrixD newLeft,MatrixD newRight,double seconds)
        {
            LastTranslation=LastRotation=0;
            if(!Held && !releasePending && !Coasting) return false;
            if(!newLeft.Translation.IsValid() || !newRight.Translation.IsValid()) { Cancel(); return false; }
            if(double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds<0) { Cancel(); return false; }
            int trackedHands=SplitRelease ? 3 : sampledHands;
            if(trackedHands!=0 &&
                (((trackedHands&1)!=0 && Discontinuous(rawLeft,newLeft)) || ((trackedHands&2)!=0 && Discontinuous(rawRight,newRight))))
            { Cancel(); return false; }
            if(seconds>.1) { Brake(); poseReady=false; panVelocity=rotationVelocity=Vector3D.Zero; zoomVelocity=0; }
            if(!Held)
            {
                if(releasePending) Release(newLeft,newRight,seconds);
                sampledHands=0;
                return Coast(seconds);
            }
            if(!poseReady)
            {
                if(sampledHands==3 && Hands!=3)
                {
                    // Keep the two-hand throw briefly while the grip buttons release at different times.
                    splitZoomVelocity=ZoomRelease(zoomVelocity,newLeft,newRight,seconds)!=0 ? zoomVelocity:0;
                    splitRotationVelocity=RotationRelease(rotationVelocity,newLeft,newRight,seconds).LengthSquared()>0 ? rotationVelocity:Vector3D.Zero;
                    splitPanVelocity=panVelocity; splitAge=0;
                    splitPivot=HandCenter(newLeft,newRight,3);
                }
                else ClearSplitRelease();
                rawLeft=newLeft; rawRight=newRight; poseReady=true;
                sampledHands=Hands; panVelocity=rotationVelocity=Vector3D.Zero; zoomVelocity=0;
                return false;
            }
            if(SplitRelease)
            {
                splitAge+=seconds;
                if(ZoomRelease(splitZoomVelocity,newLeft,newRight,seconds)==0) splitZoomVelocity=0;
                if(RotationRelease(splitRotationVelocity,newLeft,newRight,seconds).LengthSquared()==0) splitRotationVelocity=Vector3D.Zero;
                if(splitAge>SplitReleaseTime || !SplitRelease) ClearSplitRelease();
            }
            var pan=HandCenter(newLeft,newRight,Hands)-HandCenter(rawLeft,rawRight,Hands);
            double beforeSpan=Vector3D.Distance(rawLeft.Translation,rawRight.Translation);
            double afterSpan=Vector3D.Distance(newLeft.Translation,newRight.Translation);
            bool scaling=Hands==3 && beforeSpan>=.08 && afterSpan>=.08;
            double zoom=scaling ? Math.Log(afterSpan/beforeSpan) : 0;
            var rotation=scaling ? RotationVector(PairRotation(rawLeft,rawRight,newLeft,newRight)) : Vector3D.Zero;
            var pivot=HandCenter(rawLeft,rawRight,Hands);
            if(seconds>0)
            {
                var speed=pan/seconds;
                double rate=zoom/seconds;
                pan=Smooth(ref panVelocity,speed,seconds,.065-.025*MathHelper.Clamp(speed.Length()/.5,0,1))*PanGain*PanSensitivity;
                if(scaling)
                {
                    var velocity=new Vector3D(zoomVelocity,0,0);
                    zoom=Smooth(ref velocity,new Vector3D(rate,0,0),seconds,.08-.03*MathHelper.Clamp(Math.Abs(rate),0,1)).X*ZoomGain*ZoomSensitivity;
                    zoomVelocity=velocity.X;
                }
                else zoomVelocity=0;
                rotation=Smooth(ref rotationVelocity,rotation*(RotationGain*RotationSensitivity/seconds),seconds,.075);
            }
            else { panVelocity=rotationVelocity=Vector3D.Zero; zoomVelocity=0; }
            rawLeft=newLeft; rawRight=newRight;
            var oldCenter=Center;
            if(Hands==3)
            {
                double angle=rotation.Length();
                var turn=angle>1e-12 ? MatrixD.CreateFromAxisAngle(rotation/angle,angle) : MatrixD.Identity;
                Center=Vector3D.TransformNormal(Center-pivot,turn)*Scale(zoom)+pivot+pan;
                Orientation=VrMath.Rigid(MatrixD.Transpose(turn)*Orientation);
                LastRotation=angle;
            }
            else Center+=pan;
            LastTranslation=Vector3D.Distance(Center,oldCenter);
            return true;
        }
        private static Vector3D RotationVector(MatrixD rotation)
        {
            var q=QuaternionD.CreateFromRotationMatrix(rotation);
            var vector=new Vector3D(q.X,q.Y,q.Z);
            double length=vector.Length();
            return length<1e-12 ? Vector3D.Zero : vector*((q.W<0 ? -1:1)*2*Math.Atan2(length,Math.Abs(q.W))/length);
        }
        private static Vector3D HandCenter(MatrixD a,MatrixD b,int hands) =>
            hands==3 ? (a.Translation+b.Translation)*.5 : hands==1 ? a.Translation : b.Translation;
        private static Vector3D Smooth(ref Vector3D velocity,Vector3D target,double seconds,double time)
        {
            double decay=Math.Exp(-seconds/time);
            var delta=target*seconds+(velocity-target)*(time*(1-decay));
            velocity=target+(velocity-target)*decay;
            return delta;
        }
        private double Scale(double zoom)
        {
            double next=MathHelper.Clamp(UnitsPerMeter*Math.Exp(-zoom),MinScale,MaxScale);
            double scale=UnitsPerMeter/next; UnitsPerMeter=next;
            if(next==MinScale || next==MaxScale) zoomVelocity=coastZoom=0;
            return scale;
        }
        private void Release(MatrixD newLeft,MatrixD newRight,double seconds)
        {
            if(seconds<=0 || sampledHands==0) { Brake(); return; }
            bool split=SplitRelease && splitAge+seconds<=SplitReleaseTime;
            int hands=split ? 3:sampledHands;
            // Use the fresh release pose so filter catch-up cannot turn a stopped hand into a throw.
            var speed=(HandCenter(newLeft,newRight,hands)-HandCenter(rawLeft,rawRight,hands))/seconds;
            var pan=PanRelease(split ? splitPanVelocity:panVelocity,speed);
            double zoom=hands==3 ? ZoomRelease(split ? splitZoomVelocity:zoomVelocity,newLeft,newRight,seconds):0;
            var rotation=hands==3 ? RotationRelease(split ? splitRotationVelocity:rotationVelocity,newLeft,newRight,seconds):Vector3D.Zero;
            var pivot=split ? splitPivot : HandCenter(newLeft,newRight,hands);
            Brake(); coastPan=pan*ReleaseGain*PanGlide; coastZoom=zoom*ReleaseGain*ZoomGlide; coastRotation=rotation; coastPivot=pivot;
            panVelocity=rotationVelocity=Vector3D.Zero; zoomVelocity=0;
        }
        private static Vector3D PanRelease(Vector3D velocity,Vector3D speed) =>
            MovingRelease(velocity*PanReleaseGain,speed,PanReleaseGain,.04,.02,.8);
        private Vector3D RotationRelease(Vector3D velocity,MatrixD newLeft,MatrixD newRight,double seconds)
        {
            if(seconds<=0 || Vector3D.Distance(rawLeft.Translation,rawRight.Translation)<.08 ||
                Vector3D.Distance(newLeft.Translation,newRight.Translation)<.08) return Vector3D.Zero;
            var speed=RotationVector(PairRotation(rawLeft,rawRight,newLeft,newRight))/seconds;
            return MovingRelease(velocity*RotationReleaseGain*RotationGlide,speed,RotationGain*RotationSensitivity*RotationReleaseGain*RotationGlide,.08,.02,.35);
        }
        private static Vector3D MovingRelease(Vector3D velocity,Vector3D speed,double gain,double rawMinimum,double filteredMinimum,double maximum)
        {
            double length=speed.Length(),filtered=velocity.Length();
            return length>rawMinimum && filtered>filteredMinimum && Vector3D.Dot(speed,velocity)>0 ?
                velocity/filtered*Math.Min(maximum,Math.Min(filtered,length*gain))*MathHelper.Clamp((length-rawMinimum)/rawMinimum,0,1) : Vector3D.Zero;
        }
        private double ZoomRelease(double velocity,MatrixD newLeft,MatrixD newRight,double seconds)
        {
            if(seconds<=0) return 0;
            velocity*=ZoomReleaseGain;
            double before=Vector3D.Distance(rawLeft.Translation,rawRight.Translation);
            double after=Vector3D.Distance(newLeft.Translation,newRight.Translation);
            if(before>=.08 && after>=.08)
            {
                double rate=Math.Log(after/before)/seconds;
                if(Math.Abs(rate)>.08 && Math.Abs(velocity)>.04 && rate*velocity>0)
                    return Math.Sign(rate)*Math.Min(1,Math.Min(Math.Abs(velocity),Math.Abs(rate)*ZoomReleaseGain))*MathHelper.Clamp((Math.Abs(rate)-.08)/.08,0,1);
            }
            return 0;
        }
        private bool Coast(double seconds)
        {
            if(!Coasting || seconds<=0) return false;
            double step=Math.Min(seconds,CoastDuration-coastAge),decay=Math.Exp(-step/CoastTime);
            var pan=coastPan*(CoastTime*(1-decay));
            double rotationStep=Math.Min(step,Math.Max(0,RotationCoastDuration-coastAge));
            double rotationDecay=Math.Exp(-rotationStep/RotationCoastTime);
            var rotation=coastRotation*(RotationCoastTime*(1-rotationDecay));
            double angle=rotation.Length();
            var turn=angle>1e-12 ? MatrixD.CreateFromAxisAngle(rotation/angle,angle):MatrixD.Identity;
            var before=Center;
            Center=Vector3D.TransformNormal(Center-coastPivot,turn)*Scale(coastZoom*CoastTime*(1-decay))+coastPivot+pan;
            Orientation=VrMath.Rigid(MatrixD.Transpose(turn)*Orientation);
            coastPivot+=pan; coastPan*=decay; coastZoom*=decay; coastAge+=step;
            coastRotation=coastAge>=RotationCoastDuration ? Vector3D.Zero:coastRotation*rotationDecay;
            LastTranslation=Vector3D.Distance(Center,before);
            LastRotation=angle;
            if(coastAge>=CoastDuration) Brake();
            return true;
        }
        private static bool Discontinuous(MatrixD before,MatrixD after)
        {
            var a=QuaternionD.CreateFromRotationMatrix(before);
            var b=QuaternionD.CreateFromRotationMatrix(after);
            return Vector3D.Distance(before.Translation,after.Translation)>.35 ||
                Math.Abs(a.X*b.X+a.Y*b.Y+a.Z*b.Z+a.W*b.W)<.707;
        }
        private static MatrixD PairRotation(MatrixD oldLeft,MatrixD oldRight,MatrixD newLeft,MatrixD newRight)
        {
            var before=oldRight.Translation-oldLeft.Translation;
            var after=newRight.Translation-newLeft.Translation;
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
