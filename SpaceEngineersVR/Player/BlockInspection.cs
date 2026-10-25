using System;
using System.Collections.Generic;
using HarmonyLib;
using Sandbox;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.Gui;
using Sandbox.Game.Weapons;
using Sandbox.Game.World;
using SpaceEngineersVR.Plugin;
using VRage.Game;
using VRageMath;

namespace SpaceEngineersVR.Player
{
    internal static class BlockInspection
    {
        private static readonly MyHudBlockInfo info=new MyHudBlockInfo();
        private static DateTime next;
        private static MySlimBlock previous;
        private static MyDefinitionId shown;
        private static (long,int,int,bool) changes;
        private static DateTime settle;
        private static readonly System.Reflection.FieldInfo markTime=AccessTools.Field(typeof(MyEngineerToolBase),"m_lastMarkTime"),marked=AccessTools.Field(typeof(MyEngineerToolBase),"m_markedComponent");
        public static SurfaceView Current { get; private set; }
        public static bool ConsumesSecondary { get; private set; }
        // Direction from the finger to the card along the head's right axis.
        internal static int Side { get; private set; }=-1;
        internal static bool Visible(bool grip,bool automatic) => grip!=automatic;
        public static void Reset() { Current=null; previous=null; shown=default(MyDefinitionId); changes=default((long,int,int,bool)); next=DateTime.MinValue; ConsumesSecondary=false; NativeBlockInfo.Hide(); }
        public static void Update()
        {
            inspected=null;
            Inspect();
            if(Current==null) NativeBlockInfo.Hide();
#if SEVR_DIAGNOSTICS
            inspected=inspected ?? Diagnostics.BuildInfoProbe.Inspected;
#endif
            Patches.BuildInfoBridge.Inspect(inspected);
        }
        private static MySlimBlock inspected;
        private static void Inspect()
        {
            Current=null; ConsumesSecondary=false;
            if(!NativeBlockInfo.Available) return;
            bool tool=WeaponHandling.ToolEquipped;
            var character=MySession.Static?.LocalCharacter;
            var hand=Handedness.ActionHand;
            bool grip=Controls.Static.ActionGripClick.RawPressed;
            if(!grip && !Common.Config.InspectWithoutGrip) return;
            if(!InputRouter.Gameplay || Main.MenuOpen || character==null || character.IsSitting ||
                MySession.Static.ControlledEntity!=character ||
                HelmetHud.NearHead(hand.GripTracking,Player.Headset.pose.deviceToAbsolute.matrix) ||
                hand==Player.HandR && SpatialUi.Pointing || SpatialUi.Owns(hand) || CockpitTouch.Owns(hand) || HandInteraction.Consumes(hand) || HandGrab.Holding(hand) || !hand.pose.isTracked) return;
            if(!TrackedArms.TryFreePointPose(hand,out var finger)) return;
            LineD ray;
            if(tool) ray=HandInteraction.RayForPose(finger);
            else if(!HandInteraction.TryInteractionRay(out ray)) return;
            var engineerTool=tool ? character.CurrentWeapon as MyEngineerToolBase : null;
            var block=engineerTool?.GetTargetBlock() ?? Target(ray);
            var projected=block==null ? Projected(ray) : block.CubeGrid.Projector!=null ? block : null;
            if(projected!=null) block=null;
            var definition=block?.BlockDefinition ?? projected?.BlockDefinition ?? (PlacementControls.OwnsTools && !VoxelTools.Active ? MyCubeBuilder.Static?.CurrentBlockDefinition : null);
            if(definition==null) { shown=default(MyDefinitionId); return; }
            // With a tool, grip is also the secondary-action modifier and must stay live.
            ConsumesSecondary=!tool && grip && InputRouter.Mode==InputMode.Walking;
            if(ConsumesSecondary) Controls.Static.ActionGripClick.BlockUntilRelease();
            if(!Visible(grip,Common.Config.InspectWithoutGrip)) return;
            // Rebuild only on change like the native tools, at most 10 Hz.
            var state=block==null ? default((long,int,int,bool)):(block.GetStockpileStamp()+(long)block.ComponentStack.LastChangeStamp,(int)(block.Integrity/block.MaxIntegrity*1000),Marked(engineerTool),engineerTool is MyAngleGrinder);
            // Icons stream in after a new target's first capture.
            bool target=previous!=(block ?? projected) || shown!=definition.Id;
            if(target) settle=DateTime.UtcNow.AddSeconds(2);
            if(target || (state!=changes || DateTime.UtcNow<settle) && DateTime.UtcNow>=next)
            {
                previous=block ?? projected; shown=definition.Id; changes=state;
                next=DateTime.UtcNow.AddMilliseconds(100);
                if(block!=null)
                {
                    MySlimBlock.SetBlockComponents(info,block);
                    info.MissingComponentIndex=Marked(engineerTool);
                    info.ShowComponentLoss=engineerTool is MyAngleGrinder;
                }
                else if(projected!=null) Projection(info,projected);
                else MySlimBlock.SetBlockComponents(info,definition,MyCubeBuilder.BuildComponent.GetBuilderInventory(character));
                NativeBlockInfo.Show(info,Key(block ?? projected,definition));
            }
            var head=SpatialUi.DeviceWorld(Player.Headset.pose.deviceToAbsolute.matrix);
            var pixels=NativeBlockInfo.Pixels(info.Components.Count);
            Side=hand==Player.HandR ? -1:1;
            if(!tool && !PlacementControls.OwnsTools) inspected=block ?? projected;
            Current=new SurfaceView { Id="Block inspection",Style=SurfaceStyle.BlockInfo,Title=definition.DisplayNameText,Text=Key(block ?? projected,definition),Handle=info.Components.Count,
                Width=Width,Height=Width*pixels.Y/pixels.X,Pose=CockpitTouch.LabelPose(head,finger.Translation,Width,Side) };
        }
        internal const float Width=.16f;
        // MyEngineerToolBase keeps the component a failed weld could not find for 2.5 s.
        private static int Marked(MyEngineerToolBase tool)
        {
            if(tool==null) return -1;
            int time=(int)markTime.GetValue(tool);
            return time>=0 && MySandboxGame.TotalGamePlayTimeInMilliseconds-time<=2500 ? (int)marked.GetValue(tool):-1;
        }
        // MyWelder's projection display.
        private static void Projection(MyHudBlockInfo target,MySlimBlock block)
        {
            MySlimBlock.SetBlockComponents(target,block);
            for(int i=0;i<target.Components.Count;i++)
            {
                var row=target.Components[i]; row.MountedCount=row.StockpileCount=0; row.DeconstructDegrades=false; target.Components[i]=row;
            }
            target.BlockIntegrity=.01f; target.MissingComponentIndex=0; target.ShowComponentLoss=false;
        }
        private static MySlimBlock Target(LineD ray) => Target(ray,out _);
        internal static MySlimBlock Pointed() => HandInteraction.TryInteractionRay(out var ray) ? Target(ray) : null;
        internal static MySlimBlock Target(LineD ray,out Vector3D point)
        {
            var character=MySession.Static.LocalCharacter;
            var aim=MatrixD.CreateWorld(ray.From,ray.Direction,Vector3D.CalculatePerpendicularVector(ray.Direction));
            Multiplayer.ToolContact.CastTarget(character,character?.CurrentWeapon as VRage.Game.Entity.MyEntity,aim,ray.Length,out var block,out _,out point,out _);
            return block;
        }
        private static readonly List<Vector3I> cells=new List<Vector3I>();
        private static MySlimBlock Projected(LineD ray)
        {
            if(!MyCubeGrid.GetLineIntersection(ref ray,out var grid,out _,out _,g=>g.Projector!=null) || grid.Projector==null) return null;
            grid.RayCastCells(ray.From,ray.To,cells);
            foreach(var cell in cells)
            {
                var block=grid.GetCubeBlock(cell);
                if(block!=null && grid.Projector.CanBuild(block,false)!=Sandbox.ModAPI.BuildCheckResult.AlreadyBuilt) return block;
            }
            return null;
        }
        private static string Key(MySlimBlock block,Sandbox.Definitions.MyCubeBlockDefinition definition) =>
            definition.Id+"|"+(block==null ? "":block.CubeGrid.EntityId+":"+block.UniqueId);
        internal static void Paint(OverlayCanvas canvas,SurfaceView view)
        {
            canvas.ClearTexture();
            // Never show the previous target's capture.
            if(NativeBlockInfo.View!=null && NativeBlockInfo.Captured==view.Text)
            {
                NativeSprites.Draw(canvas.Texture,new[] {new NativeSprite(null,new VRageMath.RectangleF(0,0,canvas.Width,canvas.Height),Vector4.One) {Texture=NativeBlockInfo.View,EncodeSrgb=true,Premultiplied=true}});
                CardAdditions.Draw(canvas.Texture,view.Text,view.Handle);
            }
        }
    }
}
