using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace OdysseyTransportShuttleEnhanced
{
    /// <summary>
    /// 返程迫降要砸开飞船自己占地上面的屋顶。下落体撞屋顶时原版只处理下落体占地，
    /// 这两艘飞船的下落体尺寸和建筑尺寸不一致，只靠原版会留下一条屋顶。
    ///
    /// 联动关系：ShipJob_Arrive 生成下落体，Skyfaller 在落地前调用 HitRoof。
    /// 本补丁在那之后，按舱内太空无人机或机械师飞船的朝向和尺寸再砸一次。
    /// 其他下落体直接返回。厚山屋顶不砸，选点时已经避开，这里再挡一层。
    /// 燃料充足时落在没有屋顶的格子上，这里不会拆到东西。
    /// </summary>
    [HarmonyPatch(typeof(Skyfaller), "HitRoof")]
    public static class Patch_ShuttleHitRoof
    {
        public static void Postfix(Skyfaller __instance)
        {
            if (__instance?.innerContainer == null || __instance.Map == null)
            {
                return;
            }

            Thing shuttle = null;
            for (int i = 0; i < __instance.innerContainer.Count; i++)
            {
                Thing thing = __instance.innerContainer[i];
                if (thing != null && (thing.def.defName == "TransportDrone" || thing.def.defName == "MechanitorShuttle"))
                {
                    shuttle = thing;
                    break;
                }
            }

            if (shuttle == null)
            {
                return;
            }

            Map map = __instance.Map;
            Rot4 rot = shuttle.Rotation.IsValid ? shuttle.Rotation : shuttle.def.defaultPlacingRot;
            List<IntVec3> cells = new List<IntVec3>();
            foreach (IntVec3 cell in GenAdj.OccupiedRect(__instance.Position, rot, shuttle.def.size))
            {
                if (!cell.InBounds(map))
                {
                    continue;
                }

                RoofDef roof = cell.GetRoof(map);
                if (roof == null || roof.isThickRoof)
                {
                    continue;
                }

                cells.Add(cell);
            }

            if (cells.Count > 0)
            {
                RoofCollapserImmediate.DropRoofInCells(cells, map);
            }
        }
    }
}
