using System;
using System.Collections.Generic;
using Sandbox.Engine.Physics;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.Entities.Debris;
using Sandbox.Game.World;
using SpaceEngineersVR.Plugin;
using VRage.Game.Entity;
using VRageMath;

namespace SpaceEngineersVR.Player
{
    // Black when the head bone to eye line is blocked.
    internal static class ViewGuard
    {
        private static readonly List<MyPhysics.HitInfo> hits=new List<MyPhysics.HitInfo>();
        private static bool reported;
        private static object lastParent;
        private static DateTime settled;

        public static void Update()
        {
            float level=0;
            try
            {
                var character=MySession.Static?.LocalCharacter;
                // Seat changes teleport the body a tick before the rig re-anchors the eye.
                if(!ReferenceEquals(character?.Parent,lastParent)) { lastParent=character?.Parent; settled=DateTime.UtcNow.AddSeconds(.3); }
                if(Main.VrActive && Main.WorldAvailable && character!=null && !character.IsDead && !CameraRig.Detached && Player.Headset.pose.isTracked && DateTime.UtcNow>=settled)
                {
                    var seat=character.Parent as MyCockpit;
                    // The main camera lags the physics state by a frame; a seated eye is placed from the seat's current head matrix.
                    var eye=seat!=null ? ((MatrixD)VrMath.Affine(Player.Headset.pose.deviceToAbsolute.matrix*Player.PlayerToAbsolute.inverted)*seat.GetHeadMatrix(true,true)).Translation
                        :CameraRig.DeviceWorld(Player.Headset.pose.deviceToAbsolute.matrix).Translation;
                    var head=character.GetHeadMatrix(true,true,forceHeadBone:true).Translation;
                    double near=MySector.MainCamera?.NearPlaneDistance ?? .05;
                    double distance=Vector3D.Distance(head,eye);
                    if(distance<1.2)
                    {
                        double blocked=Blocked(head,eye,character,seat?.SlimBlock);
                        level=(float)MathHelper.Clamp((distance+near-blocked)/near,0,1);
                    }
                }
            }
            catch(Exception ex)
            {
                level=1;
                if(!reported) { reported=true; Logger.Warning(ex,"View guard failed; the view stays black while it cannot check the eye"); }
            }
            ViewFade.Set(ViewFade.Source.Wall,level,level>0 ? .25f:.15f);
        }
        // Vanilla's camera filter; back faces are exits from a shape the line started inside.
        internal static double Blocked(Vector3D from,Vector3D to,MyCharacter character,MySlimBlock ignoredBlock)
        {
            var delta=to-from; double length=delta.Length();
            if(length<1e-6) return double.PositiveInfinity;
            var direction=(Vector3)(delta/length);
            hits.Clear(); MyPhysics.CastRay(from,to,hits);
            double nearest=double.PositiveInfinity;
            foreach(var hit in hits)
            {
                var info=hit.HkHitInfo;
                if(Vector3.Dot(info.Normal,direction)>0) continue;
                if(Ignored(info.GetHitEntity() as MyEntity,info.Body,info.GetShapeKey(0),character,ignoredBlock)) continue;
                nearest=Math.Min(nearest,info.HitFraction*length);
            }
            return nearest;
        }
        private static bool Ignored(MyEntity entity,Havok.HkRigidBody rigidBody,uint shapeKey,MyCharacter character,MySlimBlock ignoredBlock)
        {
            var body=rigidBody?.UserObject as MyPhysicsBody;
            if(entity==null || body==null || body.IsPhantom || entity==character || entity is MyCharacter || entity is MyFloatingObject ||
                entity is MyDebrisBase || entity is MySafeZone || entity is Sandbox.Game.Weapons.MyAutomaticRifleGun || entity is Sandbox.Game.Weapons.MyEngineerToolBase || entity is Sandbox.Game.Weapons.MyHandDrill) return true;
            if(character.Ladder!=null && entity.GetTopMostParent()==character.Ladder.GetTopMostParent()) return true;
            var grid=entity as MyCubeGrid;
            return ignoredBlock!=null && grid==ignoredBlock.CubeGrid && grid.Physics?.Shape.GetBlockFromShapeKey(shapeKey)==ignoredBlock;
        }
    }
}
