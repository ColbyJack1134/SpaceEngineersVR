using System;
using System.Collections.Generic;
using System.Linq;
using VRageMath;
using VRageRender.Import;
using VRageRender.Messages;

namespace SpaceEngineersVR.Player
{
    internal static class CockpitCoverGeometry
    {
        internal const string Material="CockpitFighter_Interior";
        internal const int Count=13;
        internal const float Travel=1.3f;
        internal static float Initial(int slot) => slot<9 ? 1 : 0;
        internal static readonly Vector3[] Centers={
            new Vector3(-.397461f,-.365356f,.075684f),new Vector3(-.401245f,-.370117f,.138916f),
            new Vector3(-.347046f,-.400757f,.098419f),new Vector3(-.349609f,-.403931f,.140930f),
            new Vector3(-.344482f,-.397339f,.055084f),new Vector3(-.345703f,-.399048f,.076569f),
            new Vector3(-.348267f,-.402344f,.119537f),new Vector3(-.350708f,-.405518f,.160645f),
            new Vector3(-.382446f,-.387573f,.183472f),new Vector3(-.387939f,-.380127f,.053009f),
            new Vector3(-.390625f,-.383545f,.096741f),new Vector3(-.391846f,-.385132f,.118011f),
            new Vector3(-.394165f,-.388184f,.156189f) };
        internal static readonly Vector3[] Hinges={
            new Vector3(-.404663f,-.378784f,.074219f),new Vector3(-.408447f,-.383667f,.137451f),
            new Vector3(-.354248f,-.414185f,.096954f),new Vector3(-.356812f,-.417480f,.139465f),
            new Vector3(-.351685f,-.410889f,.053604f),new Vector3(-.352905f,-.412598f,.075073f),
            new Vector3(-.355469f,-.415894f,.118103f),new Vector3(-.358032f,-.418945f,.159119f),
            new Vector3(-.389648f,-.401001f,.181946f),new Vector3(-.403320f,-.377075f,.052353f),
            new Vector3(-.406006f,-.380371f,.096069f),new Vector3(-.407227f,-.382080f,.117371f),
            new Vector3(-.409546f,-.385010f,.155579f) };
        internal static Matrix Visual(int slot,float openness) => CockpitStickMath.Around(Hinges[slot],
            Matrix.CreateFromAxisAngle(Vector3.Normalize(Vector3.Cross(CockpitSwitchGeometry.Normal,CockpitSwitchGeometry.Up)),
                (MathHelper.Clamp(openness,0,1)-Initial(slot))*Travel));
        internal static MatrixD TouchPose(int slot,float openness)
        {
            Matrix visual=Visual(slot,openness);
            Vector3 center=Vector3.Transform(Centers[slot],visual);
            // A small target on the free end, separate from the lever underneath.
            Vector3 leaf=center-Hinges[slot];
            center+=leaf*.45f;
            Vector3 normal=Vector3.TransformNormal(CockpitSwitchGeometry.Normal,
                Matrix.CreateFromAxisAngle(Vector3.Normalize(Vector3.Cross(CockpitSwitchGeometry.Normal,CockpitSwitchGeometry.Up)),openness*Travel));
            return MatrixD.CreateWorld(center+normal*.002f,-normal,Vector3.Normalize(leaf));
        }
        internal static MyModelData[] Build(Dictionary<string,object> tags) =>
            CockpitSwitchGeometry.Partition(tags,Material,4126,Centers,
                Enumerable.Range(0,Count).Select(i=>i<9 ? 42 : 24).ToArray());
    }
}
