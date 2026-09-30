using System;
using VRageMath;

namespace SpaceEngineersVR.Player
{
    public static class VrMath
    {
        // Let the tracked head move freely inside this radius. Move the capsule only
        // by the excess, so crossing the boundary never consumes a whole head offset.
        public const float RoomscaleBodyRadius = 0.025f;
        public static Vector3 RoomscaleTravel(Vector3 offset)
        {
            offset.Y=0;
            float distance=offset.Length();
            return distance<=RoomscaleBodyRadius ? Vector3.Zero : offset*((distance-RoomscaleBodyRadius)/distance);
        }
        public static MatrixD PhysicsAnchoredBody(MatrixD visual,Vector3D physicsPosition)
        {
            MatrixD body=Rigid(visual);
            body.Translation=physicsPosition;
            return body;
        }
        public static bool HasStepRise(double rise) => rise>0.015; // Exclude the sweep's 5mm landing skin and flat-floor noise.

        public static MatrixD Rigid(MatrixD value)
        {
            return MatrixD.CreateWorld(value.Translation,Vector3D.Normalize(value.Forward),Vector3D.Normalize(value.Up));
        }
        public static MatrixD Level(MatrixD value,Vector3D up)
        {
            up.Normalize();
            Vector3D forward=value.Forward-up*Vector3D.Dot(value.Forward,up);
            if(forward.LengthSquared()<0.0001) forward=Vector3D.Cross(up,value.Right);
            forward.Normalize();
            return MatrixD.CreateWorld(value.Translation,forward,up);
        }
        public static float PlanarYaw(Vector3 forward)
        {
            if (forward.X*forward.X+forward.Z*forward.Z<0.0001f) return 0;
            return (float)Math.Atan2(-forward.X,-forward.Z);
        }
        public static MatrixD RecenterAnchor(MatrixD anchor, Matrix oldOrigin, Matrix newOrigin)
        {
            MatrixD rotation=(MatrixD)newOrigin.GetOrientation()*MatrixD.Transpose((MatrixD)oldOrigin.GetOrientation());
            MatrixD result=Rigid(rotation*anchor.GetOrientation());
            result.Translation=anchor.Translation;
            return result;
        }
        public static MatrixD AdvanceAnchor(MatrixD anchor, MatrixD previousBody, MatrixD currentBody)
        {
            return Rigid(anchor.GetOrientation()*MatrixD.Transpose(previousBody.GetOrientation())*currentBody.GetOrientation());
        }
        public static Matrix Affine(Matrix value)
        {
            // Float matrix inversion can leave M44 at 0.99999994. Multiplying that
            // by a large world translation scales the entire camera position.
            value.M14=value.M24=value.M34=0; value.M44=1;
            return value;
        }
        public static Vector3 DirectedMove(Vector3 move, Vector3 forward)
        {
            return Vector3.TransformNormal(move,Matrix.CreateRotationY(PlanarYaw(forward)));
        }
        public static bool PanelHit(Matrix hand, Matrix panel, float width, float height, out Vector2 uv)
        {
            Matrix local=hand*Matrix.Invert(panel);
            uv=Vector2.Zero;
            if (local.Translation.Z<=0 || local.Forward.Z>=-0.0001f) return false;
            float distance=-local.Translation.Z/local.Forward.Z;
            if (distance>10) return false;
            Vector3 hit=local.Translation+local.Forward*distance;
            uv=new Vector2(hit.X/width+0.5f,0.5f-hit.Y/height);
            return uv.X>=0 && uv.X<=1 && uv.Y>=0 && uv.Y<=1;
        }
        // All tracking translations stay in small, local coordinates. World-space camera math is double precision.
        public static MatrixD EyeView(MatrixD gameView, Matrix trackingHead, Matrix trackingOriginInverse, Matrix eyeToHead,double unitsPerMeter=1)
        {
            MatrixD local=Affine(eyeToHead*trackingHead*trackingOriginInverse);
            local.Translation*=unitsPerMeter;
            return MatrixD.Invert(local*MatrixD.Invert(gameView));
        }

        public static MatrixD Projection(float left, float right, float top, float bottom, double near, double far = 0)
        {
            if (right <= left || bottom <= top || near <= 0 || (far != 0 && far <= near))
                throw new ArgumentException("Invalid eye projection bounds");
            double x = 1.0 / (right - left), y = 1.0 / (bottom - top);
            return new MatrixD(2*x,0,0,0, 0,2*y,0,0,
                (right+left)*x,(bottom+top)*y,far == 0 ? 0 : far/(near-far),-1,
                0,0,far == 0 ? near : near*far/(near-far),0);
        }

        public static Matrix TrackingOrigin(Matrix head)
        {
            Vector3 forward = head.Forward;
            forward.Y = 0;
            if (forward.LengthSquared() < 0.0001f) forward = Vector3.Forward;
            forward.Normalize();
            return Matrix.CreateWorld(head.Translation, forward, Vector3.Up);
        }

        public static float Deadzone(float value, float deadzone = 0.18f)
        {
            return Math.Abs(value) <= deadzone ? 0 : Math.Sign(value) * (Math.Abs(value) - deadzone) / (1 - deadzone);
        }
    }
}
