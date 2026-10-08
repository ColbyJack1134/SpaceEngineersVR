using System;
using VRageMath;

namespace SpaceEngineersVR.Player
{
    internal struct MarkerBillboard
    {
        public const double IconAngle = Math.PI / 90; // Two degrees, independent of eye target resolution.
        public Vector3D Center, Right, Up;
        public double Scale;

        internal static MatrixD WithUp(MatrixD head,Vector3D up)
        {
            up-=head.Forward*Vector3D.Dot(up,head.Forward);
            if(up.LengthSquared()<1e-8) return head;
            head.Up=Vector3D.Normalize(up);
            head.Right=Vector3D.Normalize(Vector3D.Cross(head.Forward,head.Up));
            return head;
        }

        internal static bool TryCreate(Vector3D position,MatrixD head,Vector3D? up,bool faceViewer,out MarkerBillboard billboard)
        {
            if(!TryCreate(position,up.HasValue ? WithUp(head,up.Value):head,out billboard)) return false;
            if(faceViewer) billboard.FaceViewer(head.Translation,up,head.Right);
            return true;
        }
        public static bool TryCreate(Vector3D position, MatrixD head, out MarkerBillboard billboard)
        {
            billboard=default(MarkerBillboard);
            Vector3D towardHead=head.Translation-position;
            double distance=towardHead.Length();
            if (!position.IsValid() || !head.IsValid() || !distance.IsValid() || distance<0.1) return false;
            billboard=new MarkerBillboard { Center=position,Right=Vector3D.Normalize(head.Right),Up=Vector3D.Normalize(head.Up),
                Scale=2*distance*Math.Tan(IconAngle/2) };
            return true;
        }

        public Vector3D Point(double x, double y) => Center+Scale*(Right*x-Up*y);

        internal void FaceViewer(Vector3D eye,Vector3D? referenceUp=null,Vector3D? poleRight=null)
        {
            var normal=eye-Center;
            if(!normal.IsValid() || normal.LengthSquared()<.01) return;
            normal.Normalize();
            var right=Vector3D.Cross(referenceUp ?? Up,normal);
            // Near the up axis, the shared head basis avoids an unstable roll flip.
            if(poleRight.HasValue && right.LengthSquared()<1e-4)
                right=poleRight.Value-normal*Vector3D.Dot(poleRight.Value,normal);
            if(right.LengthSquared()<1e-8) return;
            Right=Vector3D.Normalize(right);
            Up=Vector3D.Normalize(Vector3D.Cross(normal,Right));
        }

        internal static bool TryCreatePixels(Vector3D position,double width,MatrixD view,MatrixD projection,int viewportWidth,out MarkerBillboard board,Vector3D? up=null,bool faceViewer=false)
        {
            var eye=StereoRenderState.PhysicalEye ? WorldMarkers.RenderHead : MatrixD.Invert(view);
            if(up.HasValue) eye=WithUp(eye,up.Value);
            if(!TryCreate(position,eye,out board)) return false;
            if(faceViewer) board.FaceViewer(eye.Translation,up,eye.Right);
            double depth=-Vector3D.Transform(position,MatrixD.Invert(eye)).Z;
            if(depth<=.05) return false;
            double slope=StereoRenderState.PhysicalEye ? StereoRenderState.PixelSlopeX : 1/projection.M11;
            board.Scale=2*depth*width*slope/viewportWidth;
            return true;
        }

        public bool Project(RectangleF bounds, MatrixD view, MatrixD projection, ref NativeSprite sprite)
        {
            if (!Clip(Point(bounds.X,bounds.Y),view,projection,out sprite.TopLeft) ||
                !Clip(Point(bounds.X+bounds.Width,bounds.Y),view,projection,out sprite.TopRight) ||
                !Clip(Point(bounds.X,bounds.Y+bounds.Height),view,projection,out sprite.BottomLeft) ||
                !Clip(Point(bounds.X+bounds.Width,bounds.Y+bounds.Height),view,projection,out sprite.BottomRight)) return false;
            sprite.Projected=true;
            return true;
        }

        private static bool Clip(Vector3D world, MatrixD view, MatrixD projection, out Vector4 clip)
        {
            // Subtract the eye in double precision before sending small clip coordinates to D3D.
            var local=Vector3D.Transform(world,view);
            var projected=Vector4D.Transform(new Vector4D(local,1),projection);
            clip=(Vector4)projected;
            return local.IsValid() && local.Z<-0.05 && projected.W>0 &&
                (projected.X+projected.Y+projected.Z+projected.W).IsValid();
        }
    }
}
