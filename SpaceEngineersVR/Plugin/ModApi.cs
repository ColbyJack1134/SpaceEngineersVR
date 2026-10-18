using System;
using System.Collections.Generic;
using Sandbox.Game.World;
using Sandbox.ModAPI;
using SpaceEngineersVR.Player;
using VRageMath;

namespace SpaceEngineersVR.Plugin
{
    // Public API for Workshop mods, which cannot reference the plugin: offered over mod messaging (see MOD-API.md).
    // Members and their meaning never change; additions raise Version.
    internal static class ModApi
    {
        internal const long Channel=6000296990326476873; // "SEVR_API" in ASCII
        internal const string Request="SpaceEngineersVR.ApiRequest";
        internal const int Version=1;
        private static Dictionary<string,Delegate> api;
        private static readonly Action<object> handler=Received;
        private static readonly List<Action<bool,LineD>> useCallbacks=new List<Action<bool,LineD>>();
        private static bool registered;

        internal static void Refresh()
        {
            if(!registered && MySession.Static!=null && MyAPIGateway.Utilities!=null) Listen();
        }
        internal static void Listen()
        {
            MyAPIGateway.Utilities.RegisterMessageHandler(Channel,handler);
            registered=true;
            Offer();
        }
        internal static void Reset()
        {
            if(registered) MyAPIGateway.Utilities?.UnregisterMessageHandler(Channel,handler);
            registered=false;
            useCallbacks.Clear();
        }
        private static void Received(object message)
        {
            if(message as string==Request) Offer();
        }
        private static void Offer() => MyAPIGateway.Utilities.SendModMessage(Channel,api ?? (api=Build()));

        internal static Dictionary<string,Delegate> Build() => new Dictionary<string,Delegate>
        {
            ["Version"]=new Func<int>(() => Version),
            ["IsActive"]=new Func<bool>(() => Main.VrActive && Main.WorldAvailable),
            ["HeadMatrix"]=new Func<MatrixD?>(Head),
            ["HandMatrix"]=new Func<bool,MatrixD?>(main => Hand(Pick(main),out var pose) ? pose:(MatrixD?)null),
            ["FingerTip"]=new Func<bool,Vector3D?>(main => TrackedArms.TryFreeFingertip(Pick(main),out var tip) ? tip:(Vector3D?)null),
            ["InteractionRay"]=new Func<LineD?>(() => HandInteraction.TryInteractionRay(out var ray) ? ray:(LineD?)null),
            ["RegisterUse"]=new Action<Action<bool,LineD>>(callback => { if(callback!=null && !useCallbacks.Contains(callback)) useCallbacks.Add(callback); }),
            ["UnregisterUse"]=new Action<Action<bool,LineD>>(callback => useCallbacks.Remove(callback)),
        };

        private static MatrixD? Head()
        {
            var headset=Player.Player.Headset.pose;
            if(!Main.VrActive || !Main.WorldAvailable || !headset.isTracked || MySector.MainCamera==null) return null;
            var pose=SpatialUi.DeviceWorld(headset.deviceToAbsolute.matrix);
            return pose.IsValid() ? pose:(MatrixD?)null;
        }
        // Mods name hands as main and other, so left-handed players need no special case.
        private static Controller Pick(bool main) => main ? Handedness.ActionHand:Handedness.SupportHand;
        private static bool Hand(Controller hand,out MatrixD pose)
        {
            pose=MatrixD.Identity;
            if(!Main.VrActive || !Main.WorldAvailable || !hand.pose.isTracked || MySector.MainCamera==null) return false;
            pose=SpatialUi.DeviceWorld(hand.AimTracking);
            return pose.IsValid();
        }

        // Runs before the native use; a mod cannot cancel it.
        internal static void Use(Controller hand)
        {
            if(useCallbacks.Count>0 && HandInteraction.TryInteractionRay(hand,out var ray)) Notify(hand==Handedness.ActionHand,ray);
        }
        internal static void Notify(bool main,LineD ray)
        {
            foreach(var callback in useCallbacks.ToArray())
            {
                try { callback(main,ray); }
                catch(Exception ex) { useCallbacks.Remove(callback); Logger.Warning(ex,"A mod's SEVR Use callback failed and was removed"); }
            }
        }
    }
}
