namespace SpaceEngineersVR.Config
{
    public class MenuWindowSetting
    {
        internal const int DefaultFaceOpacity=55;
        public string Screen { get; set; }
        public string World { get; set; }
        public long Cockpit { get; set; }
        public int SignalMode { get; set; }
        public int AimMode { get; set; }
        [System.ComponentModel.DefaultValue(DefaultFaceOpacity)]
        public int FaceOpacity { get; set; } = DefaultFaceOpacity;
        public float Width { get; set; }
        public float Height { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
        public float QX { get; set; }
        public float QY { get; set; }
        public float QZ { get; set; }
        public float QW { get; set; }
    }
}
