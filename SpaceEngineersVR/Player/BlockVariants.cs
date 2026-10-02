using System;
using System.Linq;
using HarmonyLib;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.Game.World;

namespace SpaceEngineersVR.Player
{
    internal static class BlockVariants
    {
        private static readonly Type nativeType=AccessTools.TypeByName("Sandbox.Game.Screens.Helpers.MyRadialMenuItemCubeBlock");
        private static readonly System.Reflection.MethodInfo enabled=AccessTools.Method(nativeType,"IsBlockEnabled");
        private static readonly System.Reflection.MethodInfo activate=AccessTools.Method(typeof(MyCubeBuilder),"UpdateCubeBlockStageDefinition");
        internal static bool Available(MyCubeBlockDefinition block) => block!=null &&
            (!MySession.Static.SurvivalMode || block.AvailableInSurvival) &&
            Convert.ToInt32(enabled.Invoke(null,new object[] {block,null}))==0;
        internal static MyCubeBlockDefinition[] Family(MyCubeBlockDefinition current,System.Collections.Generic.IEnumerable<MyCubeBlockDefinition> stages) =>
            (current.BlockVariantsGroup?.Blocks ?? stages.ToArray()).Where(b=>b.CubeSize==current.CubeSize).GroupBy(b=>b.Id).Select(g=>g.First()).ToArray();
        internal static ActionChoice[] Choices()
        {
            var builder=MyCubeBuilder.Static;
            var current=builder?.CurrentBlockDefinition;
            if(current==null) return new ActionChoice[0];
            var stages=builder.CubeBuilderState.CurrentBlockDefinitionStages;
            var variants=Family(current,stages);
            if(variants.Select(b=>b.Id).Distinct().Count()<2) return new ActionChoice[0];
            return variants.Where(b=>b.CubeSize==current.CubeSize).Select(block=>new ActionChoice(block.DisplayNameText,()=> {
                if(MyCubeBuilder.Static!=builder || !builder.IsActivated || !Available(block)) return;
                if(builder.CurrentBlockDefinition?.Id==block.Id) return;
                var pair=MyDefinitionManager.Static.GetDefinitionGroup(block.BlockPairName);
                builder.CubeBuilderState.SetCurrentBlockForBlockVariantGroup(pair);
                activate.Invoke(builder,new object[] {block});
            },icon:block.Icons?.FirstOrDefault(),enabled:()=>Available(block))).ToArray();
        }
        internal static ActionChoice[][] Pages(ActionChoice[] variants,ActionChoice[] actions)
        {
            var pages=new System.Collections.Generic.List<ActionChoice[]>();
            foreach(var source in new[] {variants,actions})
                for(int offset=0;offset<source.Length;offset+=9)
                {
                    var page=new ActionChoice[9];
                    Array.Copy(source,offset,page,0,Math.Min(9,source.Length-offset)); pages.Add(page);
                }
            return pages.ToArray();
        }
    }
}
