using System;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.World;
using SpaceEngineersVR.Multiplayer;
using SpaceEngineersVR.Plugin;
using VRageMath;

namespace SpaceEngineersVR.Player
{
    // Empty-hand grip and trigger on a loose grid. Physics runs through GridGrab where it is authoritative; on a vanilla host nothing happens.
    internal static class HandGrab
    {
        private sealed class Hand
        {
            public GridGrab.Hold Hold;
            public Matrix PalmInGrid;
            public bool WasDown;
            public DateTime NextStrain;
        }
        private static readonly Hand[] hands={new Hand(),new Hand()};
        private static readonly GridGrab.Holder holder=new GridGrab.Holder();
        private static MyCharacter owner;
        private static bool disabled;
        private static DateTime sampled;
        internal static bool Holding(Controller hand) => For(hand).Hold!=null;
        private static Hand For(Controller hand) => hands[hand==Player.HandL ? 0:1];
        internal static bool TryPalm(Controller hand,out MatrixD palm)
        {
            palm=MatrixD.Identity;
            var h=For(hand);
            if(h.Hold==null || !GridGrab.Grabbable(h.Hold.Grid)) return false;
            palm=(MatrixD)h.PalmInGrid*h.Hold.Grid.WorldMatrix;
            return palm.IsValid();
        }
        public static void Reset()
        {
            foreach(var hand in hands) Release(hand,Array.IndexOf(hands,hand)==0,false);
            owner=null;
        }
        public static void Update()
        {
            if(disabled) return;
            try { UpdateCore(); }
            catch(Exception ex) { disabled=true; Reset(); Logger.Warning(ex,"Hand grab disabled"); }
        }
        private static void UpdateCore()
        {
            var now=DateTime.UtcNow;
            float dt=(float)Math.Min(.05,Math.Max(0,(now-sampled).TotalSeconds)); sampled=now;
            var character=MySession.Static?.LocalCharacter;
            if(owner!=character) { Reset(); owner=character; }
            bool available=Main.VrActive && character!=null && !character.IsDead && !character.IsSitting && !character.IsOnLadder &&
                MySession.Static.ControlledEntity==character && character.CurrentWeapon==null && InputRouter.Gameplay && !Main.MenuOpen &&
                !ThirdPersonView.Active && !PlacementControls.OwnsTools && !TouchScreenBridge.OwnsInput && !FloatingWindows.OwnsInput &&
                MultiplayerRuntime.GrabSupported;
            var c=Controls.Static;
            bool flying=character?.JetpackComp?.TurnedOn==true;
            for(int i=0;i<2;i++)
            {
                var hand=hands[i]; bool left=i==0; var controller=left ? Player.HandL:Player.HandR;
                bool handFree=available && controller.pose.isTracked && !SpatialUi.Owns(controller) && !HelmetHud.Consumes(controller) && !LadderClimb.Holding(controller) &&
                    !GridSelection.Owns(controller) && !HandInteraction.Consumes(controller) && !CockpitControls.Held(controller);
                bool down=handFree && c.GripClick(controller).IsPressed;
                // Grip alone sprints on the left and rests naturally on things; the chord starts a grab, grip alone keeps it.
                bool squeeze=down && (left ? c.LeftTriggerPressure:c.PointerPressure).RawPosition.X>.5f;
                bool palmKnown=TrackedArms.TryFreePalm(character,controller,out var palm);
                if(hand.Hold!=null)
                {
                    bool other=hands[1-i].Hold?.Grid==hand.Hold.Grid;
                    bool kept=down && palmKnown && (MultiplayerRuntime.AppliesPhysics ? GridGrab.Apply(hand.Hold,palm.Translation,flying,dt,other):
                        GridGrab.Grabbable(hand.Hold.Grid) && Vector3D.Distance(hand.Hold.Point,palm.Translation)<=GridGrab.Slip);
                    if(!kept) { Release(hand,left,down); hand.WasDown=squeeze; continue; }
                    if(hand.Hold.Saturation>.3f && now>=hand.NextStrain)
                    {
                        float strain=Math.Min(1,hand.Hold.Saturation);
                        controller.Vibrate(0,.014f,50+40*strain,.08f+.35f*strain); hand.NextStrain=now.AddMilliseconds(45);
                    }
                    hand.WasDown=squeeze; continue;
                }
                // The chord must start on the grid; a grip held from elsewhere does not pick things up when brushed.
                bool found=squeeze && !hand.WasDown && palmKnown && GridGrab.TryFind(palm,out var grid,out var local,out double miss);
                if(squeeze && !hand.WasDown && palmKnown && !found)
                    Logger.Info("Grab: no loose grid at the "+(left ? "left":"right")+" hand; nearest block face "+(double.IsInfinity(miss) ? "none within 1.5 m":miss.ToString("0.00")+" m"));
                if(found)
                {
                    hand.Hold=new GridGrab.Hold {Grid=grid,Local=local};
                    hand.PalmInGrid=(Matrix)(palm*grid.PositionComp.WorldMatrixNormalizedInv);
                    hand.NextStrain=now;
                    if(left) holder.Left=hand.Hold; else holder.Right=hand.Hold;
                    // Grip also crouches or rolls depending on the hand; a grab takes the grip for itself until release.
                    c.BlockGripRoles(controller); c.BlockTriggerRoles(controller);
                    if(controller==Handedness.ActionHand) c.Fire.BlockUntilRelease();
                    CockpitFeedback.Engage(controller);
                    MultiplayerRuntime.SendGrab(grid,GridGrab.Encode(left,local));
                }
                hand.WasDown=squeeze;
            }
        }
        private static void Release(Hand hand,bool left,bool stillDown)
        {
            if(hand.Hold==null) return;
            var grid=hand.Hold.Grid;
            hand.Hold=null;
            if(left) holder.Left=null; else holder.Right=null;
            var controller=left ? Player.HandL:Player.HandR;
            if(stillDown) controller.Vibrate(0,.03f,70,.3f);
            MultiplayerRuntime.SendGrab(grid,GridGrab.EncodeRelease(left));
        }
    }
}
