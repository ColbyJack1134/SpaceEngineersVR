using System;
using VRageMath;
using VRageRender.Animations;

namespace SpaceEngineersVR.Player
{
    // Two-link arm solve, model space, row vectors, character forward -Z; hand roll goes into the forearm, the wrist keeps pitch and side.
    internal static class ArmSolve
    {
        // FixedStep, when set, replaces the wall-clock step. Seconds is the step of the latest Correct call.
        internal sealed class State
        {
            public float Correction,Seconds; public long Time; public Vector3 Elbow; public bool HasElbow;
            public float FixedStep { get; set; }
        }
        internal struct Options
        {
            public bool Travel,Stretch;
            public float Screen;
            public Options(bool travel,bool stretch,float screen=0) { Travel=travel; Stretch=stretch; Screen=screen; }
            // Local view keeps the hand on the controller; other players see model lengths and a hand that stops at full reach.
            public static readonly Options Local=new Options(true,true),Reach=new Options(true,false),Fixed=new Options(false,false);
        }
        // Bone-local unit vectors from the bind pose, where the palm faces the body at zero pronation, so its normal is the hinge axis.
        internal struct Rig
        {
            public Vector3 UpperAxis,UpperHinge,LowerAxis,LowerHinge,PalmLong,PalmSide,PalmNormal;
            public float Side;
        }
        internal const float MaxStretch=1.35f,ShoulderTravel=.12f;
        internal const float PitchLimit=70,RadialLimit=20,UlnarLimit=35,RollLimit=85;
        internal const float CorrectionRange=70,CorrectionStep=10;
        // While the wrist screen is in use the collarbone slides, then turns, toward the elbow that puts the forearm on the hand's line.
        internal const float ScreenShift=.15f,ScreenShrug=30,ScreenFreeFrom=1,ScreenFreeTo=1.1f;
        // Easing of the screen-mode elbow, seconds; the hand itself is never delayed.
        internal const float ScreenElbowEase=.08f;
        // Elbow bone, then each twist bone from the elbow toward the palm. The glove bones carry the hand's roll in full.
        internal static readonly float[] TwistShares={ .4f,1,1 };

        internal static Rig MeasureRig(MyCharacterBone upper,MyCharacterBone lower,MyCharacterBone palm,MyCharacterBone thumb,MyCharacterBone little,float side)
        {
            Matrix u=upper.GetAbsoluteRigTransform(),l=lower.GetAbsoluteRigTransform(),p=palm.GetAbsoluteRigTransform();
            Vector3 longAxis=Vector3.Normalize(p.Translation-l.Translation);
            Vector3 sideAxis=thumb!=null && little!=null ? thumb.GetAbsoluteRigTransform().Translation-little.GetAbsoluteRigTransform().Translation : p.Backward*-side;
            sideAxis-=longAxis*Vector3.Dot(sideAxis,longAxis);
            if(sideAxis.LengthSquared()<1e-6f) sideAxis=Vector3.Cross(longAxis,Vector3.Up);
            sideAxis.Normalize();
            // The cross product does not mirror; flip it on the right so the normal leaves the palm toward the body on both arms.
            Vector3 normal=Vector3.Cross(longAxis,sideAxis)*-side;
            Matrix ui=Matrix.Transpose(u.GetOrientation()),li=Matrix.Transpose(l.GetOrientation()),pi=Matrix.Transpose(p.GetOrientation());
            return new Rig {
                Side=side,
                UpperAxis=Vector3.Normalize(Vector3.TransformNormal(l.Translation-u.Translation,ui)),UpperHinge=Vector3.TransformNormal(normal,ui),
                LowerAxis=Vector3.Normalize(Vector3.TransformNormal(p.Translation-l.Translation,li)),LowerHinge=Vector3.TransformNormal(normal,li),
                PalmLong=Vector3.TransformNormal(longAxis,pi),PalmSide=Vector3.TransformNormal(sideAxis,pi),PalmNormal=Vector3.TransformNormal(normal,pi) };
        }

