using System.Collections.Generic;
using SharpDX.Direct3D11;
using VRageMath;

namespace SpaceEngineersVR.Player
{
    // Build Info's measurement labels are HUD text at a projected point; each is drawn at that point with the marker billboard.
    internal static class BuildInfoLabels
    {
        private sealed class Slot { public readonly BuildInfoPanel Panel=BuildInfoPanel.Create(); public Vector3D Anchor; }
        private struct Label { public Slot Slot; public BuildInfoPanel.View View; public Vector3D Anchor; public double Share; }
        private static readonly List<Slot> slots=new List<Slot>();
        private static int used;
        private static volatile Label[] published=new Label[0];
        // Build Info offsets the text .03 HUD units right of the point: .015 screen widths, in screen heights at 16:9.
        private const double Offset=.015*16/9;

        internal static void BeginFrame() => used=0;
        internal static void EndFrame()
        {
            var labels=new List<Label>(used);
            for(int i=0;i<used;i++)
            {
                var slot=slots[i]; var view=slot.Panel.Current;
                if(view!=null) labels.Add(new Label { Slot=slot,View=view,Anchor=slot.Anchor,Share=slot.Panel.ScreenShare(view) });
            }
            published=labels.ToArray();
        }
        internal static BuildInfoPanel.Capture Begin(Vector3D anchor,double halfHeight)
        {
            if(used==slots.Count) slots.Add(new Slot());
            var slot=slots[used++];
            slot.Anchor=anchor;
            return slot.Panel.Begin(true,halfHeight);
        }
        internal static void Reset() { used=0; published=new Label[0]; foreach(var slot in slots) slot.Panel.Reset(); }

        // Render thread, with the marker head, up and facing.
        internal static void Draw(Texture2D target,MatrixD head,MatrixD view,MatrixD projection,bool faceViewer,Vector3D? up)
        {
            var labels=published;
            if(labels.Length==0) return;
            var sprites=new List<NativeSprite>(labels.Length);
            foreach(var label in labels)
            {
                if(!MarkerBillboard.TryCreate(label.Anchor,head,up,faceViewer,out var board)) continue;
                double screen=2*MarkerBillboard.DesktopHalfField*Vector3D.Distance(head.Translation,label.Anchor);
                double height=label.Share*screen,scale=height/label.View.Height,width=label.View.Width*scale;
                var pose=MatrixD.Identity;
                pose.Right=board.Right*scale; pose.Up=board.Up*scale; pose.Backward=Vector3D.Cross(board.Right,board.Up)*scale;
                pose.Translation=label.Anchor+board.Right*(Offset*screen+width/2)-board.Up*(height/2);
                sprites.Add(label.Slot.Panel.WorldQuad(target.Device,label.View,pose,view,projection));
            }
            if(sprites.Count>0) NativeSprites.Draw(target,sprites.ToArray());
        }
    }
}
