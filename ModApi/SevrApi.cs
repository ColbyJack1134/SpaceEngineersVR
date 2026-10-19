// Space Engineers VR mod API client, version 1. Copy this file into your mod unchanged; see MOD-API.md.
using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRageMath;

namespace SpaceEngineersVR.Api
{
    // Create one in your session component: Load() in LoadData, Unload() in UnloadData.
    // Without the plugin nothing arrives, IsActive stays false and every pose is null.
    public class SevrApi
    {
        public const long Channel = 6000296990326476873;
        private const string Request = "SpaceEngineersVR.ApiRequest";

        private Func<int> version;
        private Func<bool> isActive;
        private Func<MatrixD?> headMatrix;
        private Func<bool, MatrixD?> handMatrix;
        private Func<bool, Vector3D?> fingerTip;
        private Func<LineD?> interactionRay;
        private Action<Action<bool, LineD>> registerUse, unregisterUse;
        private readonly Action<object> received;
        private readonly Action<bool, LineD> used;

        // Raised each time the plugin sends the API, including after a VR restart.
        public event Action Ready;
        // A VR Use press: true for the main hand, and that hand's interaction ray. The game's own Use still runs.
        public event Action<bool, LineD> Use;

        public SevrApi()
        {
            received = Received;
            used = Used;
        }

        public bool Loaded { get { return version != null; } }
        public int Version { get { return version != null ? version() : 0; } }
        // The player is in VR. Check this each time; it can change during a session.
        public bool IsActive { get { return isActive != null && isActive(); } }
        // World matrix of the headset; Forward is the view direction.
        public MatrixD? HeadMatrix { get { return headMatrix != null ? headMatrix() : null; } }
        // Hands are named main (true) and other (false); left-handed players are mapped by the plugin.
        // World matrix of a controller; Forward is where it points.
        public MatrixD? HandMatrix(bool main) { return handMatrix != null ? handMatrix(main) : null; }
        // World position of the character's index fingertip.
        public Vector3D? FingerTip(bool main) { return fingerTip != null ? fingerTip(main) : null; }
        // The ray the game's Use currently follows, from the fingertip and limited to the normal use distance.
        public LineD? InteractionRay { get { return interactionRay != null ? interactionRay() : null; } }

        public void Load()
        {
            MyAPIGateway.Utilities.RegisterMessageHandler(Channel, received);
            MyAPIGateway.Utilities.SendModMessage(Channel, Request);
        }

        public void Unload()
        {
            if (unregisterUse != null) unregisterUse(used);
            MyAPIGateway.Utilities.UnregisterMessageHandler(Channel, received);
            version = null; isActive = null; headMatrix = null; handMatrix = null;
            fingerTip = null; interactionRay = null; registerUse = unregisterUse = null;
        }

        // Handlers run inside the game's message loop: do not register or unregister handlers here.
        private void Received(object message)
        {
            var api = message as Dictionary<string, Delegate>;
            if (api == null) return;
            var v = Get<Func<int>>(api, "Version");
            if (v == null || v() < 1) return;
            version = v;
            isActive = Get<Func<bool>>(api, "IsActive");
            headMatrix = Get<Func<MatrixD?>>(api, "HeadMatrix");
            handMatrix = Get<Func<bool, MatrixD?>>(api, "HandMatrix");
            fingerTip = Get<Func<bool, Vector3D?>>(api, "FingerTip");
            interactionRay = Get<Func<LineD?>>(api, "InteractionRay");
            registerUse = Get<Action<Action<bool, LineD>>>(api, "RegisterUse");
            unregisterUse = Get<Action<Action<bool, LineD>>>(api, "UnregisterUse");
            if (registerUse != null) registerUse(used);
            if (Ready != null) Ready();
        }

        private void Used(bool main, LineD ray)
        {
            if (Use != null) Use(main, ray);
        }

        private static T Get<T>(Dictionary<string, Delegate> api, string name) where T : class
        {
            Delegate member;
            return api.TryGetValue(name, out member) ? member as T : null;
        }
    }
}
