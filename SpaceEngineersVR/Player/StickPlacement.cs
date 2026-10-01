using VRageMath;

namespace SpaceEngineersVR.Player
{
    // Translations are cockpit-local. Articulation is applied first, so the pivot,
    // handle, contact and attached wrist all receive exactly the same offset.
    internal sealed class StickPlacement
    {
        public Vector3 Left { get; private set; }
        public Vector3 Right { get; private set; }
        public bool Unlocked { get; private set; }
        private Vector3 savedLeft, savedRight;
        public static Vector3 Limit(Vector3 offset, bool left) => !offset.IsValid() ? Vector3.Zero :
            Vector3.Clamp(offset, new Vector3(left ? -.20f : -.40f, -.15f, -.25f),
                new Vector3(left ? .40f : .20f, .30f, .35f));
        public void Load(Vector3 left, Vector3 right)
        { Left=savedLeft=Limit(left,true); Right=savedRight=Limit(right,false); Unlocked=false; }
        public void Unlock() { Unlocked=true; }
        public void Lock() { savedLeft=Left; savedRight=Right; Unlocked=false; }
        public void Cancel() { Left=savedLeft; Right=savedRight; Unlocked=false; }
        public void Move(bool left, Vector3 startOffset, Vector3 startPalm, Vector3 palm)
        {
            if (!Unlocked || !palm.IsValid() || !startPalm.IsValid()) return;
            Vector3 value=Limit(startOffset+palm-startPalm,left);
            if (left) Left=value; else Right=value;
        }
        public static Matrix Visual(Matrix articulation, Vector3 offset) => articulation*Matrix.CreateTranslation(offset);
    }
}
