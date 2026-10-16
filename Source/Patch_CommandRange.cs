using HarmonyLib;
using RimWorld;
using Verse;

namespace OdysseyTransportShuttleEnhanced
{
    /// <summary>
    /// 在机械师自己的可控范围之外，补上绑定飞船周围的一圈。
    ///
    /// 联动关系：原版 Pawn_MechanitorTracker.CanCommandTo 只认机械师脚下、半径 24.9（距离平方小于 620.01）的格子，
    /// 而且格子必须在机械师当前所在的地图上。征召机械族、下令和画出范围圈都走这个方法。
    /// 本补丁在原判定失败后再问 Hediff_MechanitorShuttleLink。飞船没生成、或不在这张地图上时，不加范围。
    /// 飞行途中飞船处于取消生成状态，这圈范围暂停，带宽不受影响。
    ///
    /// 注意：24.9 和 620.01 是原版私有常量，这里抄同一对数字，避免和机械师自己的圈差出一格。
    /// DrawCommandRadius 的后缀会再画一圈；谓词回调的是已经打过补丁的 CanCommandTo，不会反过来调用 Draw。
    /// </summary>
    [HarmonyPatch(typeof(Pawn_MechanitorTracker), nameof(Pawn_MechanitorTracker.CanCommandTo))]
    public static class Patch_CanCommandTo
    {
        public static void Postfix(Pawn_MechanitorTracker __instance, LocalTargetInfo target, ref bool __result)
        {
            if (__result)
            {
                return;
            }

            Pawn pawn = __instance.Pawn;
            Map map = pawn.MapHeld;
            if (map == null || !target.Cell.InBounds(map))
            {
                return;
            }

            Hediff_MechanitorShuttleLink link = Hediff_MechanitorShuttleLink.Of(pawn);
            if (link != null && link.Covers(map, target.Cell))
            {
                __result = true;
            }
        }
    }

    [HarmonyPatch(typeof(Pawn_MechanitorTracker), nameof(Pawn_MechanitorTracker.DrawCommandRadius))]
    public static class Patch_DrawCommandRadius
    {
        public static void Postfix(Pawn_MechanitorTracker __instance)
        {
            if (!__instance.Pawn.Spawned || !__instance.AnySelectedDraftedMechs)
            {
                return;
            }

            Hediff_MechanitorShuttleLink link = Hediff_MechanitorShuttleLink.Of(__instance.Pawn);
            link?.DrawRadii(__instance);
        }
    }
}
