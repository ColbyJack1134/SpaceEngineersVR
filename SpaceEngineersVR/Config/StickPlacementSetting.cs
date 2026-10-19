namespace SpaceEngineersVR.Config
{
    public class StickPlacementSetting
    {
        public string Subtype { get; set; }
        public float LeftX { get; set; }
        public float LeftY { get; set; }
        public float LeftZ { get; set; }
        public float RightX { get; set; }
        public float RightY { get; set; }
        public float RightZ { get; set; }
        // 1: Suspended Control Seat right offsets are relative to the inboard default.
        public int Version { get; set; }
        public const int Current=1;
    }
}
