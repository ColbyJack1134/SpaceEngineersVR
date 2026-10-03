using System;
using System.Collections.Generic;
using Sandbox.Engine.Physics;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.World;
using SpaceEngineersVR.Plugin;
using VRage.Game.ModAPI;
using VRageMath;

namespace SpaceEngineersVR.Player
{
    internal static class BodyLocomotion
    {
        private static readonly List<MyPhysics.HitInfo> hits = new List<MyPhysics.HitInfo>();
        private static bool disabled;

        public static void Update(MyCharacter character)
        {
            if (disabled || MySession.Static?.ControlledEntity != character || character.Physics?.CharacterProxy == null ||
                character.IsSitting || character.IsDead || !Player.Headset.pose.isTracked) return;
            try
            {
                CameraRig.Begin(character);
                if(ThirdPersonView.Character) return;
                if (Common.Config.UseHeadRotationForCharacter)
                {
                    MatrixD head=(MatrixD)Player.Headset.deviceToPlayer*CameraRig.Anchor;
                    MatrixD body=VrMath.Level(head,character.WorldMatrix.Up);
                    body.Translation=character.WorldMatrix.Translation;
                    if(Vector3D.DistanceSquared(body.Forward,character.WorldMatrix.Forward)>0.000001)
                    {
                        character.Physics.CharacterProxy.SetForwardAndUp(body.Forward,body.Up);
                        // Physics already owns the transform. Do not teleport its capsule to
                        // the animated/IK-offset visual position on every head turn.
                        character.PositionComp.SetWorldMatrix(ref body,character.Physics);
                    }
                }
                if (!Common.Config.RoomscaleMovement) return;
                Vector3 offset=Player.Headset.deviceToPlayer.Translation;
                offset.Y=0; // Physical crouching/leaning changes eye height, not the avatar's floor height.
                // A tracking reset must not become a large character teleport.
                if (offset.Length()>0.5f) { Player.ConsumeRoomscale(offset); return; }
                offset=VrMath.RoomscaleTravel(offset);
                if(offset.LengthSquared()<0.00000001f) return;
                MatrixD anchor=CameraRig.Anchor;
                Vector3D displacement=Vector3D.TransformNormal(offset,anchor);
                // Keep room walking tangent to the character's floor even if its vanilla camera is pitched.
                displacement-=character.WorldMatrix.Up*Vector3D.Dot(displacement,character.WorldMatrix.Up);
                if (displacement.LengthSquared()>1e-10) displacement=Vector3D.Normalize(displacement)*offset.Length();
                var shape=character.Physics.CharacterProxy.GetCollisionShape();
                if (shape.IsZero) return;
                MatrixD shapeWorld=character.Physics.GetWorldMatrix();
                shapeWorld.Translation+=Vector3D.TransformNormal(character.Physics.Center,shapeWorld);
                double fraction=Sweep(character,shape,shapeWorld,displacement);
                Vector3D accepted=displacement*fraction;
                // Horizontal sweeps alone cannot climb stair risers. Try up/across/down
                // with the same capsule, and require walkable support at the landing.
                if(fraction<0.999 && !((IMyCharacter)character).EnabledThrusts && !character.IsFalling && !character.IsJumping)
                {
                    Vector3D up=character.WorldMatrix.Up;
                    const double stepHeight=0.35;
                    if(Sweep(character,shape,shapeWorld,up*stepHeight)>0.999)
                    {
                        MatrixD raised=shapeWorld; raised.Translation+=up*stepHeight;
                        double across=Sweep(character,shape,raised,displacement);
                        if(across>fraction)
                        {
                            raised.Translation+=displacement*across;
                            double down=Sweep(character,shape,raised,-up*stepHeight);
                            bool supported=false;
                            foreach(var hit in hits)
                            {
                                var info=(IHitInfo)hit;
                                if(info.HitEntity!=character && Vector3D.Dot(info.Normal,up)>0.7 && info.Fraction<=down+0.005/stepHeight+0.0001) supported=true;
                            }
                            double rise=stepHeight*(1-down);
                            if(down<1 && supported && VrMath.HasStepRise(rise)) accepted=displacement*across+up*rise;
                        }
                    }
                }
                if(accepted.LengthSquared()>1e-10)
                {
                    // Base movement on the physics feet, not the render IK offset.
                    MatrixD physicsWorld=character.Physics.GetWorldMatrix();
                    physicsWorld.Translation+=accepted;
                    character.PositionComp.SetWorldMatrix(ref physicsWorld);
                }
                // Consume only the attempted excess, retaining the free head-sway radius.
                // Consume blocked travel too: blocked travel must not build up and lunge after leaving a wall.
                Player.ConsumeRoomscale(offset);
            }
            catch(Exception ex) { disabled=true; CameraRig.Reset(); Logger.Warning(ex,"Body/roomscale following disabled; stick locomotion remains available"); }
            finally { if(!disabled) CameraRig.End(character); }
        }

        private static double Sweep(MyCharacter character,Havok.HkShape shape,MatrixD from,Vector3D displacement)
        {
            hits.Clear();
            MyPhysics.CastShapeReturnContactBodyDatas(from.Translation+displacement,shape,ref from,
                MyPhysics.CollisionLayers.CharacterCollisionLayer,0,hits);
            double fraction=1;
            foreach(var hit in hits)
            {
                var info=(IHitInfo)hit;
                if(info.HitEntity==character || Vector3D.Dot(displacement,info.Normal)>=-0.000001) continue;
                fraction=Math.Min(fraction,Math.Max(0,info.Fraction-0.005/Math.Max(displacement.Length(),0.005)));
            }
            return fraction;
        }

        public static Vector3 RelativeMove(Vector3 move)
        {
            var source=Common.Config.ControllerRelativeMovement && Player.HandL.pose.isTracked ? Player.HandL.deviceToPlayer : Player.Headset.deviceToPlayer;
            var character=MySession.Static?.LocalCharacter;
            if(character==null || !CameraRig.Owns(character)) return VrMath.DirectedMove(move,source.Forward);
            Vector3D forward=Vector3D.TransformNormal(source.Forward,ThirdPersonView.Character ? ThirdPersonView.Current.Anchor : CameraRig.Anchor);
            Vector3 local=Vector3D.TransformNormal(forward,MatrixD.Transpose(character.WorldMatrix.GetOrientation()));
            return VrMath.DirectedMove(move,local);
        }
    }
}
