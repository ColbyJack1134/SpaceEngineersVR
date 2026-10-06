using System.Collections.Generic;
using VRageMath;

namespace SpaceEngineersVR.Player
{
    internal static class CockpitPanelGuard
    {
        internal const float Margin=.005f;
        internal static BoundingBox KeyBounds(SurfaceView surface,SurfaceKey key,float margin=0)
        {
            var b=key.Bounds;
            float z=key.Knob.HasValue ? WristKnob.Center(surface).Z:PhysicalSurface.KeyHeight(surface);
            return new BoundingBox(new Vector3((b.X-.5f)*surface.Width-margin,(.5f-b.Y-b.Height)*surface.Height-margin,z-margin),
                new Vector3((b.X+b.Width-.5f)*surface.Width+margin,(.5f-b.Y)*surface.Height+margin,z+margin));
        }
        internal static bool RoundContains(BoundingBox bounds,Vector3 point)
        {
            var half=bounds.HalfExtents; var delta=point-bounds.Center;
            return half.X>0 && half.Y>0 && delta.X*delta.X/(half.X*half.X)+delta.Y*delta.Y/(half.Y*half.Y)<=1;
        }
        internal static bool NearSurface(SurfaceView surface,CockpitProbe probe) => surface.Enabled &&
            CockpitTouch.NearKey(surface,probe.Transform(MatrixD.Invert(surface.Pose)),out _,out _,margin:Margin)>=0;
        internal static bool NearPlane(SurfaceView surface,CockpitProbe probe) => probe.Transform(MatrixD.Invert(surface.Pose)).Intersects(
            new BoundingBox(new Vector3(-surface.Width/2-Margin,-surface.Height/2-Margin,-Margin),
                new Vector3(surface.Width/2+Margin,surface.Height/2+Margin,Margin)),.001f);
        internal struct Region
        {
            internal Matrix Frame;
            internal BoundingBox Bounds;
            internal Region(Matrix frame,BoundingBox bounds) { Frame=frame; Bounds=bounds; }
        }
        // Inspection fixture: all switch covers open, moving grips at the supplied value.
        internal static Region[] Regions(CockpitRig rig,float value=0)
        {
            var regions=new List<Region>();
            for(int slot=0;slot<rig.Count;slot++)
            {
                var surface=CockpitButtons.Preview(rig.Subtype,slot);
                var handle=rig.HandleAt(slot); var bar=rig.BarAt(slot); var lever=rig.LeverAt(slot); var button=rig.ButtonAt(slot);
                surface.Pose*=handle?.Visual(value) ?? bar?.Visual(value) ?? lever?.Visual(value) ?? button?.Visual(value==1) ?? Matrix.Identity;
                var bounds=handle!=null && !handle.Pinch ? new BoundingBox(new Vector3(-handle.HalfWidth,-handle.Radius,-2*handle.Radius)-new Vector3(Margin),
                    new Vector3(handle.HalfWidth,handle.Radius,0)+new Vector3(Margin)):KeyBounds(surface,surface.Keys[0],Margin);
                regions.Add(new Region((Matrix)surface.Pose,bounds));
                if(lever?.CoverActor>=0)
                {
                    var cover=new SurfaceView {Style=SurfaceStyle.ModelControl,Width=.019f,Height=.035f};
                    regions.Add(new Region((Matrix)lever.CoverPose(1),KeyBounds(cover,new SurfaceKey("",0,0,1,1),Margin)));
                }
            }
            return regions.ToArray();
        }
    }
}
