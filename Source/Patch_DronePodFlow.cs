using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace OdysseyTransportShuttleEnhanced
{
    /// <summary>
    /// 只改太空无人机和机械师飞船的目的地菜单。原版穿梭机、运输舱不进这里。
    ///
    /// 联动关系：原版 GetTransportersFloatMenuOptionsAt 在 isShuttle 为真时只给穿梭机选项，为假时只给运输舱选项。
    /// 这两个载具改成三段，互不叠加：世界物体能给出穿梭机选项就只用那些；否则只给赠送（交易交接）；
    /// 赠送也不成立，才列出运输舱的结队、空投、袭击和「内容将遗失」。
    /// 只有机械族驾驶机械师飞船时不加赠送，也不走运输舱。穿梭机选项没有时，只给「带着全部内容返回」。
    /// 返程油料确认仍由 ChoseWorldTarget 根据 CompShuttle 处理，这里不改。
    /// </summary>
    [HarmonyPatch(typeof(CompLaunchable), "GetTransportersFloatMenuOptionsAt")]
    public static class Patch_DronePodMenu
    {
        public static bool Prefix(CompLaunchable __instance, PlanetTile tile, Action<PlanetTile, TransportersArrivalAction> launchAction, ref IEnumerable<FloatMenuOption> __result)
        {
            if (!ShuttleArrivalCascade.AppliesTo(__instance))
            {
                return true;
            }

            CompTransporter transporter = __instance.parent.GetComp<CompTransporter>();
            if (transporter == null || __instance.parent.MapHeld == null)
            {
                return true;
            }

            __result = ShuttleArrivalCascade.Options(__instance, tile, launchAction);
            return false;
        }
    }

    /// <summary>
    /// 起飞时按已经选中的抵达动作决定要不要把载具从货物里摘出来。
    ///
    /// 穿梭机流程（降落、访问、交易）不包装，载具跟着原版穿梭机动作落地或结成远行队。
    /// 太空无人机的交易交接和空投舱都会包进 DronePod：货物照该动作处理，飞机空载返回。
    /// 有机械师的机械师飞船只有交易交接才包装并空载返回。起飞时不再播报已经废弃的「放下货物，正在返回」。只有机械族时不包装赠送；
    /// 投送失败的动作是 TransportersArrivalAction_MechShuttleAbort，抵达后连货物和机械族一起返航。
    /// </summary>
    [HarmonyPatch(typeof(CompLaunchable), nameof(CompLaunchable.TryLaunch))]
    public static class Patch_DroneTryLaunch
    {
        public static void Prefix(CompLaunchable __instance, ref TransportersArrivalAction arrivalAction)
        {
            string defName = __instance.parent.def.defName;
            if (defName == "TransportDrone")
            {
                if (arrivalAction is TransportersArrivalAction_DronePod || ShuttleArrivalCascade.IsShuttleFlow(arrivalAction))
                {
                    return;
                }

                arrivalAction = new TransportersArrivalAction_DronePod(arrivalAction, __instance.parent.Tile);
                return;
            }

            if (defName != "MechanitorShuttle" || ShuttleArrivalCascade.IsShuttleFlow(arrivalAction) || arrivalAction is not TransportersArrivalAction_GiveGift || arrivalAction is TransportersArrivalAction_DronePod)
            {
                return;
            }

            arrivalAction = new TransportersArrivalAction_DronePod(arrivalAction, __instance.parent.Tile, "MechanitorShuttle", null, "OTS.MechShuttleReturned", "OTS.MechShuttleLowFuel");
        }
    }

    /// <summary>
    /// 两个载具共用的目的地分段。调用方是上面的菜单补丁和起飞补丁。
    /// </summary>
    public static class ShuttleArrivalCascade
    {
        public static bool AppliesTo(CompLaunchable launchable)
        {
            string defName = launchable?.parent?.def?.defName;
            return defName == "TransportDrone" || defName == "MechanitorShuttle";
        }

        public static bool IsShuttleFlow(TransportersArrivalAction action)
        {
            return action is TransportersArrivalAction_LandInSpecificCell
                || action is TransportersArrivalAction_VisitSettlement
                || action is TransportersArrivalAction_TransportShip;
        }

        public static IEnumerable<FloatMenuOption> Options(CompLaunchable launchable, PlanetTile tile, Action<PlanetTile, TransportersArrivalAction> launchAction)
        {
            CompTransporter transporter = launchable.parent.GetComp<CompTransporter>();
            List<CompTransporter> pods = transporter.TransportersInGroup(launchable.parent.MapHeld);
            List<FloatMenuOption> shuttle = ShuttleOptions(pods, tile, launchAction);
            if (shuttle.Count > 0)
            {
                for (int i = 0; i < shuttle.Count; i++)
                {
                    yield return shuttle[i];
                }

                yield break;
            }

            if (MechanitorShuttleCrew.OnlyMechanoids(launchable))
            {
                if (!Find.World.Impassable(tile))
                {
                    yield return new FloatMenuOption("OTS.MechShuttleAbortOption".Translate(), delegate
                    {
                        launchAction(tile, new TransportersArrivalAction_MechShuttleAbort(launchable.parent.Tile));
                    });
                }

                yield break;
            }

            List<FloatMenuOption> trade = TradeHandoverOptions(pods, tile, launchAction);
            if (trade.Count > 0)
            {
                for (int i = 0; i < trade.Count; i++)
                {
                    yield return trade[i];
                }

                yield break;
            }

            foreach (FloatMenuOption option in PodOptions(pods, tile, launchAction))
            {
                yield return option;
            }
        }

        private static List<FloatMenuOption> ShuttleOptions(List<CompTransporter> pods, PlanetTile tile, Action<PlanetTile, TransportersArrivalAction> launchAction)
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>();
            List<WorldObject> worldObjects = Find.WorldObjects.AllWorldObjects;
            for (int i = 0; i < worldObjects.Count; i++)
            {
                if (worldObjects[i].Tile != tile)
                {
                    continue;
                }

                options.AddRange(worldObjects[i].GetShuttleFloatMenuOptions(pods, launchAction));
            }

            return options;
        }

        private static List<FloatMenuOption> TradeHandoverOptions(List<CompTransporter> pods, PlanetTile tile, Action<PlanetTile, TransportersArrivalAction> launchAction)
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>();
            List<WorldObject> worldObjects = Find.WorldObjects.AllWorldObjects;
            for (int i = 0; i < worldObjects.Count; i++)
            {
                if (worldObjects[i].Tile != tile || worldObjects[i] is not Settlement settlement)
                {
                    continue;
                }

                options.AddRange(TransportersArrivalAction_GiveGift.GetFloatMenuOptions(launchAction, pods, settlement));
            }

            return options;
        }

        private static IEnumerable<FloatMenuOption> PodOptions(List<CompTransporter> pods, PlanetTile tile, Action<PlanetTile, TransportersArrivalAction> launchAction)
        {
            bool anything = false;
            if (TransportersArrivalAction_FormCaravan.CanFormCaravanAt(pods, tile) && !Find.WorldObjects.AnySettlementBaseAt(tile) && !Find.WorldObjects.AnySiteAt(tile))
            {
                anything = true;
                yield return new FloatMenuOption("FormCaravanHere".Translate(), delegate
                {
                    launchAction(tile, new TransportersArrivalAction_FormCaravan("MessageTransportPodsArrived"));
                });
            }

            List<WorldObject> worldObjects = Find.WorldObjects.AllWorldObjects;
            for (int i = 0; i < worldObjects.Count; i++)
            {
                if (worldObjects[i].Tile != tile)
                {
                    continue;
                }

                foreach (FloatMenuOption option in worldObjects[i].GetTransportersFloatMenuOptions(pods, launchAction))
                {
                    anything = true;
                    yield return option;
                }
            }

            if (!anything && !Find.World.Impassable(tile))
            {
                yield return new FloatMenuOption("TransportPodsContentsWillBeLost".Translate(), delegate
                {
                    launchAction(tile, null);
                });
            }
        }
    }
}
