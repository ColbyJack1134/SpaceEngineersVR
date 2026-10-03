using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Sandbox.Game.Entities;
using Sandbox.Game.Gui;
using Sandbox.Game.GUI.HudViewers;
using Sandbox.Game.Localization;
using Sandbox.Game.Weapons;
using Sandbox.Game.World;
using Sandbox.ModAPI;
using SharpDX.Direct3D11;
using VRage;
using VRageMath;

namespace SpaceEngineersVR.Player
{
    internal static class NativeLead
    {
        internal sealed class View
        {
            public Vector3D Position,Target;
            public Vector4 Color;
            public bool InRange;
        }
        private static readonly FieldInfo lead=AccessTools.Field(typeof(MyHudMarkerRender.MyTargetIndicatorRender),"m_targetLeadRender");
        private static readonly FieldInfo prediction=AccessTools.Field(lead.FieldType,"m_positionPrediction");
        private static OverlayCanvas warning;
        private static ShaderResourceView warningTexture;
        private static int warningRevision=-1;
        internal static View Capture(MyHudMarkerRender renderer)
        {
            var indicator=renderer.TargetIndicatorRender;
            var circle=MyHud.TargetingMarkers?.TargetingCircle;
            var target=indicator?.TargetInfo?.Entity;
            var controlled=MySession.Static?.ControlledEntity;
            if(indicator?.HasTargetLock!=true || target==null || target.Closed || target.MarkedForClose || circle==null || circle.State.ToString()!="Visible" ||
                RemoteView.Current!=null || MyHud.MinimalHud || MyHud.IsHudMinimal || MyHud.CutsceneHud ||
                !(controlled is Sandbox.ModAPI.IMyTargetingCapableBlock capable) || !capable.CanActiveToolShoot() || !capable.IsTargetLockingEnabled()) return null;
            if(controlled is MyCubeBlock block && (!block.IsWorking || !block.CubeGrid.IsPowerSwitchOn)) return null;
            var native=lead.GetValue(indicator);
            var predictor=native==null ? null:prediction.GetValue(native) as MyLargeTurretTargetingSystem.MyPositionPrediction;
            if(predictor==null || !(controlled.Entity is IMyShootOrigin origin)) return null;
            // Reuse the game's predictor, including its ammunition, velocity and per-tick cache.
            predictor.Origin=origin; predictor.UsePrediction=true;
            predictor.PreferedGridTargetingOption=target is MyCubeGrid grid && !grid.IsStatic ?
                MyLargeTurretTargetingSystem.MyPositionPrediction.MyGridTargetingOption.CenterOfMass:
                MyLargeTurretTargetingSystem.MyPositionPrediction.MyGridTargetingOption.AABBCenter;
            var position=predictor.GetTargetEntityCoordinates(target);
            if(!position.IsValid()) return null;
            double range=predictor.CurrentAmmoDefininiton?.MaxTrajectory ?? origin.MaxShootRange;
            bool inRange=Vector3D.DistanceSquared(target.PositionComp.GetPosition(),MyHudMarkerRender.GetDistanceMeasuringMatrix().Translation)<=range*range;
            return new View {Position=position,Target=Sandbox.Game.EntityComponents.MyTargetingHelper.Instance.GetLockingPosition(target),InRange=inRange,
                Color=MyHudMarkerRender.MyTargetIndicatorRender.GetTargetingColor(circle.TargetType,inRange).ToVector4()};
        }
        internal static void Draw(Texture2D target,View value,MatrixD head,MatrixD view,MatrixD projection)
        {
            if(value==null || !WorldMarkers.Project(value.Position,view,projection,out _) ||
                !MarkerBillboard.TryCreate(value.Position,head,out var board)) return;
            board.Scale*=.55*4.5*.4;
            var output=new List<NativeSprite>();
            var marker=new NativeSprite(@"Textures\GUI\TargetingPredictionMarker.dds",default(RectangleF),value.Color);
            if(!board.Project(new RectangleF(-.5f,-.5f,1,1),view,projection,ref marker)) return;
            output.Add(marker);
            if(MarkerBillboard.TryCreate(value.Target,head,out var ring))
            {
                ring.Scale*=.55*4.5;
                AddLine(output,ring,board,value.Color,target.Description.Width,target.Description.Height,view,projection);
                if(!value.InRange)
                {
                    if(warning==null) { warning=new OverlayCanvas("Native lead range",512,64,1,false,target.Device,true); warningTexture=new ShaderResourceView(target.Device,warning.Texture); }
                    if(warningRevision!=NativeSprites.Revision)
                    {
                        warning.Clear(System.Drawing.Color.Transparent); warning.Upload();
                        var glyphs=new List<NativeSprite>();
                        SignalFont.Add(glyphs,MyTexts.GetString(MySpaceTexts.LeadIndicator_OutOfWeaponRange),0,0,28,512,Vector4.One,512,64,true);
                        NativeSprites.Draw(warning.Texture,glyphs); target.Device.ImmediateContext.GenerateMips(warningTexture); warningRevision=NativeSprites.Revision;
                    }
                    var label=new NativeSprite(null,default(RectangleF),value.Color) {Texture=warningTexture,Premultiplied=true};
                    if(ring.Project(new RectangleF(-2f,1.2f,4f,.5f),view,projection,ref label)) output.Add(label);
                }
            }
            NativeSprites.Draw(target,output);
        }
        private static void AddLine(List<NativeSprite> output,MarkerBillboard ring,MarkerBillboard marker,Vector4 color,int width,int height,MatrixD view,MatrixD projection)
        {
            Func<Vector3D,Vector4> clip=p=>(Vector4)Vector4D.Transform(new Vector4D(Vector3D.Transform(p,view),1),projection);
            var a=clip(ring.Center); var b=clip(marker.Center);
            if(a.W<=0 || b.W<=0) return;
            Func<Vector4,Vector2> pixel=p=>new Vector2((p.X/p.W+1)*width/2,(1-p.Y/p.W)*height/2);
            var start=pixel(a); var end=pixel(b); var delta=end-start; float length=delta.Length();
            float ra=Vector2.Distance(start,pixel(clip(ring.Point(.5,0)))),rb=Vector2.Distance(end,pixel(clip(marker.Point(.5,0))));
            if(length<=ra+rb+1) return;
            var direction=delta/length; start+=direction*ra; end-=direction*rb;
            var normal=new Vector2(-direction.Y,direction.X)*(1.5f*height/1080);
            Func<Vector2,Vector4,Vector4> corner=(p,c)=>new Vector4((p.X*2/width-1)*c.W,(1-p.Y*2/height)*c.W,c.Z,c.W);
            output.Add(new NativeSprite(@"Textures\GUI\TargetingLine.dds",default(RectangleF),color) {Projected=true,
                TopLeft=corner(start-normal,a),BottomLeft=corner(start+normal,a),TopRight=corner(end-normal,b),BottomRight=corner(end+normal,b)});
        }
    }
}
