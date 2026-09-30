using System.Linq;
using Sandbox.Game.Entities.Character;
using SpaceEngineersVR.Config;
using SpaceEngineersVR.Plugin;
using VRageMath;

namespace SpaceEngineersVR.Player
{
    internal static class Alignment
    {
        public static string HandKey(Controller hand) => "hand/"+(hand==Player.HandL ? "left/" : "right/")+hand.ProfileName;
        public static string ToolKey(MyCharacter character)
        {
            var weapon=character?.CurrentWeapon;
            // The block placer is a weapon too, but has no physical inventory item.
            if(weapon?.PhysicalObject==null) return null;
            return "tool/"+weapon.PhysicalObject.SubtypeName+"/"+
                ((weapon as VRage.Game.Entity.MyEntity)?.Model?.AssetName ?? "").Replace('\\','/');
        }
        public static string SeatKey(string part) => "cockpit/"+SeatFit.Seat.BlockDefinition.Id.SubtypeName+"/"+part;
        public const string WristKey="wrist/display";
        public static AnchorOffsetSetting Get(string key) =>
            Common.Config?.AnchorOffsets.FirstOrDefault(x=>x!=null && x.Key==key && x.Valid) ?? new AnchorOffsetSetting { Key=key };
        public static MatrixD Apply(string key,MatrixD frame) => (MatrixD)Get(key).Matrix*frame;
        public static float Scale(string key) => Get(key).Scale;
    }
}
