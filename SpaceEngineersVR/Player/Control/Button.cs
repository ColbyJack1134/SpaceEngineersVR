using System.Runtime.CompilerServices;
using Valve.VR;

namespace SpaceEngineersVR.Player.Control
{
    public class Button
    {
        private static readonly unsafe uint InputDigitalActionData_t_size = (uint)sizeof(InputDigitalActionData_t);

        private InputDigitalActionData_t data;
        private readonly ulong handle;
        private readonly InputGate gate = new InputGate();

        public bool Active => data.bActive;
        internal bool RawPressed => data.bActive && data.bState;
        public bool IsPressed => gate.Held;
        public bool HasChanged => gate.Pressed || gate.Released;
        public bool HasPressed => gate.Pressed;
        public bool HasReleased => gate.Released;
        public void BlockUntilRelease() => gate.Block();

        public Button(string name)
        {
            var error = OpenVR.Input.GetActionHandle(name, ref handle);
            if (error != EVRInputError.None) throw new System.InvalidOperationException("Invalid SteamVR action " + name + ": " + error);
        }

        internal Button(ulong actionHandle) { handle=actionHandle; }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Update()
        {
            if (OpenVR.Input.GetDigitalActionData(handle, ref data, InputDigitalActionData_t_size, OpenVR.k_ulInvalidInputValueHandle) != EVRInputError.None)
                data = default(InputDigitalActionData_t);
            AcceptSample(data);
        }

        internal void UpdateFrom(Button source)
        {
            var own=default(InputDigitalActionData_t);
            if (OpenVR.Input.GetDigitalActionData(handle, ref own, InputDigitalActionData_t_size, OpenVR.k_ulInvalidInputValueHandle) != EVRInputError.None)
                own = default(InputDigitalActionData_t);
            Mirror(own.bActive,source);
        }
        internal void Mirror(bool active,Button source)
        {
            var sample=source.data;
            sample.bActive=active && sample.bActive;
            AcceptSample(sample);
        }

        internal void UpdateFrom(Analog source,float press,float release)
        {
            float x=source.RawPosition.X;
            AcceptSample(new InputDigitalActionData_t { bActive=source.Active,bState=data.bState ? x>release:x>press,activeOrigin=source.Origin });
        }

        internal void AcceptSample(InputDigitalActionData_t sample)
        {
            data=sample;
            gate.Update(data.bActive, data.bState);
        }
    }
}
