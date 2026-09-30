using HarmonyLib;
using VRage.Render.Scene;
using VRageMath;

namespace SpaceEngineersVR.Wrappers
{
    public sealed class SceneCamera
    {
        private readonly MyScene scene;
        public SceneCamera(object scene) { this.scene = (MyScene)scene; }
        public static SceneCamera Current()
        {
            var type = AccessTools.TypeByName("VRage.Render11.Scene.MyScene11");
            return new SceneCamera(AccessTools.Field(type,"Instance").GetValue(null));
        }
        // Environment is a struct. Modify the engine's field directly rather than a boxed copy.
        public Vector3D Position { get => scene.Environment.CameraPosition; set => scene.Environment.CameraPosition=value; }
    }
}
