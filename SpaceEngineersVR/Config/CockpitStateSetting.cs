namespace SpaceEngineersVR.Config
{
    public sealed class CockpitStateSetting
    {
        public int LayoutVersion { get; set; }
        public string World { get; set; }
        public long Cockpit { get; set; }
        public bool[] Covers { get; set; }
    }
}
