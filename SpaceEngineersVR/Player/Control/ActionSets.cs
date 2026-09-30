using Valve.VR;

namespace SpaceEngineersVR.Player.Control
{
    public class ActionSets
    {
        private readonly unsafe uint VRActiveActionSet_t_size = (uint)sizeof(VRActiveActionSet_t);

        private VRActiveActionSet_t[] sets;

        public ActionSets(params string[] names)
        {
            sets = new VRActiveActionSet_t[names.Length];
            for (var i = 0; i < names.Length; i++)
            {
                var error = OpenVR.Input.GetActionSetHandle(names[i], ref sets[i].ulActionSet);
                if (error != EVRInputError.None) throw new System.InvalidOperationException("SteamVR action set " + names[i] + ": " + error);
                sets[i].ulRestrictedToDevice = OpenVR.k_ulInvalidInputValueHandle;
            }
        }

        public void Update()
        {
            var error = OpenVR.Input.UpdateActionState(sets, VRActiveActionSet_t_size);
            if (error != EVRInputError.None) throw new System.InvalidOperationException("SteamVR UpdateActionState: " + error);
        }
    }
}
