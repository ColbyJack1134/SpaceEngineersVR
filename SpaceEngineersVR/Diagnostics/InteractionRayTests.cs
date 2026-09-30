using System;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using SpaceEngineersVR.Patches;
using SpaceEngineersVR.Player;
using VRage.Game;
using VRageMath;

namespace SpaceEngineersVR.Diagnostics
{
    internal static class InteractionRayTests
    {
        public static void Run(Action<string> log)
        {
            // Read the installed engine bodies, not an imitation of their local layout.
            foreach (MethodBase target in InteractionRayPatch.TargetMethods())
            {
                var original = PatchProcessor.GetOriginalInstructions(target).ToList();
                var patched = InteractionRayPatch.Transpiler(original, target).ToList();
                int call = patched.FindIndex(i => i.operand is MethodInfo method &&
                    method.DeclaringType == typeof(InteractionRayPatch));
                if (call < 3 || patched.Count != original.Count + 4)
                    throw new Exception("Native detector ray override missing or duplicated");
                int origin = patched[call - 2].LocalIndex(), direction = patched[call - 1].LocalIndex();
                if (patched[call - 3].opcode != OpCodes.Ldarg_0 ||
                    patched[call + 1].LocalIndex() != origin || patched[call + 2].LocalIndex() != direction ||
                    !patched[call + 3].LoadsField(AccessTools.Field(typeof(MyConstants), nameof(MyConstants.DEFAULT_INTERACTIVE_DISTANCE))))
                    throw new Exception("Detector override no longer feeds its native endpoint calculation");
                if (patched[call + 1].labels.Count != 0)
                    throw new Exception("Native head/camera branch can bypass the hand-ray override");
                var retained = patched.Where((_, index) => index < call - 3 || index > call).ToArray();
                for (int i = 0; i < original.Count; i++)
                    if (retained[i].opcode != original[i].opcode || !Equals(retained[i].operand, original[i].operand))
                        throw new Exception("Native hit ordering, occlusion or use-object logic was modified");
                bool rejected = false;
                try { InteractionRayPatch.Transpiler(new[] { new CodeInstruction(OpCodes.Ret) }, target).ToList(); }
                catch (InvalidOperationException) { rejected = true; }
                if (!rejected) throw new Exception("Unknown detector layout accepted silently");
            }

            foreach (double yaw in new[] { -2.5, 0, 1.1 })
            {
                var pose = MatrixD.CreateRotationX(0.3) * MatrixD.CreateRotationY(yaw);
                pose.Translation = new Vector3D(1e6, -2e6, 3e6);
                var ray = HandInteraction.RayForPose(pose);
                if (Vector3D.Distance(ray.From, pose.Translation) > 1e-7 ||
                    Vector3D.Distance(ray.Direction, pose.Forward) > 1e-7 ||
                    Math.Abs(ray.Length - MyConstants.DEFAULT_INTERACTIVE_DISTANCE) > 1e-7)
                    throw new Exception("Interaction beam origin, direction or native reach disagrees at large world coordinates");
            }
            log("PASS interaction ray: installed ray/area detector IL, native collision/action code retained, branch entry, unknown-layout rejection and shared beam geometry");
        }
    }
}