        // chain: the twist bones between elbow and palm, ordered from the elbow toward the palm.
        internal static bool Apply(MyCharacterBone upper,MyCharacterBone lower,MyCharacterBone[] chain,MyCharacterBone palm,Rig rig,
            Matrix target,Matrix palmOffset,Options options,float bodyScale,State state)
        {
            if(!target.IsValid()) return false;
            // Vanilla weapon IK may have translated the wrist; measure the arm from its bind lengths.
            lower.Translation=lower.BindTransform.Translation*(bodyScale-1); palm.Translation=Vector3.Zero; palm.Rotation=Quaternion.Identity;
            foreach(var bone in chain) { bone.Rotation=Quaternion.Identity; bone.Translation=Vector3.Zero; }
            upper.ComputeAbsoluteTransform(true,true);
            float upperLength=Vector3.Distance(upper.AbsoluteTransform.Translation,lower.AbsoluteTransform.Translation);
            float lowerLength=Vector3.Distance(lower.AbsoluteTransform.Translation,palm.AbsoluteTransform.Translation);
            if(upperLength<.02f || lowerLength<.02f) return false;
            Matrix palmPose=palmOffset*target.GetOrientation();
            Vector3 wrist=target.Translation;
            float screen=MathHelper.Clamp(options.Screen,0,1);
            Vector3 gloveAxis=Vector3.Normalize(Vector3.TransformNormal(rig.PalmLong,palmPose));
            if(screen>0 && upper.Parent!=null) ScreenShoulder(upper.Parent,upper,wrist-gloveAxis*lowerLength,upperLength,screen,rig.Side);
            Vector3 shoulder=upper.AbsoluteTransform.Translation;
            Vector3 delta=wrist-shoulder;
            float distance=delta.Length();
            if(float.IsNaN(distance) || distance<1e-4f || distance>2.5f) return false;
            Vector3 axis=delta/distance;
            float reach=upperLength+lowerLength;
            if(options.Travel)
            {
                float travel=Math.Min(ShoulderTravel,Math.Max(0,distance+.01f-reach)*.6f);
                shoulder+=axis*travel; distance-=travel;
            }
            float stretch=1;
            if(options.Stretch)
            {
                stretch=MathHelper.Clamp((distance+.01f)/reach,1,MaxStretch);
                upperLength*=stretch; lowerLength*=stretch; reach*=stretch;
            }
            float span=MathHelper.Clamp(distance,Math.Abs(upperLength-lowerLength)+.005f,reach-.005f);
            Vector3 solvedWrist=shoulder+axis*span;
            Vector3 hint=Hint(shoulder,wrist,reach,rig.Side,axis);
            float along=(upperLength*upperLength-lowerLength*lowerLength+span*span)/(2*span);
            float height=(float)Math.Sqrt(Math.Max(0,upperLength*upperLength-along*along));
            float correction=Correct(hint,axis,shoulder,solvedWrist,along,height,palmPose,rig,options.Screen,state);
            Vector3 bend=Turn(hint,axis,correction);
            Vector3 elbow=shoulder+axis*along+bend*height;
            Vector3 end=solvedWrist,hand=options.Stretch ? wrist : solvedWrist;
            // Screen in use: the elbow goes on the line back from the glove, so the forearm carries the screen square to the hand.
            if(screen>0)
            {
                Vector3 toWrist=wrist-shoulder;
                Vector3 toIdeal=toWrist-gloveAxis*lowerLength;
                Vector3 screenElbow,screenWrist;
                if(toIdeal.Length()<=upperLength)
                {
                    // Distance back along the glove line that puts the elbow at the upper arm's length, nearest the forearm's own.
                    float g=Vector3.Dot(toWrist,gloveAxis),root=g*g-toWrist.LengthSquared()+upperLength*upperLength,back=-1;
                    if(root>=0)
                    {
                        float r=(float)Math.Sqrt(root),near=Math.Abs(g+r-lowerLength)<Math.Abs(g-r-lowerLength) ? g+r : g-r;
                        if(near>lowerLength*.75f && near<lowerLength*1.25f) back=near;
                    }
                    if(back>0) screenElbow=wrist-gloveAxis*back;
                    else
                    {
                        Vector3 toward=toIdeal-axis*Vector3.Dot(toIdeal,axis);
                        screenElbow=shoulder+axis*along+(toward.LengthSquared()<1e-6f ? bend : Vector3.Normalize(toward))*height;
                    }
                    screenWrist=hand;
                }
                else if(!options.Stretch)
                {
                    // Without stretch the hand and screen stop short.
                    screenElbow=shoulder+Vector3.Normalize(toIdeal)*upperLength;
                    screenWrist=screenElbow+gloveAxis*lowerLength;
                }
                else
                {
                    // A forearm far off the shoulder-to-hand line would need a huge stretch; toward ScreenFreeTo the elbow returns to the free solve.
                    float even=EvenStretch(toWrist,gloveAxis,upperLength,lowerLength);
                    float free=even>0 ? MathHelper.Clamp((even*stretch-ScreenFreeFrom)/(ScreenFreeTo-ScreenFreeFrom),0,1) : 1;
                    screenElbow=free>=1 ? elbow : shoulder+Vector3.Normalize(toWrist-gloveAxis*lowerLength*even)*upperLength*even;
                    if(free>0 && free<1) screenElbow=Vector3.Lerp(screenElbow,elbow,free);
                    screenWrist=hand;
                }
                elbow=Vector3.Lerp(elbow,screenElbow,screen);
                end=Vector3.Lerp(end,screenWrist,screen); hand=Vector3.Lerp(hand,screenWrist,screen);
                Vector3 toEnd=end-shoulder;
                if(toEnd.LengthSquared()>1e-8f) axis=Vector3.Normalize(toEnd);
                Vector3 bent=(elbow-shoulder)-axis*Vector3.Dot(elbow-shoulder,axis);
                if(bent.LengthSquared()>1e-6f) bend=Vector3.Normalize(bent);
            }
            if(state!=null && screen>0)
            {
                // Small pose and head changes move the screen-mode elbow between solutions; easing it keeps the arm and screen steady.
                Vector3 offset=elbow-shoulder;
                if(state.HasElbow) offset=Vector3.Lerp(state.Elbow,offset,1-(float)Math.Exp(-state.Seconds/(ScreenElbowEase*screen)));
                state.Elbow=offset; state.HasElbow=true; elbow=shoulder+offset;
            }
            else if(state!=null) state.HasElbow=false;
            Vector3 forearm=Vector3.Normalize(end-elbow);
            Vector3 hinge=Hinge(bend,axis,rig.Side);
            Matrix upperPose=Frame(rig.UpperAxis,rig.UpperHinge,elbow-shoulder,hinge); upperPose.Translation=shoulder;
            upper.SetCompleteTransformFromAbsoluteMatrix(ref upperPose,false); upper.ComputeAbsoluteTransform(true,true);
            Matrix neutral=Frame(rig.LowerAxis,rig.LowerHinge,forearm,hinge);
            float roll=MathHelper.ToRadians(Roll(forearm,hinge,palmPose,rig));
            Matrix lowerRig=lower.GetAbsoluteRigTransform();
            Matrix lowerPose=neutral*Matrix.CreateFromAxisAngle(forearm,roll*TwistShares[0]); lowerPose.Translation=elbow;
            lower.SetCompleteTransformFromAbsoluteMatrix(ref lowerPose,false); lower.ComputeAbsoluteTransform(true,true);
            palmPose.Translation=hand;
            Matrix lowerRigInverse=Matrix.Invert(lowerRig);
            // The forearm is one piece: a changed length is spread evenly over its twist bones instead of opening a gap at the glove.
            float spread=Vector3.Distance(end,elbow)/Math.Max(1e-4f,Vector3.Distance(palm.GetAbsoluteRigTransform().Translation,lowerRig.Translation));
            for(int i=0;i<chain.Length;i++)
            {
                float share=i+1<TwistShares.Length ? TwistShares[i+1] : 1;
                Matrix chainRig=chain[i].GetAbsoluteRigTransform();
                Matrix pose=(chainRig*lowerRigInverse).GetOrientation()*neutral*Matrix.CreateFromAxisAngle(forearm,roll*share);
                pose.Translation=elbow+forearm*Vector3.Distance(chainRig.Translation,lowerRig.Translation)*spread;
                chain[i].SetCompleteTransformFromAbsoluteMatrix(ref pose,false); chain[i].ComputeAbsoluteTransform(true,true);
            }
            palm.SetCompleteTransformFromAbsoluteMatrix(ref palmPose,false); palm.ComputeAbsoluteTransform(true,true);
            return upper.AbsoluteTransform.IsValid() && lower.AbsoluteTransform.IsValid() && palm.AbsoluteTransform.IsValid();
        }

