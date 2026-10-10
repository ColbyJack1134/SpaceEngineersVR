using System;
using Sandbox.Game.World;
using SpaceEngineersVR.Plugin;
using VRageMath;

namespace SpaceEngineersVR.Player
{
    internal static class DampenerTargeting
    {
        private static object owner;
        private static Controller pointer;
        private static uint sequence,pending;
        private static bool faceAttempt;
        private static DateTime deadline;
        internal static bool InRange(VRage.Game.Entity.MyEntity target) => MySession.Static?.ControlledEntity!=null &&
            Sandbox.Game.GameSystems.MyEntityThrustComponent.IsInRangeOfRelativeDampening(MySession.Static.ControlledEntity,target);
        public static void Activate()
        {
            Cancel();
            if(!InputRouter.Gameplay || SpectatorView.Active || MySession.Static?.ControlledEntity==null || !Player.Headset.pose.isTracked) return;
            GridSelection.Clear();
            pointer=GameActions.InvocationHand ?? Player.HandR;
            GridSelection.CloseMenu();
            var head=CameraRig.DeviceWorld(Player.Headset.pose.deviceToAbsolute.matrix);
            Send(null,new LineD(head.Translation,head.Translation+head.Forward*1000),true,pointer);
        }
        internal static void Select(VRage.Game.Entity.MyEntity target,LineD ray,Controller hand) => Send(target,ray,false,hand);
        private static void Send(VRage.Game.Entity.MyEntity target,LineD ray,bool face,Controller hand)
        {
            var controlled=MySession.Static?.ControlledEntity;
            if(controlled==null) return;
            owner=controlled; pointer=hand; faceAttempt=face; pending=++sequence; if(pending==0) pending=++sequence;
            deadline=DateTime.UtcNow.AddSeconds(4);
            Multiplayer.DampenerRequests.Result=Complete;
            if(!Multiplayer.DampenerRequests.Request(pending,controlled.Entity.EntityId,target?.EntityId ?? 0,ray))
            { Cancel(); EssentialHud.Notify("Auto dampener targeting is unavailable"); }
        }
        private static void Complete(uint token,long entity,bool accepted)
        {
            if(token!=pending || pending==0 || !ReferenceEquals(owner,MySession.Static?.ControlledEntity) ||
                MySession.Static.ControlledEntity.Entity.EntityId!=entity) return;
            var hand=pointer; bool face=faceAttempt; Cancel();
            if(accepted) hand?.Vibrate(0,.025f,100,.2f);
            else if(InputRouter.Gameplay && !Main.MenuOpen) GridSelection.Arm("Dampeners",pointer:hand);
            else if(!face) EssentialHud.Notify("Auto dampener target is unavailable");
        }
        internal static void Cancel() { pending=0; owner=null; pointer=null; }
        public static void Update()
        {
            if(pending==0) return;
            if(!ReferenceEquals(owner,MySession.Static?.ControlledEntity) || Main.MenuOpen || !InputRouter.Gameplay ||
                MySession.Static?.LocalCharacter?.IsDead!=false || Controls.Static.Unequip.HasPressed)
            {
                if(Controls.Static.Unequip.HasPressed) Controls.Static.Unequip.BlockUntilRelease();
                Cancel(); return;
            }
            if(DateTime.UtcNow>=deadline) { Cancel(); EssentialHud.Notify("Update the host plugin for auto dampener targeting"); }
        }
    }
}
