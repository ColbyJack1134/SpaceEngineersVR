using System;
using System.Collections.Generic;
using Sandbox.Engine.Physics;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Character;
using Sandbox.ModAPI;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRageMath;

namespace SpaceEngineersVR.Player
{
    // Grab maths; the mod's server does the physics.
    internal static class GridGrab
    {
        internal const float MinDistance=1.2f,Torque=100000,DistanceRate=2,ThrowDistance=200,SteeringSpinDamping=.9f,AlignedSpinDamping=.3f,ClientSpinDamping=.3f;
        // The mod stiffens the pull below this hold distance when an offset is sent; the distance field stays above it and the offset carries the rest.
        internal const double StiffDistance=2;
        internal const int ContactLimit=200,ContactDrain=15,ContactRecovery=10,ContactSettle=20,ContactIdleGain=2,ThrowGround=-2;
        internal static readonly float AlignDeadzone=MathHelper.ToRadians(3);
        internal sealed class Hold
        {
            public MyCubeGrid Grid;
            public Vector3 Local;
            public float Distance;
            public bool SnapToCenter;
            // Pitch, yaw and roll demands in -1..1; the grid axes they turn are latched while any demand is held.
            public Vector3 Input;
            public Vector3[] Axes;
            public MatrixD? TurnTarget;
        }
        internal sealed class Contact { public int Counter=ContactLimit,Settle; }
        private static readonly List<IHitInfo> hits=new List<IHitInfo>();