        // Elbow direction from the hand position: down in front, out when the hand rises or crosses, back when reaching ahead, up behind.
        internal static Vector3 Hint(Vector3 shoulder,Vector3 wrist,float reach,float side,Vector3 axis)
        {
            Vector3 h=(wrist-shoulder)/reach;
            float inward=-side*h.X,up=h.Y,forward=-h.Z;
            float outward=.35f+.6f*MathHelper.Clamp(up+.2f,0,1)+.8f*MathHelper.Clamp(inward+.1f,0,1);
            float back=.3f+.6f*MathHelper.Clamp(forward-.3f,0,1);
            float raise=.8f*MathHelper.Clamp(-forward,0,1);
            Vector3 hint=new Vector3(side*outward,raise-1,back);
            hint-=axis*Vector3.Dot(hint,axis);
            if(hint.LengthSquared()<1e-4f)
            {
                hint=Vector3.Cross(axis,Vector3.Backward);
                if(hint.LengthSquared()<1e-4f) hint=Vector3.Cross(axis,Vector3.Up);
            }
            return Vector3.Normalize(hint);
        }
        // Smallest stretch s>1 with |reach-glove*lower*s|=upper*s; 0 when the forearm points too far back for an even stretch.
        private static float EvenStretch(Vector3 reach,Vector3 glove,float upper,float lower)
        {
            float a=lower*lower-upper*upper,b=-2*lower*Vector3.Dot(reach,glove),c=reach.LengthSquared();
            if(Math.Abs(a)<1e-6f) return b<0 ? Math.Max(1,-c/b) : 0;
            float d=b*b-4*a*c;
            if(d<0) return 0;
            float r=(float)Math.Sqrt(d),s1=(-b-r)/(2*a),s2=(-b+r)/(2*a);
            float low=Math.Min(s1,s2),high=Math.Max(s1,s2);
            return low>=1 ? low : high>=1 ? high : 0;
        }
        private static void ScreenShoulder(MyCharacterBone collar,MyCharacterBone upper,Vector3 elbow,float upperLength,float screen,float side)
        {
            Vector3 need=elbow-upper.AbsoluteTransform.Translation;
            float over=need.Length()-upperLength;
            if(over<=0) return;
            Matrix pose=collar.AbsoluteTransform;
            Vector3 from=upper.AbsoluteTransform.Translation-pose.Translation;
            Vector3 shift=need/need.Length()*Math.Min(ScreenShift,over)*screen;
            // Never toward the middle of the chest: an arm reaching inward would pull the shoulder across the body.
            if(shift.X*side<0) shift.X=0;
            pose.Translation+=shift;
            Vector3 root=pose.Translation,to=elbow-root;
            Vector3 turnAxis=Vector3.Cross(from,to);
            float angle=0;
            if(turnAxis.LengthSquared()>1e-10f)
            {
                turnAxis.Normalize();
                // Smallest turn that brings the elbow within reach; the distance falls monotonically up to the full angle.
                bool reaches(float turn) => Vector3.Distance(root+Vector3.TransformNormal(from,Matrix.CreateFromAxisAngle(turnAxis,turn)),elbow)<=upperLength;
                float low=0,high=Math.Min(MathHelper.ToRadians(ScreenShrug),(float)Math.Acos(MathHelper.Clamp(Vector3.Dot(Vector3.Normalize(from),Vector3.Normalize(to)),-1,1)));
                if(reaches(high))
                    for(int i=0;i<12;i++) { float mid=(low+high)/2; if(reaches(mid)) high=mid; else low=mid; }
                angle=high*screen;
            }
            Matrix turned=angle>0 ? pose.GetOrientation()*Matrix.CreateFromAxisAngle(turnAxis,angle) : pose.GetOrientation();
            turned.Translation=root;
            collar.SetCompleteTransformFromAbsoluteMatrix(ref turned,false); collar.ComputeAbsoluteTransform(true,true);
        }
        internal static Vector3 Turn(Vector3 hint,Vector3 axis,float degrees) =>
            Math.Abs(degrees)<1e-4f ? hint : Vector3.Normalize(Vector3.TransformNormal(hint,Matrix.CreateFromAxisAngle(axis,MathHelper.ToRadians(degrees))));
        // The bend plane normal points laterally for the right arm and medially for the left; the hinge is kept medial.
        internal static Vector3 Hinge(Vector3 bend,Vector3 axis,float side) => Vector3.Normalize(Vector3.Cross(bend,axis))*-side;

