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
        internal const int Count=25;
        internal const float Travel=1.134464f; // 65 degrees between the installed open/closed leaf faces.
        internal static int Slot(int index) => index<13 ? index : index+8;
        internal static float Initial(int index) => index<13 ? index<9 ? 1 : 0 : index==14 || index==15 || index==16 || index==17 || index==19 || index==23 || index==24 ? 1 : 0;
        internal static readonly Vector3[] Centers={
            new Vector3(-.397461f,-.365356f,.075684f),new Vector3(-.401245f,-.370117f,.138916f),
            new Vector3(-.347046f,-.400757f,.098419f),new Vector3(-.349609f,-.403931f,.140930f),
            new Vector3(-.344482f,-.397339f,.055084f),new Vector3(-.345703f,-.399048f,.076569f),
            new Vector3(-.348267f,-.402344f,.119537f),new Vector3(-.350708f,-.405518f,.160645f),
            new Vector3(-.382446f,-.387573f,.183472f),new Vector3(-.387939f,-.380127f,.053009f),
            new Vector3(-.390625f,-.383545f,.096741f),new Vector3(-.391846f,-.385132f,.118011f),
            new Vector3(-.394165f,-.388184f,.156189f),
            new Vector3(-0.148925783f,-0.399291990f,-0.258178709f),
            new Vector3(-0.124755860f,-0.386230464f,-0.272460938f),
            new Vector3(-0.101196287f,-0.386230464f,-0.272460938f),
            new Vector3(-0.077667237f,-0.386230464f,-0.272460938f),
            new Vector3(-0.054153443f,-0.386230464f,-0.272460938f),
            new Vector3(-0.031814575f,-0.399291990f,-0.258178709f),
            new Vector3(0.051956177f,-0.386230464f,-0.272460938f),
            new Vector3(0.075073245f,-0.399291990f,-0.258178709f),
            new Vector3(0.098297119f,-0.399291990f,-0.258178709f),
            new Vector3(0.120880127f,-0.399291990f,-0.258178709f),
            new Vector3(0.148620606f,-0.386230464f,-0.272460938f),
            new Vector3(0.171508787f,-0.386230464f,-0.272460938f) };
        internal static readonly Vector3[] Hinges={
            new Vector3(-.404663f,-.378784f,.074219f),new Vector3(-.408447f,-.383667f,.137451f),
            new Vector3(-.354248f,-.414185f,.096954f),new Vector3(-.356812f,-.417480f,.139465f),
            new Vector3(-.351685f,-.410889f,.053604f),new Vector3(-.352905f,-.412598f,.075073f),
            new Vector3(-.355469f,-.415894f,.118103f),new Vector3(-.358032f,-.418945f,.159119f),
            new Vector3(-.389648f,-.401001f,.181946f),new Vector3(-.403320f,-.377075f,.052353f),
            new Vector3(-.406006f,-.380371f,.096069f),new Vector3(-.407227f,-.382080f,.117371f),
            new Vector3(-.409546f,-.385010f,.155579f),
            new Vector3(-0.149032319f,-0.402600468f,-0.273650791f),
            new Vector3(-0.124890238f,-0.401997446f,-0.276104234f),
            new Vector3(-0.101332473f,-0.401997460f,-0.276104198f),
            new Vector3(-0.077801596f,-0.401997446f,-0.276104234f),
            new Vector3(-0.054281271f,-0.401997455f,-0.276104220f),
            new Vector3(-0.031917312f,-0.402600520f,-0.273650752f),
            new Vector3(0.051821800f,-0.401997446f,-0.276104234f),
            new Vector3(0.074971040f,-0.402600527f,-0.273650746f),
            new Vector3(0.098194932f,-0.402600528f,-0.273650746f),
            new Vector3(0.120777941f,-0.402600528f,-0.273650746f),
            new Vector3(0.148486265f,-0.401997446f,-0.276104235f),
            new Vector3(0.171374409f,-0.401997446f,-0.276104234f) };
        internal static Matrix Visual(int slot,float openness) => CockpitStickMath.Around(Hinges[slot],
            Matrix.CreateFromAxisAngle(CockpitSwitchGeometry.AxisFor(Slot(slot)),
                (MathHelper.Clamp(openness,0,1)-Initial(slot))*Travel));
        internal static MatrixD TouchPose(int slot,float openness)
        {
            Matrix visual=Visual(slot,openness);
            Vector3 center=Vector3.Transform(Centers[slot],visual);
            // A small target on the free end, separate from the lever underneath.
            Vector3 leaf=center-Hinges[slot];
            center+=leaf*.45f;
            Vector3 normal=Vector3.TransformNormal(CockpitSwitchGeometry.NormalFor(Slot(slot)),
                Matrix.CreateFromAxisAngle(CockpitSwitchGeometry.AxisFor(Slot(slot)),openness*Travel));
            return MatrixD.CreateWorld(center+normal*.002f,-normal,Vector3.Normalize(leaf));
        }
        internal static MyModelData[] Build(Dictionary<string,object> tags)
        {
            var parts=CockpitSwitchGeometry.Partition(tags,Material,4126,Centers.Concat(new[] { CockpitBarGeometry.Center }).ToArray(),
                Enumerable.Range(0,Count).Select(i=>Initial(i)>0 ? 42 : 24).Concat(new[] { 70 }).ToArray());
            CockpitBarGeometry.ExtendStem(parts[parts.Length-1]);
            return parts;
        }
    }
}