        internal static bool WithinSize(Vector3I min,Vector3I max,float gridSize,double maxDimension)
        {
            var size=(Vector3)(max-min+Vector3I.One)*gridSize;
            return size.X<maxDimension && size.Y<maxDimension && size.Z<maxDimension;
        }
        internal static bool Grabbable(MyCubeGrid grid,double maxDimension) => grid!=null && !grid.Closed && !grid.MarkedForClose && !grid.IsStatic && !grid.IsPreview &&
            grid.Physics!=null && grid.Physics.Enabled && !grid.Physics.IsStatic && WithinSize(grid.Min,grid.Max,grid.GridSize,maxDimension);
        internal static float ClampDistance(double value,double reach) => (float)MathHelper.Clamp(value,MinDistance,reach);
        internal static Vector3D PulledPoint(Hold hold) => hold.SnapToCenter ? hold.Grid.Physics.CenterOfMassWorld:Vector3D.Transform(hold.Local,hold.Grid.WorldMatrix);
        internal static float SnapDistance(Vector3D point,Vector3D origin,Vector3D direction,double reach) => ClampDistance(Vector3D.Dot(point-origin,direction),reach);
        // The mod pulls toward head + forward * distance + offset. The offset moves that point onto the hand-ray target.
        internal static double PacketDistance(double distance) => Math.Max(distance,StiffDistance);
        internal static Vector3D Offset(MatrixD head,double packetDistance,Vector3D target) => target-(head.Translation+head.Forward*packetDistance);
        // The nearest grid under the ray. A rejected grid is described for the log.
        internal static bool TryPick(Vector3D origin,Vector3D direction,double reach,double maxDimension,out MyCubeGrid grid,out Vector3 local,out double distance,out string rejected)
        {
            grid=null; local=Vector3.Zero; distance=0; rejected=null;
            if(MyAPIGateway.Physics==null) return false;
            hits.Clear();
            MyAPIGateway.Physics.CastRay(origin,origin+direction*reach,hits);
            IHitInfo best=null; MyCubeGrid candidate=null;
            foreach(var hit in hits)
            {
                var entity=(hit?.HitEntity as MyEntity)?.GetTopMostParent() as MyCubeGrid;
                if(entity==null || best!=null && hit.Fraction>=best.Fraction) continue;
                best=hit; candidate=entity;
            }
            hits.Clear();
            if(best==null) return false;
            distance=Vector3D.Distance(origin,best.Position);
            if(!Grabbable(candidate,maxDimension))
            {
                rejected=(candidate.IsStatic ? "static":!WithinSize(candidate.Min,candidate.Max,candidate.GridSize,maxDimension) ? "over "+maxDimension+" m":"not grabbable")+" grid "+candidate.DisplayName;
                return false;
            }
            grid=candidate; local=(Vector3)Vector3D.Transform(best.Position,candidate.PositionComp.WorldMatrixNormalizedInv);
            return true;
        }
        internal static bool RayHits(Vector3D origin,Vector3D direction,double reach,MyCubeGrid grid) =>
            MyAPIGateway.Physics!=null && MyAPIGateway.Physics.CastRay(origin,origin+direction*reach,out IHitInfo hit) && (hit.HitEntity as MyEntity)?.GetTopMostParent()==grid;
        // The entity under the player's feet takes the reaction force; a flying player names none.
        internal static long Ground(MyCharacter character)
        {
            if(character==null || character.Closed || character.JetpackComp?.TurnedOn==true || MyAPIGateway.Physics==null) return -1;
            var up=character.WorldMatrix.Up; var position=character.PositionComp.GetPosition();
            return MyAPIGateway.Physics.CastRay(position+up,position-up,out IHitInfo hit,MyPhysics.CollisionLayers.VoxelCollisionLayer) && hit.HitEntity!=null ? hit.HitEntity.EntityId:-1;
        }
        // Grid axes turned by pitch, yaw and roll: the grid axis closest to the camera's right, down and forward, each used once.
        internal static Vector3[] ResolveAxes(MatrixD grid,MatrixD camera)
        {
            var local=new List<Vector3> {Vector3.Forward,Vector3.Up,Vector3.Right};
            var world=new List<Vector3D> {grid.Forward,grid.Up,grid.Right};
            var fits=new[] {camera.Right,camera.Down,camera.Forward};
            var result=new Vector3[3];
            for(int i=0;i<3;i++)
            {
                int best=0; double bestValue=double.MaxValue; int sign=1;
                for(int j=0;j<local.Count;j++)
                {
                    double dot=Vector3D.Dot(fits[i],world[j]);
                    if(1-Math.Abs(dot)<bestValue) { bestValue=1-Math.Abs(dot); best=j; sign=dot<0 ? -1:1; }
                }
                result[i]=local[best]*sign; local.RemoveAt(best); world.RemoveAt(best);
            }
            return result;
        }
        internal static Vector3 RotationTorque(Vector3[] axes,Vector3 input) => (axes[0]*input.X+axes[1]*input.Y+axes[2]*input.Z)*Torque;
        // Two-hand turn: the frame spanned by the fingertips, forward from one hand to the other.
        internal static MatrixD TurnFrame(Vector3D a,Vector3D b,Vector3D upA,Vector3D upB)
        {
            var forward=b-a;
            if(forward.LengthSquared()<1e-6) return MatrixD.Identity;
            forward.Normalize();
            var up=upA+upB; up-=forward*Vector3D.Dot(up,forward);
            if(up.LengthSquared()<1e-6) up=Vector3D.CalculatePerpendicularVector(forward);
            return MatrixD.CreateWorld(a,forward,Vector3D.Normalize(up));
        }
        // Row-vector convention: a world rotation W applied after the orientation gives orientation*W.
        internal static MatrixD TurnTarget(MatrixD startFrame,MatrixD currentFrame,MatrixD gridStart) =>
            gridStart.GetOrientation()*(MatrixD.Invert(startFrame.GetOrientation())*currentFrame.GetOrientation());
        // Body torque toward a target orientation; the error is the local rotation that maps the current orientation onto the target.
        internal static Vector3 AlignTorque(MatrixD orientation,MatrixD target,float mass)
        {
            var error=Quaternion.CreateFromRotationMatrix((Matrix)(target.GetOrientation()*MatrixD.Invert(orientation.GetOrientation())));
            if(error.W<0) error=-error;
            float angle=2*(float)Math.Acos(MathHelper.Clamp(error.W,-1,1));
            var axis=new Vector3(error.X,error.Y,error.Z);
            if(angle<=AlignDeadzone || axis.LengthSquared()<1e-8f) return Vector3.Zero;
            float massFactor=MathHelper.Clamp(1/(1+mass/1000),.5f,1);
            return Vector3.Normalize(axis)*Torque*massFactor;
        }
        // Body contact counter: drains while the held grid touches the player, recovers after a short settle, drops the grid at zero.
        internal static bool Step(Contact contact,bool holding,bool touching)
        {
            if(!holding) { contact.Counter=Math.Min(ContactLimit,contact.Counter+ContactIdleGain); contact.Settle=0; return false; }
            if(touching) { contact.Counter=Math.Max(0,contact.Counter-ContactDrain); contact.Settle=ContactSettle; return contact.Counter<=0; }
            if(contact.Settle>0) contact.Settle--; else contact.Counter=Math.Min(ContactLimit,contact.Counter+ContactRecovery);
            return false;
        }
    }
}
