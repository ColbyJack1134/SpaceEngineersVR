using System;
using System.Collections.Generic;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.World;
using Sandbox.ModAPI;
using SpaceEngineersVR.Plugin;
using VRage.Game.Entity.UseObject;
using VRageMath;

namespace SpaceEngineersVR.Player
{
    internal static class BlockActions
    {
        internal static bool CharacterAvailable(Sandbox.Game.Entities.Character.MyCharacter character) =>
            character!=null && MySession.Static?.LocalCharacter==character && MySession.Static.ControlledEntity==character &&
            !character.IsDead && !character.IsSitting && !ThirdPersonView.Active && Player.HandR.pose.isTracked;

        internal static bool Available(IMyUseObject target,Sandbox.Game.Entities.Character.MyCharacter character,UseActionEnum action)
        {
            if(!CharacterAvailable(character) || target?.Owner==null || target.Owner.Closed || (target.SupportedActions & action)!=action) return false;
            if(target.Owner is IMyTerminalBlock block && !block.HasLocalPlayerAccess()) return false;
            var activation=target.ActivationMatrix;
            if(!activation.IsValid() || Math.Abs(activation.Determinant())<1e-12 || !HandInteraction.TryWorldPose(Player.HandR,out var pointer)) return false;
            if(TrackedArms.TryFreePointPose(Player.HandR,out var finger)) pointer=finger;
            return InReach(pointer,HandInteraction.ClosestControlPoint(activation,pointer.Translation),target.InteractiveDistance);
        }

        internal static bool InReach(MatrixD pointer,Vector3D point,float reach)
        {
            float distance=(float)Vector3D.Distance(pointer.Translation,point);
            if(distance>reach) return false;
            var direction=point-pointer.Translation;
            if(distance>.001f)
            {
                direction.Normalize();
                var ray=MatrixD.CreateWorld(pointer.Translation,direction,pointer.Up);
                if(HandInteraction.ObstacleDistance(ray,distance)+.025f<distance) return false;
            }
            return true;
        }

        internal static ActionChoice[] Capture(bool plannerOnly=false)
        {
            var target=HandInteraction.CaptureRightTarget();
            var character=MySession.Static?.LocalCharacter;
            var choices=new List<ActionChoice>();
            if(target!=null && !plannerOnly) choices.AddRange(Choices(target.SupportedActions,target is MyUseObjectTextPanel,
                action=>Available(target,character,action),action=>
            {
                if(!Main.VrActive || !InputRouter.Gameplay || Main.MenuOpen || !Available(target,character,action)) return;
                target.Use(action,character);
            }));
            choices.AddRange(BuildPlannerActions.Capture(target,character));
            return choices.ToArray();
        }
        internal static ActionChoice[] Choices(UseActionEnum supported,bool textPanel,Func<UseActionEnum,bool> available,Action<UseActionEnum> use)
        {
            var choices=new List<ActionChoice>();
            void Add(UseActionEnum action,string label,bool opensMenu,string icon)
            {
                if((supported & action)!=action) return;
                choices.Add(new ActionChoice(label,()=> { if(available(action)) use(action); },opensMenu,icon,()=>available(action)));
            }
            Add(UseActionEnum.Manipulate,textPanel ? "Edit text":"Use",textPanel,NativeSprites.Hud("RadialMenu"));
            Add(UseActionEnum.OpenTerminal,"Control panel",true,GameActions.TerminalAction.Icon);
            Add(UseActionEnum.OpenInventory,"Block inventory",true,GameActions.InventoryAction.Icon);
            return choices.ToArray();
        }
        internal static ActionChoice[][] Pages(ActionChoice[] block,ActionChoice[][] existing)
        {
            var pages=new List<ActionChoice[]>();
            if(block.Length>0) pages.AddRange(BlockVariants.Pages(Array.Empty<ActionChoice>(),block));
            pages.AddRange(existing);
            return pages.ToArray();
        }
    }
}
