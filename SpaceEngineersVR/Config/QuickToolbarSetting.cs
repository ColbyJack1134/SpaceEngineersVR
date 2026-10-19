namespace SpaceEngineersVR.Config
{
    // Owner is the player identity on foot, or the cockpit entity when seated.
    public sealed class QuickToolbarSetting
    {
        public string World { get; set; }
        public bool Character { get; set; }
        public long Owner { get; set; }
        public string ToolbarXml { get; set; }
    }
}
