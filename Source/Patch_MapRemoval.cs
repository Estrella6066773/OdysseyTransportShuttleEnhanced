using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace OdysseyTransportShuttleEnhanced
{
    /// <summary>
    /// 停着或正落着玩家载具的地图，不因为「地图上没有玩家单位」被自动卸载。
    ///
    /// 联动关系：MapParent.TickInterval 每帧调 CheckRemoveMapNow，MapParent.ShouldRemoveMapNow 先用
    /// Map.AnyBuildingBlockingMapRemoval 挡一道。原版这一道只认逆重锚和逆重引擎，
    /// 所以太空无人机、机械师飞船空载降落到新生成的地图后，没有小人压着，地图会在下一次检查时被卸载，
    /// 飞机和船上的货物跟着一起销毁。这里把这两种载具算作阻挡物。
    ///
    /// 落体中也要算：载具落地是先在图上生成下落体（TransportDroneIncoming 等），再等它砸地。
    /// 只认已落地的载具会漏掉下落这一段，地图仍然会在下落期间被卸载。所以下落和飞离的四种下落体也列进来。
    ///
    /// 只在 property 原本为假时补判，不改动其它地图；地图原本就不会被移除的（玩家基地、有小人、有逆重锚）
    /// 直接沿用原结果。查询走 listerThings.AnyThingWithDef，是按 def 的哈希表，不遍历全图。
    ///
    /// 目标方法用 AccessTools.PropertyGetter 取，避免属性名和 get_ 前缀拼错的歧义。
    /// 注意：这条补丁只挡自动卸载。玩家主动放弃地图、任务逻辑直接 DeinitAndRemoveMap 的路径不受影响。
    /// 也正因为如此，载具停在地图上时地图会一直留着，玩家把载具飞走之后才会按原版规则卸载。
    /// </summary>
    [HarmonyPatch]
    public static class Patch_AnyBuildingBlockingMapRemoval
    {
        private static readonly string[] BlockingDefNames =
        {
            "TransportDrone",
            "TransportDroneIncoming",
            "TransportDroneLeaving",
            "MechanitorShuttle",
            "MechanitorShuttleIncoming",
            "MechanitorShuttleLeaving"
        };

        private static List<ThingDef> blockingDefs;

        public static MethodBase TargetMethod()
        {
            return AccessTools.PropertyGetter(typeof(Map), "AnyBuildingBlockingMapRemoval");
        }

        public static void Postfix(Map __instance, ref bool __result)
        {
            if (__result)
            {
                return;
            }

            List<ThingDef> defs = ResolveDefs();
            for (int i = 0; i < defs.Count; i++)
            {
                if (__instance.listerThings.AnyThingWithDef(defs[i]))
                {
                    __result = true;
                    return;
                }
            }
        }

        private static List<ThingDef> ResolveDefs()
        {
            if (blockingDefs != null)
            {
                return blockingDefs;
            }

            blockingDefs = new List<ThingDef>();
            for (int i = 0; i < BlockingDefNames.Length; i++)
            {
                ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(BlockingDefNames[i]);
                if (def != null)
                {
                    blockingDefs.Add(def);
                }
            }

            return blockingDefs;
        }
    }
}
