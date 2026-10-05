using System;
using VRageMath;

namespace SpaceEngineersVR.Player
{
    internal struct CockpitProbe
    {
        internal const float Radius=.011f,Length=.028f,TipExtension=.003f;
        internal const float CenterSide=.0025f,CenterUp=-.0005f;
        internal Vector3D Tip,Start,End;
        internal CockpitProbe(MatrixD pointer,bool left=false)
        {
            Tip=pointer.Translation;
            // The index bone axis is offset from the glove mesh center, mirrored between hands.
            var center=Tip+pointer.Right*(left ? CenterSide:-CenterSide)+pointer.Up*CenterUp;
            Start=center+pointer.Forward*(TipExtension-Radius);
            End=Start-pointer.Forward*Length;
        }
        internal CockpitProbe Transform(MatrixD matrix) => new CockpitProbe {
            Tip=Vector3D.Transform(Tip,matrix),Start=Vector3D.Transform(Start,matrix),End=Vector3D.Transform(End,matrix) };
        internal float DistanceSquared(BoundingBox box,out Vector3 contact) => SegmentBox.DistanceSquared(Start,End,box,out contact);
        internal bool Intersects(BoundingBox bounds,float padding=0) => DistanceSquared(bounds,out _) <= (Radius+padding)*(Radius+padding);
    }
}
