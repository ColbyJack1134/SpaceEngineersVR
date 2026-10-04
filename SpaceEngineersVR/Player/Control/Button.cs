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

        internal void AcceptSample(InputDigitalActionData_t sample)
        {
            data=sample;
            gate.Update(data.bActive, data.bState);
        }
    }
}
