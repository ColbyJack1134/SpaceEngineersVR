using System;

namespace SpaceEngineersVR.Config
{
    public partial class PluginConfig
    {
        // WeaponCore Manual-mode aim source in VR: 0 headset gaze, 1 right-hand pointer, 2 WeaponCore default (ship/camera).
        private int weaponCoreAim;
        public int WeaponCoreAim { get => weaponCoreAim; set => SetValue(ref weaponCoreAim,value<0 || value>2 ? 0:value); }

        // Which WeaponCore threats the world markers show: 0 all while aiming a lock, 1 all always, 2 only the pointed one while
        // aiming. The locked target always shows.
        private int threatMarkers;
        public int ThreatMarkers { get => threatMarkers; set => SetValue(ref threatMarkers,value<0 || value>2 ? 0:value); }

        // Controller pulses for WeaponCore missile / drone warnings and Defense Shields warnings. Notices show either way.
        private bool warningPulse=true;
        public bool WarningPulse { get => warningPulse; set => SetValue(ref warningPulse,value); }
        public const float DefaultWarningPulseInterval=1.5f,MaxWarningPulseInterval=5;
        // Seconds between repeat pulses while a missile / drone warning lasts; 0 pulses only when it starts.
        private float warningPulseInterval=DefaultWarningPulseInterval;
        public float WarningPulseInterval
        {
            get => warningPulseInterval;
            set => SetValue(ref warningPulseInterval,(float)Math.Round(Bound(value,0,MaxWarningPulseInterval,DefaultWarningPulseInterval)*2)/2);
        }
    }
}
