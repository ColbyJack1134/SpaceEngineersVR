using System.Runtime.CompilerServices;
using System.Collections.Generic;
using Valve.VR;
using VRageMath;

namespace SpaceEngineersVR.Player.Control
{
    public class Analog
    {
        private static readonly unsafe uint InputAnalogActionData_t_size = (uint)sizeof(InputAnalogActionData_t);

        private InputAnalogActionData_t data;
        private readonly ulong handle;
        private readonly InputGate gate = new InputGate();
        private readonly InputGate press;
        private readonly float pressThreshold;
        internal bool CanPress => press?.Ready ?? gate.Ready;

        public bool Active => data.bActive;
        internal ulong HeldOrigin => gate.Held ? data.activeOrigin : OpenVR.k_ulInvalidInputValueHandle;
        internal Vector2 RawPosition => data.bActive ? new Vector2(data.x,data.y) : Vector2.Zero;
        public Vector2 Position => gate.Held ? new Vector2(VrMath.Deadzone(data.x), VrMath.Deadzone(data.y)) : Vector2.Zero;
        public Vector2 Delta => new Vector2(data.deltaX, data.deltaY);
        public void BlockUntilRelease(bool blockPress=true) { gate.Block(); if(blockPress) press?.Block(); }

        public Analog(string name,float pressThreshold=0)
        {
            this.pressThreshold=pressThreshold;
            if(pressThreshold>0) press=new InputGate();
            var error = OpenVR.Input.GetActionHandle(name, ref handle);
            if (error != EVRInputError.None) throw new System.InvalidOperationException("Invalid SteamVR action " + name + ": " + error);
        }

        internal Analog(ulong actionHandle,float pressThreshold=0)
        { handle=actionHandle; this.pressThreshold=pressThreshold; if(pressThreshold>0) press=new InputGate(); }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Update(ISet<ulong> continuingOrigins = null)
        {
            var sample = default(InputAnalogActionData_t);
            if (OpenVR.Input.GetAnalogActionData(handle, ref sample, InputAnalogActionData_t_size, OpenVR.k_ulInvalidInputValueHandle) != EVRInputError.None)
                sample = default(InputAnalogActionData_t);
            AcceptSample(sample, continuingOrigins);
        }

        internal void AcceptSample(InputAnalogActionData_t sample, ISet<ulong> continuingOrigins = null)
        {
            data = sample;
            // UI clicks rearm below the squeeze threshold; flight still requires full release.
            press?.Update(data.bActive,data.x>pressThreshold);
            // Carry only an already accepted physical source into the new action set.
            // Different bindings, inactive samples and invalid origins cannot rearm it.
            bool continuing = data.activeOrigin != OpenVR.k_ulInvalidInputValueHandle &&
                continuingOrigins != null && continuingOrigins.Contains(data.activeOrigin);
            gate.Update(data.bActive, VrMath.Deadzone(data.x) != 0 || VrMath.Deadzone(data.y) != 0, continuing);
        }
    }
}
