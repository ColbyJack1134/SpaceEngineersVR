using System.Threading;
using HarmonyLib;
using Sandbox.Game.Components;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.Lights;
using Sandbox.Game.World;
using SpaceEngineersVR.Plugin;
using VRageMath;

namespace SpaceEngineersVR.Player
{
    [HarmonyPatch(typeof(MyRenderComponentCharacter),nameof(MyRenderComponentCharacter.UpdateLightPosition))]
    internal static class HelmetLight
    {
        internal const float Height=.05f;
        private sealed class Frame
        {
            public readonly MyCharacter Character;
            public readonly MatrixD Head;
            public Frame(MyCharacter character,MatrixD head) { Character=character; Head=head; }
        }
        private static Frame frame;
        public static void Publish()
        {
            var character=MySession.Static?.LocalCharacter;
            bool active=Main.VrActive && character!=null && !character.IsDead && Player.Headset.pose.isTracked &&
                (CameraRig.Owns(character) || SeatFit.Eligible(SeatFit.Seat));
            Volatile.Write(ref frame,active ? new Frame(character,SpatialUi.DeviceWorld(Player.Headset.pose.deviceToAbsolute.matrix)) : null);
            if(active) (character.Render as MyRenderComponentCharacter)?.UpdateLightPosition();
        }
        internal static MatrixD Pose(MatrixD head)
        {
            head.Translation+=head.Up*Height;
            return head;
        }
        private static void Postfix(MyRenderComponentCharacter __instance,MyLight ___m_light)
        {
            var current=Volatile.Read(ref frame);
            if(!Main.VrActive || current==null || ___m_light==null || current.Character.Render!=__instance || current.Character.Closed) return;
            var pose=Pose(current.Head);
            ___m_light.ReflectorDirection=pose.Forward;
            ___m_light.ReflectorUp=pose.Up;
            ___m_light.Position=pose.Translation;
            ___m_light.UpdateLight();
        }
    }
}
