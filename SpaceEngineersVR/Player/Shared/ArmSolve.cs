using System;
using VRageMath;
using VRageRender.Animations;

namespace SpaceEngineersVR.Player
{
    // Two-link arm solve, model space, row vectors, character forward -Z; hand roll goes into the forearm, the wrist keeps pitch and side.
    internal static class ArmSolve
    {
        // FixedStep, when set, replaces the wall-clock step.
        internal sealed class State { public float Correction; public long Time; public float FixedStep { get; set; } }
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
        // Upper arm stretch allowed to reach the straight-sleeve elbow while the wrist screen is in use.
        internal const float ScreenStretch=1.6f;
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
            Vector3 shoulder=upper.AbsoluteTransform.Translation;
            float upperLength=Vector3.Distance(shoulder,lower.AbsoluteTransform.Translation);
            float lowerLength=Vector3.Distance(lower.AbsoluteTransform.Translation,palm.AbsoluteTransform.Translation);
            if(upperLength<.02f || lowerLength<.02f) return false;
            Matrix palmPose=palmOffset*target.GetOrientation();
            Vector3 wrist=target.Translation;
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
            if(options.Stretch)
            {
                float stretch=MathHelper.Clamp((distance+.01f)/reach,1,MaxStretch);
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
            // Screen in use: the glove is fixed to the hand and the elbow moves toward the straight-sleeve point, upper arm up to ScreenStretch.
            float screen=MathHelper.Clamp(options.Screen,0,1);
            if(screen>0)
            {
                Vector3 gloveAxis=Vector3.Normalize(Vector3.TransformNormal(rig.PalmLong,palmPose));
                Vector3 ideal=solvedWrist-gloveAxis*lowerLength;
                Vector3 toIdeal=ideal-shoulder;
                float needed=toIdeal.Length();
                float arm=MathHelper.Clamp(needed,upperLength,upperLength*(options.Stretch ? ScreenStretch : 1));
                if(Math.Abs(needed-arm)>1e-4f)
                {
                    float reachable=MathHelper.Clamp(span,Math.Abs(arm-lowerLength)+.005f,arm+lowerLength-.005f);
                    float armAlong=(arm*arm-lowerLength*lowerLength+reachable*reachable)/(2*reachable);
                    float armHeight=(float)Math.Sqrt(Math.Max(0,arm*arm-armAlong*armAlong));
                    Vector3 toward=toIdeal-axis*Vector3.Dot(toIdeal,axis);
                    toward=toward.LengthSquared()<1e-6f ? hint : Vector3.Normalize(toward);
                    ideal=shoulder+axis*armAlong+toward*armHeight;
                }
                elbow=Vector3.Lerp(elbow,ideal,screen);
                // Keep the forearm length; fall back to the arm circle if that shortens or overstretches the upper arm.
                elbow=solvedWrist-Vector3.Normalize(solvedWrist-elbow)*lowerLength;
                Vector3 fromShoulder=elbow-shoulder;
                float longest=upperLength*(options.Stretch ? ScreenStretch : 1);
                if(fromShoulder.LengthSquared()<upperLength*upperLength || fromShoulder.LengthSquared()>longest*longest)
                {
                    float limit=fromShoulder.LengthSquared()<upperLength*upperLength ? upperLength : longest;
                    float reachable=MathHelper.Clamp(span,Math.Abs(limit-lowerLength)+.005f,limit+lowerLength-.005f);
                    float limitAlong=(limit*limit-lowerLength*lowerLength+reachable*reachable)/(2*reachable);
                    float limitHeight=(float)Math.Sqrt(Math.Max(0,limit*limit-limitAlong*limitAlong));
                    Vector3 toward=fromShoulder-axis*Vector3.Dot(fromShoulder,axis);
                    toward=toward.LengthSquared()<1e-6f ? hint : Vector3.Normalize(toward);
                    elbow=shoulder+axis*limitAlong+toward*limitHeight;
                }
                bend=(elbow-shoulder)-axis*Vector3.Dot(elbow-shoulder,axis);
                bend=bend.LengthSquared()<1e-4f ? hint : Vector3.Normalize(bend);
            }
            Vector3 forearm=Vector3.Normalize(solvedWrist-elbow);
            Vector3 hinge=Hinge(bend,axis,rig.Side);
            Matrix upperPose=Frame(rig.UpperAxis,rig.UpperHinge,elbow-shoulder,hinge); upperPose.Translation=shoulder;
            upper.SetCompleteTransformFromAbsoluteMatrix(ref upperPose,false); upper.ComputeAbsoluteTransform(true,true);
            Matrix neutral=Frame(rig.LowerAxis,rig.LowerHinge,forearm,hinge);
            float roll=MathHelper.ToRadians(Roll(forearm,hinge,palmPose,rig));
            Matrix lowerRig=lower.GetAbsoluteRigTransform();
            Matrix lowerPose=neutral*Matrix.CreateFromAxisAngle(forearm,roll*TwistShares[0]); lowerPose.Translation=elbow;
            lower.SetCompleteTransformFromAbsoluteMatrix(ref lowerPose,false); lower.ComputeAbsoluteTransform(true,true);
            palmPose.Translation=options.Stretch ? wrist : solvedWrist;
            Matrix lowerRigInverse=Matrix.Invert(lowerRig),palmRigInverse=Matrix.Invert(palm.GetAbsoluteRigTransform());
            for(int i=0;i<chain.Length;i++)
            {
                float share=i+1<TwistShares.Length ? TwistShares[i+1] : 1;
                Matrix chainRig=chain[i].GetAbsoluteRigTransform();
                Matrix pose=(chainRig*lowerRigInverse).GetOrientation()*neutral*Matrix.CreateFromAxisAngle(forearm,roll*share);
                pose.Translation=chain[i].AbsoluteTransform.Translation;
                if(screen>0)
                {
                    Matrix fixedToHand=chainRig*palmRigInverse*palmPose;
                    if(screen>=1) pose=fixedToHand;
                    else
                    {
                        Vector3 position=Vector3.Lerp(pose.Translation,fixedToHand.Translation,screen);
                        pose=Matrix.CreateFromQuaternion(Quaternion.Slerp(Quaternion.CreateFromRotationMatrix(pose),Quaternion.CreateFromRotationMatrix(fixedToHand),screen));
                        pose.Translation=position;
                    }
                }
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
            state.Time=now;
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
