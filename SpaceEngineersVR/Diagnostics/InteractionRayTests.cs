#if SEVR_DIAGNOSTICS
using System;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Sandbox.Game.Entities.Character.Components;
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
                var original = PatchProcessor.GetOriginalInstructions(target, out var generator).ToList();
                var patched = InteractionRayPatch.Transpiler(original, target, generator).ToList();
                int call = patched.FindIndex(i => i.operand is MethodInfo method &&
                    method.DeclaringType == typeof(InteractionRayPatch));
                bool area = target.DeclaringType.Name == "MyCharacterClosestDetectorComponent";
                int start = call - (area ? 5 : 3);
                int resume = call + (area ? 5 : 2);
                if (start < 0 || patched.Count != original.Count + resume - start)
                    throw new Exception("Native detector ray override missing or duplicated");
                int origin = patched[start + 1].LocalIndex(), direction = patched[start + 2].LocalIndex();
                if (patched[start].opcode != OpCodes.Ldarg_0 || !patched[call + 1].IsStloc() ||
                    patched[resume].LocalIndex() != origin || patched[resume + 1].LocalIndex() != direction ||
                    !patched[resume + 2].IsLdloc() || patched[call + 1].LocalIndex() != patched[resume + 2].LocalIndex())
                    throw new Exception("Detector override no longer feeds its native endpoint calculation");
                if (patched[resume].labels.Count != 0)
                    throw new Exception("Native head/camera branch can bypass the hand-ray override");
                if (area && (patched[call + 4].opcode != OpCodes.Blt || !patched.Any(i => i.labels.Contains((Label)patched[call + 4].operand))))
                    throw new Exception("Native empty area sweep has no cleanup path");
                var retained = patched.Where((_, index) => index < start || index >= resume).ToArray();
                for (int i = 0; i < original.Count; i++)
                    if (i != start + 2 && (retained[i].opcode != original[i].opcode || !Equals(retained[i].operand, original[i].operand)))
                        throw new Exception("Native hit ordering, occlusion or use-object logic was modified");
                bool rejected = false;
                try { InteractionRayPatch.Transpiler(new[] { new CodeInstruction(OpCodes.Ret) }, target, generator).ToList(); }
                catch (InvalidOperationException) { rejected = true; }
                if (!rejected) throw new Exception("Unknown detector layout accepted silently");
            }

            var hitMethod = AccessTools.DeclaredMethod(typeof(MyCharacterRaycastDetectorComponent), "ProcessUseObjects");
            var hit = InteractionHitDistancePatch.Transpiler(PatchProcessor.GetOriginalInstructions(hitMethod), hitMethod).ToList();
            int conversion = hit.FindIndex(i => i.operand is MethodInfo method && method.DeclaringType == typeof(InteractionHitDistancePatch));
            if (conversion < 0 || hit[conversion + 1].opcode != OpCodes.Mul)
                throw new Exception("Use-object fractions no longer convert to distance along the native segment");

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
            var head = new Vector3D(1e6, -2e6, 3e6);
            var extended = MatrixD.Identity;
            extended.Translation = head + extended.Forward * (MyConstants.DEFAULT_INTERACTIVE_DISTANCE * 0.4);
            var bounded = HandInteraction.RayForPose(extended, head);
            if (Math.Abs(Vector3D.Distance(bounded.To, head) - MyConstants.DEFAULT_INTERACTIVE_DISTANCE) > 1e-7 ||
                Math.Abs(bounded.Length - 0.6 * MyConstants.DEFAULT_INTERACTIVE_DISTANCE) > 1e-7)
                throw new Exception("Extended hand changes native head-relative reach");
            log("PASS interaction ray: installed ray/area detector IL, native collision/action code retained, branch entry, unknown-layout rejection and shared beam geometry");
        }
    }
}
#endif
