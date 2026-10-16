using System;
using System.Collections.Generic;
using System.Globalization;
using Sandbox.Game.Entities;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRageMath;

namespace SpaceEngineersVR.Multiplayer
{
    // Spring-damper hold of a loose grid at the grabbed point. Forces run where physics is authoritative: the host applies
    // them for every VR player from the replicated hand poses, single player applies them locally.
    internal static class GridGrab
    {
        internal const int MaxBlocksPerAxis=20;
        internal const float StandingForce=20000,FlyingForce=10000;
        // Spring saturates at Reach from the hand; a hand further than Slip from the grab point lets go.
        internal const float Reach=.25f,Slip=.6f,TouchDistance=.05f,GrabRange=3;
        internal sealed class Hold
        {
            public MyCubeGrid Grid;
            public Vector3 Local;
            public Vector3D LastPalm;
            public bool HasPalm;
            public float Saturation;
            public Vector3D Point => Vector3D.Transform(Local,Grid.WorldMatrix);
        }
        internal sealed class Holder { public readonly Hold[] Hands=new Hold[2]; public Hold Left { get => Hands[0]; set => Hands[0]=value; } public Hold Right { get => Hands[1]; set => Hands[1]=value; } }
        private static readonly List<MyEntity> nearby=new List<MyEntity>();

        internal static bool WithinSize(Vector3I min,Vector3I max)
        {
            var size=max-min+Vector3I.One;
            return size.X<=MaxBlocksPerAxis && size.Y<=MaxBlocksPerAxis && size.Z<=MaxBlocksPerAxis;
        }
        internal static bool Grabbable(MyCubeGrid grid) => grid!=null && !grid.Closed && !grid.MarkedForClose && !grid.IsStatic && !grid.IsPreview &&
            grid.Physics!=null && grid.Physics.Enabled && !grid.Physics.IsStatic && WithinSize(grid.Min,grid.Max);
        // The palm centre within TouchDistance of a block's cell box.
        internal static bool TryFind(Vector3D palm,out MyCubeGrid grid,out Vector3 local)
        {
            grid=null; local=Vector3.Zero;
            var sphere=new BoundingSphereD(palm,TouchDistance+1.5);
            nearby.Clear();
            MyGamePruningStructure.GetAllEntitiesInSphere(ref sphere,nearby);
            double nearest=TouchDistance;
            foreach(var entity in nearby)
            {
                if(!(entity is MyCubeGrid candidate) || !Grabbable(candidate)) continue;
                var point=Vector3D.Transform(palm,candidate.PositionComp.WorldMatrixNormalizedInv);
                var cell=candidate.WorldToGridInteger(palm);
                for(int x=-1;x<=1;x++) for(int y=-1;y<=1;y++) for(int z=-1;z<=1;z++)
                {
                    var block=candidate.GetCubeBlock(cell+new Vector3I(x,y,z));
                    if(block==null) continue;
                    var box=new BoundingBoxD(((Vector3D)block.Min-Vector3D.Half)*candidate.GridSize,((Vector3D)block.Max+Vector3D.Half)*candidate.GridSize);
                    double distance=box.Distance(point);
                    if(distance>=nearest) continue;
                    nearest=distance; grid=candidate; local=(Vector3)point;
                }
            }
            nearby.Clear();
            return grid!=null;
        }
        internal static float ForceCap(bool flying) => flying ? FlyingForce:StandingForce;
        // Critically damped pull toward the hand, clamped to the cap; saturation is the unclamped demand over the cap.
        internal static Vector3 Spring(Vector3 error,Vector3 relativeVelocity,float mass,float cap,out float saturation)
        {
            saturation=0;
            if(!error.IsValid() || !relativeVelocity.IsValid() || cap<=0) return Vector3.Zero;
            float stiffness=cap/Reach;
            float damping=2*(float)Math.Sqrt(stiffness*Math.Max(1,mass));
            var force=error*stiffness-relativeVelocity*Math.Min(damping,cap/2);
            float length=force.Length();
            saturation=length/cap;
            return length>cap ? force*(cap/length):force;
        }
        // Returns false when the hold ended (grid gone, or the hand slipped off).
        internal static bool Apply(Hold hold,Vector3D palm,bool flying,float dt,bool otherHandHolds)
        {
            var grid=hold.Grid;
            if(!Grabbable(grid)) return false;
            var point=hold.Point;
            if(Vector3D.Distance(point,palm)>Slip) return false;
            var handVelocity=hold.HasPalm && dt>0 ? (palm-hold.LastPalm)/dt:Vector3D.Zero;
            hold.LastPalm=palm; hold.HasPalm=true;
            var physics=grid.Physics;
            var relative=(Vector3)(physics.GetVelocityAtPoint(point)-handVelocity);
            var force=Spring((Vector3)(palm-point),relative,physics.Mass,ForceCap(flying),out hold.Saturation);
            // One hand cannot steer spin; damp it so the grid does not twirl on the hand. Two hands steer through their two springs.
            Vector3? torque=otherHandHolds ? (Vector3?)null:-physics.AngularVelocity*Math.Min(physics.Mass,ForceCap(flying)/10)*.5f;
            physics.AddForce(MyPhysicsForceType.APPLY_WORLD_FORCE,force,point,torque);
            return true;
        }
        internal static string Encode(bool left,Vector3 local) =>
            string.Join(";",(left ? "L":"R"),local.X.ToString("R",CultureInfo.InvariantCulture),local.Y.ToString("R",CultureInfo.InvariantCulture),local.Z.ToString("R",CultureInfo.InvariantCulture));
        internal static string EncodeRelease(bool left) => left ? "L":"R";
        internal static bool Decode(string value,out bool left,out Vector3? local)
        {
            left=false; local=null;
            if(string.IsNullOrEmpty(value) || value.Length>80) return false;
            var parts=value.Split(';');
            if(parts.Length!=1 && parts.Length!=4 || parts[0]!="L" && parts[0]!="R") return false;
            left=parts[0]=="L";
            if(parts.Length==1) return true;
            var point=new Vector3();
            if(!float.TryParse(parts[1],NumberStyles.Float,CultureInfo.InvariantCulture,out point.X) || !float.TryParse(parts[2],NumberStyles.Float,CultureInfo.InvariantCulture,out point.Y) ||
                !float.TryParse(parts[3],NumberStyles.Float,CultureInfo.InvariantCulture,out point.Z) || !point.IsValid() || point.Length()>60) return false;
            local=point; return true;
        }
    }
}
