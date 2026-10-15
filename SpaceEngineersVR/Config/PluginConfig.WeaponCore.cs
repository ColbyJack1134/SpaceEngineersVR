namespace SpaceEngineersVR.Config
{
    public partial class PluginConfig
    {
        // WeaponCore Manual-mode aim source in VR: 0 headset gaze, 1 right-hand pointer, 2 WeaponCore default (ship/camera).
        private int weaponCoreAim;
        public int WeaponCoreAim { get => weaponCoreAim; set => SetValue(ref weaponCoreAim,value<0 || value>2 ? 0:value); }
    }
}
