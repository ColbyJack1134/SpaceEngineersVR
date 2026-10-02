using System;
using VRageMath;
using VRageRender.Animations;

namespace SpaceEngineersVR.Player
{
    internal static class ArmMath
    {
        // Analytic two-link solve. All coordinates are small model-space values.
        // Fixed-length solve used after fitting the tracked arm's reach.
        public static bool Solve(Vector3 shoulder,Vector3 target,float upper,float lower,Vector3 hint,
            out Vector3 elbow,out Vector3 wrist)
        {
            elbow=wrist=shoulder;
            if(!shoulder.IsValid() || !target.IsValid() || !hint.IsValid() || upper<0.02f || lower<0.02f ||
                float.IsNaN(upper) || float.IsNaN(lower) || float.IsInfinity(upper) || float.IsInfinity(lower)) return false;
            Vector3 delta=target-shoulder;
            float requested=delta.Length();
            Vector3 direction=requested>0.0001f ? delta/requested : Vector3.Forward;
            float distance=MathHelper.Clamp(requested,Math.Abs(upper-lower)+0.005f,upper+lower-0.005f);
            Vector3 bend=hint-direction*Vector3.Dot(hint,direction);
            if(bend.LengthSquared()<0.0001f)
            {
                Vector3 fallback=Math.Abs(direction.Y)<0.9f ? Vector3.Down : Vector3.Backward;
                bend=fallback-direction*Vector3.Dot(fallback,direction);
            }
            bend.Normalize();
            float along=(upper*upper-lower*lower+distance*distance)/(2*distance);
            float height=(float)Math.Sqrt(Math.Max(0,upper*upper-along*along));
            elbow=shoulder+direction*along+bend*height;
            wrist=shoulder+direction*distance;
            return elbow.IsValid() && wrist.IsValid();
        }
        public static Matrix AimBone(Matrix bone,Vector3 from,Vector3 to)
        {
            if(from.LengthSquared()<1e-8f || to.LengthSquared()<1e-8f) return bone;
            from.Normalize(); to.Normalize();
            float dot=MathHelper.Clamp(Vector3.Dot(from,to),-1,1);
            Matrix turn;
            if(dot>0.999999f) turn=Matrix.Identity;
            else if(dot< -0.999999f)
            {
                Vector3 axis=Vector3.Cross(from,Math.Abs(from.Y)<0.9f ? Vector3.Up : Vector3.Right);
                axis.Normalize(); turn=Matrix.CreateFromAxisAngle(axis,MathHelper.Pi);
            }
            else
            {
                Vector3 axis=Vector3.Cross(from,to); axis.Normalize();
                turn=Matrix.CreateFromAxisAngle(axis,(float)Math.Acos(dot));
            }
            Matrix result=bone.GetOrientation()*turn;
            result.Translation=bone.Translation;
            return result;
        }
        public static bool ApplyPose(MyCharacterBone upper,MyCharacterBone lower,MyCharacterBone palm,
            Matrix target,Matrix palmOffset,Vector3 hint,bool adaptive=true,bool rigidWrist=false,float bodyScale=1)
        {
            if(!target.IsValid()) return false;
            // Vanilla weapon IK can translate the palm to reach a weapon. Restore
            // its bind-space offset before solving, rather than inheriting a stretched wrist.
            lower.Translation=lower.BindTransform.Translation*(bodyScale-1); palm.Translation=Vector3.Zero;
            for(var bone=palm.Parent;bone!=null && bone!=lower;bone=bone.Parent)
            { bone.Rotation=Quaternion.Identity; bone.Translation=Vector3.Zero; }
            upper.ComputeAbsoluteTransform(true,true);
            Vector3 shoulder=upper.AbsoluteTransform.Translation;
            float upperLength=Vector3.Distance(shoulder,lower.AbsoluteTransform.Translation);
            float lowerLength=Vector3.Distance(lower.AbsoluteTransform.Translation,palm.AbsoluteTransform.Translation);
            Matrix palmPose=palmOffset*target.GetOrientation();
            if(rigidWrist)
            {
                palmPose.Translation=target.Translation;
                Matrix rigidLower=lower.GetAbsoluteRigTransform()*Matrix.Invert(palm.GetAbsoluteRigTransform())*palmPose;
                Vector3 rigidElbow=rigidLower.Translation;
                Vector3 reach=rigidElbow-shoulder;
                float distance=reach.Length();
                if(distance<.001f || distance>2.5f) return false;
                // Keep the forearm straight through the cuff. Fit reach at the shoulder,
                // instead of forcing all wrist deflection into the short Forearm1 segment.
                if(adaptive) shoulder+=reach/distance*Math.Min(.12f,Math.Max(0,distance-upperLength)*.6f);
                Matrix rigidUpper=AimBone(upper.GetAbsoluteRigTransform(),
                    lower.GetAbsoluteRigTransform().Translation-upper.GetAbsoluteRigTransform().Translation,rigidElbow-shoulder);
                Vector3 rigidAxis=Vector3.Normalize(rigidElbow-shoulder);
                Vector3 normal=Vector3.Cross(rigidAxis,Vector3.Normalize(palmPose.Translation-rigidElbow));
                if(normal.LengthSquared()>.001f)
                {
                    Vector3 bindUpper=lower.GetAbsoluteRigTransform().Translation-upper.GetAbsoluteRigTransform().Translation;
                    Vector3 bindLower=palm.GetAbsoluteRigTransform().Translation-lower.GetAbsoluteRigTransform().Translation;
                    float sign=Math.Sign(Vector3.Dot(upper.GetAbsoluteRigTransform().Backward,Vector3.Cross(bindUpper,bindLower)));
                    Vector3 from=rigidUpper.Backward-rigidAxis*Vector3.Dot(rigidUpper.Backward,rigidAxis);
                    if(from.LengthSquared()>.001f && sign!=0)
                    {
                        float bend=normal.Length();
                        from.Normalize(); normal.Normalize(); normal*=sign;
                        float angle=(float)Math.Atan2(Vector3.Dot(rigidAxis,Vector3.Cross(from,normal)),Vector3.Dot(from,normal));
                        // The bend plane loses its direction near a straight arm. Keep the
                        // shoulder's neutral twist dominant so crossing it cannot flip the sleeve.
                        float weight=.4f*MathHelper.SmoothStep(0,1,MathHelper.Clamp(bend/.35f,0,1));
                        angle=(float)Math.Atan2(weight*Math.Sin(angle),1-weight+weight*Math.Cos(angle));
                        rigidUpper=rigidUpper.GetOrientation()*Matrix.CreateFromAxisAngle(rigidAxis,angle);
                    }
                }
                rigidUpper.Translation=shoulder;
                upper.SetCompleteTransformFromAbsoluteMatrix(ref rigidUpper,false); upper.ComputeAbsoluteTransform(true,true);
                var cuff=palm.Parent;
                while(cuff!=null && cuff.Parent!=lower) cuff=cuff.Parent;
                if(cuff!=null)
                {
                    // Forearm2 carries the tablet and strap. Share sleeve roll at Forearm1
                    // while preserving the cuff's complete frame relative to the hand.
                    Matrix cuffPose=cuff.GetAbsoluteRigTransform()*Matrix.Invert(palm.GetAbsoluteRigTransform())*palmPose;
                    Vector3 sleeveAxis=Vector3.Normalize(palmPose.Translation-rigidElbow);
                    Matrix sleeve=lower.GetAbsoluteRigTransform()*Matrix.Invert(upper.GetAbsoluteRigTransform())*rigidUpper;
                    Vector3 bindAxis=palm.GetAbsoluteRigTransform().Translation-lower.GetAbsoluteRigTransform().Translation;
                    Vector3 neutralAxis=Vector3.TransformNormal(bindAxis,
                        Matrix.Transpose(lower.GetAbsoluteRigTransform().GetOrientation())*sleeve.GetOrientation());
                    sleeve=AimBone(sleeve,neutralAxis,sleeveAxis);
                    Matrix twist=ForearmTwist(sleeve,rigidLower,sleeveAxis);
                    sleeve=sleeve.GetOrientation()*Matrix.CreateFromQuaternion(
                        Quaternion.Slerp(Quaternion.Identity,Quaternion.CreateFromRotationMatrix(twist),.5f));
                    sleeve.Translation=rigidElbow;
                    lower.SetCompleteTransformFromAbsoluteMatrix(ref sleeve,false); lower.ComputeAbsoluteTransform(true,true);
                    cuff.SetCompleteTransformFromAbsoluteMatrix(ref cuffPose,false); cuff.ComputeAbsoluteTransform(true,true);
                }
                else
                {
                    lower.SetCompleteTransformFromAbsoluteMatrix(ref rigidLower,false); lower.ComputeAbsoluteTransform(true,true);
                }
                palm.SetCompleteTransformFromAbsoluteMatrix(ref palmPose,false); palm.ComputeAbsoluteTransform(true,true);
                return upper.AbsoluteTransform.IsValid() && lower.AbsoluteTransform.IsValid() && palm.AbsoluteTransform.IsValid();
            }
            if(adaptive)
            {
                var rig=palm.GetAbsoluteRigTransform();
                Vector3 localAxis=Vector3.TransformNormal(rig.Translation-lower.GetAbsoluteRigTransform().Translation,Matrix.Transpose(rig.GetOrientation()));
                localAxis.Normalize();
                Vector3 forearm=Vector3.TransformNormal(localAxis,palmPose);
                hint=WristHint(shoulder,target.Translation,forearm,hint,out float reserve);
                if(!FitReach(ref shoulder,target.Translation,ref upperLength,ref lowerLength,reserve)) return false;
            }
            if(!Solve(shoulder,target.Translation,upperLength,lowerLength,hint,out Vector3 elbow,out Vector3 wrist)) return false;
            Matrix upperPose=AimBone(upper.AbsoluteTransform,lower.AbsoluteTransform.Translation-upper.AbsoluteTransform.Translation,elbow-shoulder);
            if(adaptive) upperPose.Translation=shoulder;
            upper.SetCompleteTransformFromAbsoluteMatrix(ref upperPose,!adaptive); upper.ComputeAbsoluteTransform();
            Matrix lowerPose=AimBone(lower.AbsoluteTransform,palm.AbsoluteTransform.Translation-lower.AbsoluteTransform.Translation,wrist-(adaptive ? elbow : lower.AbsoluteTransform.Translation));
            if(adaptive) lowerPose.Translation=elbow;
            lower.SetCompleteTransformFromAbsoluteMatrix(ref lowerPose,!adaptive); lower.ComputeAbsoluteTransform();
            // Pronation belongs to the forearm too. Without it the glove's display
            // ignores wrist roll while only the palm spins at the end of the arm.
            Vector3 axis=palm.AbsoluteTransform.Translation-lower.AbsoluteTransform.Translation;
            if(axis.LengthSquared()>1e-8f)
            {
                axis.Normalize();
                Matrix twist=ForearmTwist(palm.AbsoluteTransform,palmPose,axis);
                int count=0;
                for(var bone=palm.Parent;bone!=null && bone!=lower;bone=bone.Parent) count++;
                Quaternion rotation=Quaternion.CreateFromRotationMatrix(twist);
                Matrix partial=count==0 ? twist : Matrix.CreateFromQuaternion(Quaternion.Slerp(Quaternion.Identity,rotation,.5f));
                lowerPose=lower.AbsoluteTransform.GetOrientation()*partial;
                lowerPose.Translation=lower.AbsoluteTransform.Translation;
                lower.SetCompleteTransformFromAbsoluteMatrix(ref lowerPose,true); lower.ComputeAbsoluteTransform();
                if(count>0)
                {
                    var chain=new MyCharacterBone[count];
                    var bone=palm.Parent;
                    for(int i=count-1;i>=0;i--,bone=bone.Parent) chain[i]=bone;
                    partial=Matrix.CreateFromQuaternion(Quaternion.Slerp(Quaternion.Identity,rotation,.5f/count));
                    foreach(var segment in chain)
                    {
                        Matrix pose=segment.AbsoluteTransform.GetOrientation()*partial;
                        pose.Translation=segment.AbsoluteTransform.Translation;
                        segment.SetCompleteTransformFromAbsoluteMatrix(ref pose,true); segment.ComputeAbsoluteTransform();
                    }
                }
            }
            palmPose.Translation=adaptive ? target.Translation : palm.AbsoluteTransform.Translation;
            palm.SetCompleteTransformFromAbsoluteMatrix(ref palmPose,!adaptive); palm.ComputeAbsoluteTransform();
            return upper.AbsoluteTransform.IsValid() && lower.AbsoluteTransform.IsValid() && palm.AbsoluteTransform.IsValid();
        }
        internal static Vector3 WristHint(Vector3 shoulder,Vector3 target,Vector3 forearm,Vector3 bodyHint,out float reserve)
        {
            Vector3 direction=target-shoulder;
            if(direction.LengthSquared()<1e-6f) { reserve=.025f; return bodyHint; }
            direction.Normalize();
            float alignment=MathHelper.Clamp(Vector3.Dot(direction,forearm),-1,1);
            // Preserve a useful elbow bend when the palm turns across the reach,
            // instead of putting the entire change of direction into the wrist.
            reserve=.025f+.09f*(1-Math.Max(0,alignment));
            Vector3 wrist=-forearm+direction*alignment;
            Vector3 body=bodyHint-direction*Vector3.Dot(bodyHint,direction);
            if(body.LengthSquared()<1e-6f)
            {
                Vector3 fallback=Math.Abs(direction.Y)<.9f ? Vector3.Down : Vector3.Backward;
                body=fallback-direction*Vector3.Dot(fallback,direction);
            }
            body.Normalize();
            // Keep the elbow on the anatomical side of the reach. Opposing
            // wrist/body hints must not cancel and flip the bend plane.
            float opposed=Vector3.Dot(wrist,body);
            if(opposed<0) wrist-=body*opposed;
            return wrist+body*.35f;
        }
        internal static bool FitReach(ref Vector3 shoulder,Vector3 target,ref float upper,ref float lower,float reserve=.015f)
        {
            if(!shoulder.IsValid() || !target.IsValid() || upper<.02f || lower<.02f) return false;
            Vector3 delta=target-shoulder;
            float distance=delta.Length();
            if(float.IsNaN(distance) || distance>2.5f) return false;
            float extension=Math.Max(0,distance+reserve-(upper+lower));
            if(distance>.0001f)
            {
                float shoulderTravel=Math.Min(.12f,extension*.6f);
                shoulder+=delta*(shoulderTravel/distance);
                distance-=shoulderTravel;
            }
            float stretch=MathHelper.Clamp((distance+reserve)/(upper+lower),1,1.35f);
            upper*=stretch; lower*=stretch;
            // Folding near the shoulder needs similar link lengths. At exceptional
            // reach, keep the hand accurate even after the mesh extension limit.
            if(distance<Math.Abs(upper-lower)+.01f)
                upper=lower=(upper+lower)/2;
            return true;
        }
        internal static Matrix ForearmTwist(Matrix currentPalm,Matrix desiredPalm,Vector3 axis)
        {
            Vector3 from=currentPalm.Up-axis*Vector3.Dot(currentPalm.Up,axis);
            Vector3 to=desiredPalm.Up-axis*Vector3.Dot(desiredPalm.Up,axis);
            if(from.LengthSquared()<.001f || to.LengthSquared()<.001f)
            {
                from=currentPalm.Right-axis*Vector3.Dot(currentPalm.Right,axis);
                to=desiredPalm.Right-axis*Vector3.Dot(desiredPalm.Right,axis);
            }
            if(from.LengthSquared()<1e-6f || to.LengthSquared()<1e-6f) return Matrix.Identity;
            from.Normalize(); to.Normalize();
            float angle=(float)Math.Atan2(Vector3.Dot(axis,Vector3.Cross(from,to)),Vector3.Dot(from,to));
            return Matrix.CreateFromAxisAngle(axis,angle);
        }
        public static Matrix PalmCorrection(Matrix palmRig,Matrix forearmRig,float side)
        {
            Vector3 forward=palmRig.Translation-forearmRig.Translation;
            forward.Normalize();
            Vector3 up=Math.Abs(Vector3.Dot(forward,Vector3.Up))<0.95f ? Vector3.Up : Vector3.Backward;
            Matrix grip=Matrix.CreateWorld(Vector3.Zero,forward,up);
            // Correct the suit palm axes in grip space: left +90 degrees, right -90.
            // Inward-pointing thumbs then point up with an upright controller.
            // Pitch the corrected palms down 55 degrees in grip space; keep the
            // driver tip ray unchanged while calibrating the visible hand pose.
            return palmRig.GetOrientation()*Matrix.Transpose(grip.GetOrientation())*Matrix.CreateRotationZ(-side*MathHelper.PiOver2)*Matrix.CreateRotationX(MathHelper.ToRadians(-55f));
        }
    }
}