        // Rotates the elbow about the shoulder-wrist axis when the hand rotation would overrun the wrist or forearm limits.
        private static float Correct(Vector3 hint,Vector3 axis,Vector3 shoulder,Vector3 wrist,float along,float height,Matrix palmPose,Rig rig,float screen,State state)
        {
            float best=0,bestCost=float.MaxValue;
            for(float degrees=-CorrectionRange;degrees<=CorrectionRange;degrees+=CorrectionStep)
            {
                Vector3 bend=Turn(hint,axis,degrees);
                Vector3 elbow=shoulder+axis*along+bend*height;
                Vector3 forearm=Vector3.Normalize(wrist-elbow);
                float cost=Violation(forearm,Hinge(bend,axis,rig.Side),palmPose,rig)+Comfort(forearm,palmPose,rig)+.02f*degrees*degrees;
                if(cost<bestCost) { bestCost=cost; best=degrees; }
            }
            if(state==null) return best;
            long now=System.Diagnostics.Stopwatch.GetTimestamp();
            float seconds=state.FixedStep>0 ? state.FixedStep : state.Time==0 ? 1 : (float)Math.Min(.1,(now-state.Time)/(double)System.Diagnostics.Stopwatch.Frequency);
            state.Time=now; state.Seconds=seconds;
            // A used wrist screen needs a steady forearm, so the elbow settles slowly while the screen is in use.
            float smoothing=MathHelper.Lerp(.08f,.5f,MathHelper.Clamp(screen,0,1));
            float step=(best-state.Correction)*(1-(float)Math.Exp(-seconds/smoothing));
            float limit=360*seconds;
            state.Correction+=MathHelper.Clamp(step,-limit,limit);
            return state.Correction;
        }
        // A relaxed wrist bends little; side deviation is the less comfortable of the two.
        internal static float Comfort(Vector3 forearm,Matrix palmPose,Rig rig)
        {
            Bend(forearm,palmPose,rig,out float pitch,out float side);
            return .015f*pitch*pitch+.03f*side*side;
        }
        internal static float Violation(Vector3 forearm,Vector3 hinge,Matrix palmPose,Rig rig)
        {
            Wrist(forearm,hinge,palmPose,rig,out float pitch,out float side,out float roll);
            float sideLimit=side>0 ? UlnarLimit : RadialLimit;
            float p=Math.Max(0,Math.Abs(pitch)-PitchLimit),s=Math.Max(0,Math.Abs(side)-sideLimit),r=Math.Max(0,Math.Abs(roll)-RollLimit);
            return p*p+s*s+r*r;
        }
        // Pitch and side: forearm direction in the palm frame (positive side toward the little finger); roll: the rest, about the forearm from the hinge.
        internal static void Wrist(Vector3 forearm,Vector3 hinge,Matrix palmPose,Rig rig,out float pitch,out float side,out float roll)
        {
            Bend(forearm,palmPose,rig,out pitch,out side);
            roll=Roll(forearm,hinge,palmPose,rig);
        }
        internal static void Bend(Vector3 forearm,Matrix palmPose,Rig rig,out float pitch,out float side)
        {
            float forward=Vector3.Dot(forearm,Vector3.TransformNormal(rig.PalmLong,palmPose));
            pitch=MathHelper.ToDegrees((float)Math.Atan2(Vector3.Dot(forearm,Vector3.TransformNormal(rig.PalmNormal,palmPose)),forward));
            side=MathHelper.ToDegrees((float)Math.Atan2(Vector3.Dot(forearm,Vector3.TransformNormal(rig.PalmSide,palmPose)),forward));
        }
        internal static float Roll(Vector3 forearm,Vector3 hinge,Matrix palmPose,Rig rig)
        {
            Vector3 longAxis=Vector3.TransformNormal(rig.PalmLong,palmPose);
            Matrix unswung=ArmMath.AimBone(palmPose.GetOrientation(),longAxis,forearm);
            Vector3 normal=Vector3.TransformNormal(rig.PalmNormal,unswung);
            normal-=forearm*Vector3.Dot(normal,forearm);
            if(normal.LengthSquared()<1e-6f) return 0;
            normal.Normalize();
            return MathHelper.ToDegrees((float)Math.Atan2(Vector3.Dot(forearm,Vector3.Cross(hinge,normal)),Vector3.Dot(hinge,normal)));
        }
        // Orientation that carries the bone-local pair (axis, hinge) onto the model-space pair.
        internal static Matrix Frame(Vector3 localAxis,Vector3 localHinge,Vector3 axis,Vector3 hinge)
        {
            Matrix local=Basis(localAxis,localHinge),world=Basis(axis,hinge);
            return Matrix.Transpose(local)*world;
        }
        private static Matrix Basis(Vector3 x,Vector3 y)
        {
            x.Normalize();
            y-=x*Vector3.Dot(y,x);
            if(y.LengthSquared()<1e-8f) y=Vector3.Cross(x,Math.Abs(x.Y)<.9f ? Vector3.Up : Vector3.Right);
            y.Normalize();
            Matrix basis=Matrix.Identity;
            basis.Right=x; basis.Up=y; basis.Backward=Vector3.Cross(x,y);
            return basis;
        }
    }
}
