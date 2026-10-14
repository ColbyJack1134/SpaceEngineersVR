using System;
using System.Collections.Generic;
using HarmonyLib;
using Havok;
using Sandbox.Engine.Physics;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.World;
using SpaceEngineersVR.Player;
using SpaceEngineersVR.Plugin;
using VRage.Game;
using VRage.Game.Entity;
using VRageMath;

namespace SpaceEngineersVR.Patches
{
    // The engine stands a body up to gravity by 0.04 rad a step about the capsule centre. Finish the turn at once about the
    // same pivot; the host applies the sent rotation like any other turn. A pose overlapping a grid or voxel keeps the slow
    // turn, because an overlapping capsule disables client prediction.
    [HarmonyPatch(typeof(MyCharacter),"UpdateStandup")]
    internal static class StandUprightPatch
    {
        private static bool disabled;
        private static readonly List<HkBodyCollision> hits=new List<HkBodyCollision>();

        private static void Postfix(MyCharacter __instance,ref Vector3 gravity,ref Vector3 chUp,ref Vector3 chForward)
        {
            if(disabled || !Main.VrActive || __instance!=MySession.Static?.LocalCharacter || __instance.IsMagneticBootsEnabled ||
                __instance.Definition.RotationToSupport!=MyEnumCharacterRotationToSupport.None) return;
            var proxy=__instance.Physics?.CharacterProxy;
            if(proxy==null || gravity.LengthSquared()<1e-4f || chUp.LengthSquared()<1e-8f) return;
            try
            {
                MatrixD upright=VrMath.Level(MatrixD.CreateWorld(Vector3D.Zero,chForward,chUp),-gravity);
                if(Vector3D.Dot(upright.Up,Vector3D.Normalize(chUp))>Math.Cos(.04)) return;
                var shape=proxy.GetCollisionShape();
                if(shape.IsZero) return;
                MatrixD world=__instance.Physics.GetWorldMatrix();
                Vector3D centre=world.Translation+Vector3D.TransformNormal(__instance.Physics.Center,world);
                Quaternion rotation=Quaternion.CreateFromRotationMatrix(upright);
                hits.Clear();
                MyPhysics.GetPenetrationsShape(shape,ref centre,ref rotation,hits,MyPhysics.CollisionLayers.CollisionLayerWithoutCharacter);
                foreach(var hit in hits)
                {
                    var entity=(hit.Body?.UserObject as MyPhysicsBody)?.Entity;
                    if(entity is MyCubeGrid || entity is MyVoxelBase) return;
                }
                chForward=upright.Forward;
                chUp=upright.Up;
            }
            catch(Exception ex) { disabled=true; Logger.Warning(ex,"Instant stand-up disabled; the engine turns the body upright"); }
        }
    }
}
