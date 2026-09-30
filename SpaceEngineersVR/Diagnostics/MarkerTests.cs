using System;
using SpaceEngineersVR.Player;
using VRageMath;

namespace SpaceEngineersVR.Diagnostics
{
    internal static class MarkerTests
    {
        private static void Near(double actual,double expected,string name,double tolerance=1e-7)
        {
            if (!actual.IsValid() || Math.Abs(actual-expected)>tolerance) throw new Exception(name+": "+actual+" != "+expected);
        }
        private static double Angle(Vector3D a,Vector3D b) => Math.Acos(MathHelper.Clamp(Vector3D.Dot(Vector3D.Normalize(a),Vector3D.Normalize(b)),-1,1));

        public static void Run(Action<string> log)
        {
            int count=0;
            foreach (double roll in new[] { 0.0,0.5,-0.8 })
            foreach (double yaw in new[] { -1.15,-0.6,0,0.6,1.15 })
            foreach (double pitch in new[] { -0.55,0.0,0.55 })
            foreach (double distance in new[] { 2.0,200.0,1000000.0 })
            {
                var head=MatrixD.CreateRotationZ(roll);
                head.Translation=new Vector3D(1000000,2000000,3000000);
                var direction=Vector3D.TransformNormal(new Vector3D(Math.Sin(yaw)*Math.Cos(pitch),Math.Sin(pitch),-Math.Cos(yaw)*Math.Cos(pitch)),head);
                var position=head.Translation+direction*distance;
                if (!MarkerBillboard.TryCreate(position,head,out var board)) throw new Exception("Valid marker rejected");
                var normal=Vector3D.Cross(board.Right,board.Up);
                Near(Vector3D.Dot(normal,Vector3D.Normalize(head.Translation-position)),1,"Peripheral billboard does not face viewer");
                Near(board.Right.Length(),1,"Billboard right basis"); Near(board.Up.Length(),1,"Billboard up basis");
                Near(Vector3D.Dot(board.Right,board.Up),0,"Billboard sheared");
                Near(Angle(board.Point(-.5,0)-head.Translation,board.Point(.5,0)-head.Translation),MarkerBillboard.IconAngle,"Horizontal angular size");
                Near(Angle(board.Point(0,-.5)-head.Translation,board.Point(0,.5)-head.Translation),MarkerBillboard.IconAngle,"Vertical angular size/aspect");
                foreach (double eye in new[] { -0.032,0.032 })
                {
                    var view=MatrixD.Invert(MatrixD.CreateTranslation(eye,0,0)*head);
                    var projection=VrMath.Projection(-2.5f,2.8f,-1.8f,2.1f,.05);
                    var sprite=new NativeSprite(null,default(RectangleF),Vector4.One);
                    if (!board.Project(new RectangleF(-.5f,-.5f,1,1),view,projection,ref sprite) || !sprite.Projected)
                        throw new Exception("Billboard lost projected-quad path");
                    var actual=Vector4D.Transform((Vector4D)sprite.TopLeft,MatrixD.Invert(projection));
                    var recovered=Vector3D.Transform(new Vector3D(actual.X,actual.Y,actual.Z)/actual.W,MatrixD.Invert(view));
                    Near((recovered-board.Point(-.5,-.5)).Length()/distance,0,"Per-eye corner reprojection",2e-6);
                    if (Math.Abs(yaw)>.1 && Math.Abs(sprite.TopLeft.W-sprite.TopRight.W)<0.001)
                        throw new Exception("Peripheral quad lost perspective interpolation depth");
                }
                count++;
            }
            if (MarkerBillboard.TryCreate(Vector3D.Zero,MatrixD.Identity,out _) ||
                MarkerBillboard.TryCreate(new Vector3D(double.NaN,0,-1),MatrixD.Identity,out _)) throw new Exception("Invalid marker accepted");
            if (!MarkerBillboard.TryCreate(Vector3D.Up,MatrixD.Identity,out var pole) || !pole.Right.IsValid() || !pole.Up.IsValid())
                throw new Exception("Vertical marker billboard singularity");
            log("PASS marker billboards: "+count+" peripheral/tilted/distant poses, viewer-facing normals, equal angular dimensions, shared stereo corners and perspective depth");
        }
    }
}
