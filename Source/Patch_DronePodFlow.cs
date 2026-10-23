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
    /// 太空无人机载人时：第一段补上穿梭机降落或穿梭机式结队，第二段赠送整段去掉（赠送会把船上的人一起交出去），
    /// 第三段照旧保留，别的模组加的落地动作就在这一段里。
    /// 是否按货运处理写在 IsCargoDisposal，落地型动作一律不包装，由动作自己决定怎么落地。
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
    /// 联动关系：起飞时按已经选中的抵达动作决定要不要把载具从货物里摘出来。
    ///
    /// 只有会把货舱当货物处理的动作才包装：赠送、交给远行队、袭击定居点、运输舱式落地。
    /// 落地型动作一律不包装，由动作自己决定怎么落地——原版的访问、结队，以及别的模组加的「选择落点降落」都在这一类。
    /// 包装会先把载具从货舱摘走，落地型动作随后找不到载具，只好把载具当普通货物丢进空投舱，飞机就此丢失。
    ///
    /// 载人时另有两道保险：赠送整段不提供（在菜单里去掉）；运输舱式落地改写成穿梭机式落地，人随飞机停在地图上。
    /// 万一仍然拿到纯货运动作，改成带着全部内容返回，不交货也不丢人。
    /// 有机械师的机械师飞船只有交易交接才包装并空载返回。只有机械族时不包装赠送；
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
                if (arrivalAction is TransportersArrivalAction_DronePod)
                {
                    return;
                }

                bool crewAboard = ShuttleArrivalCascade.CrewAboard(__instance);
                if (!ShuttleArrivalCascade.IsCargoDisposal(arrivalAction, crewAboard))
                {
                    return;
                }

                if (crewAboard)
                {
                    TransportersArrivalAction shuttleLanding = ShuttleArrivalCascade.AsShuttleLanding(__instance, arrivalAction);
                    if (shuttleLanding != null)
                    {
                        arrivalAction = shuttleLanding;
                        return;
                    }

                    arrivalAction = new TransportersArrivalAction_LoadedReturn(__instance.parent.Tile);
                    return;
                }

                arrivalAction = new TransportersArrivalAction_DronePod(arrivalAction, __instance.parent.Tile);
                return;
            }

            if (defName != "MechanitorShuttle" || !ShuttleArrivalCascade.IsCargoDisposal(arrivalAction, crewAboard: true) || arrivalAction is not TransportersArrivalAction_GiveGift || arrivalAction is TransportersArrivalAction_DronePod)
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

        /// <summary>
        /// 这个抵达动作会不会把货舱当货物处理（投下去、交出去、当礼物送掉）。只有这几种需要包装并让飞机空载返航。
        ///
        /// 空动作（「内容将遗失」）算货运。赠送、交给远行队、袭击定居点、运输舱式落地都算。
        /// 其余一律当作落地型：原版的访问遗址、访问太空、结队，以及别的模组注册的落地动作，
        /// 都由动作自己决定怎么落地。它们多数会先生成地图，再从货舱里取出载具降落；
        /// 提前摘走载具会让它们以为这趟没有载具，转而把载具当普通货物投进空投舱，飞机就此丢失。
        /// 载人时不把袭击定居点算货运：那张地图本来就要生成，载具落地比把人和货一起投下去更安全。
        /// </summary>
        public static bool IsCargoDisposal(TransportersArrivalAction action, bool crewAboard)
        {
            if (action == null)
            {
                return true;
            }

            if (action is TransportersArrivalAction_GiveGift || action is TransportersArrivalAction_GiveToCaravan)
            {
                return true;
            }

            if (action is TransportersArrivalAction_LandInSpecificCell)
            {
                return !LandsInShuttle(action);
            }

            if (action is TransportersArrivalAction_AttackSettlement)
            {
                return !crewAboard;
            }

            return false;
        }

        /// <summary>
        /// 原版 LandInSpecificCell 同时承担两种抵达：landInShuttle 为真时把飞船落到地图上（穿梭机），
        /// 为假时把货物按运输舱投下去（运输舱）。只按类型判定会把后者当成穿梭机流程，
        /// 于是太空无人机既不被摘出来、也不返航，跟着这趟飞行一起消失。字段是私有的，只能读。
        /// </summary>
        private static bool LandsInShuttle(TransportersArrivalAction action)
        {
            if (action is not TransportersArrivalAction_LandInSpecificCell)
            {
                return false;
            }

            object value = Traverse.Create(action).Field("landInShuttle").GetValue();
            return value is bool landInShuttle && landInShuttle;
        }

        /// <summary>
        /// 载人时把运输舱式落地改写成穿梭机式落地。位置和朝向照搬，只把 landInShuttle 换成真。
        ///
        /// 运输舱式落地会把船上的人按货舱投下去，无人机随即空载返航，人就被留在一张没有载具的地图上。
        /// 穿梭机式落地走 DropShuttle，飞船停在地图上、只卸下单位，人和飞机都留着。
        /// 落地冷却仍按 CanLandInSpecificCell 判断，冷却中不改写，避免绕过定居点的进入冷却。
        /// 不改写时返回空，调用方照旧按运输舱流程包装。
        /// </summary>
        public static TransportersArrivalAction AsShuttleLanding(CompLaunchable launchable, TransportersArrivalAction action)
        {
            if (action is not TransportersArrivalAction_LandInSpecificCell || LandsInShuttle(action))
            {
                return null;
            }

            Traverse traverse = Traverse.Create(action);
            MapParent parent = traverse.Field("mapParent").GetValue<MapParent>();
            if (parent == null || !parent.HasMap)
            {
                return null;
            }

            CompTransporter transporter = launchable.parent.GetComp<CompTransporter>();
            List<CompTransporter> pods = transporter.TransportersInGroup(launchable.parent.MapHeld);
            if (!TransportersArrivalAction_LandInSpecificCell.CanLandInSpecificCell(pods, parent))
            {
                return null;
            }

            IntVec3 cell = traverse.Field("cell").GetValue<IntVec3>();
            Rot4 rotation = traverse.Field("rotation").GetValue<Rot4>();
            return new TransportersArrivalAction_LandInSpecificCell(parent, cell, rotation, landInShuttle: true);
        }

        public static IEnumerable<FloatMenuOption> Options(CompLaunchable launchable, PlanetTile tile, Action<PlanetTile, TransportersArrivalAction> launchAction)
        {
            CompTransporter transporter = launchable.parent.GetComp<CompTransporter>();
            List<CompTransporter> pods = transporter.TransportersInGroup(launchable.parent.MapHeld);
            bool drone = launchable.parent.def.defName == "TransportDrone";
            bool crewAboard = drone && CrewAboard(launchable);
            List<FloatMenuOption> shuttle = ShuttleOptions(pods, tile, launchAction);
            if (shuttle.Count == 0 && crewAboard)
            {
                FloatMenuOption crewArrival = CrewShuttleOption(pods, tile, launchAction);
                if (crewArrival != null)
                {
                    shuttle.Add(crewArrival);
                }
            }

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

            if (!crewAboard)
            {
                List<FloatMenuOption> trade = TradeHandoverOptions(pods, tile, launchAction);
                if (trade.Count > 0)
                {
                    for (int i = 0; i < trade.Count; i++)
                    {
                        yield return trade[i];
                    }

                    yield break;
                }
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

        /// <summary>
        /// 载人时补出来的穿梭机式抵达。只在原版穿梭机选项为空时调用，不覆盖已有的穿梭机选项。
        ///
        /// 目的地有地图：给出穿梭机降落，由玩家点选落点。原版只有定居点和部分地点自己提供这条，
        /// 飞船基地、任务地图这类纯地图没有，只能掉进运输舱那一段。
        /// 目的地是空地：给出结队，报到语用穿梭机那句。起飞补丁不再把这趟包进 DronePod，
        /// 所以飞机会和原版穿梭机一样进到远行队的背包里，而不是空载返航。
        /// 两样都不成立时返回空。载人时调用方不再提供赠送，会直接列出运输舱那一段，
        /// 别的模组加的「选择落点降落」这类落地动作就在那一段里。
        /// </summary>
        private static FloatMenuOption CrewShuttleOption(List<CompTransporter> pods, PlanetTile tile, Action<PlanetTile, TransportersArrivalAction> launchAction)
        {
            MapParent parent = MapParentAt(tile);
            if (parent != null && TransportersArrivalAction_LandInSpecificCell.CanLandInSpecificCell(pods, parent))
            {
                return LandingOption(pods, parent, launchAction);
            }

            if (TransportersArrivalAction_FormCaravan.CanFormCaravanAt(pods, tile) && !Find.WorldObjects.AnySettlementBaseAt(tile) && !Find.WorldObjects.AnySiteAt(tile))
            {
                return new FloatMenuOption("FormCaravanHere".Translate(), delegate
                {
                    launchAction(tile, new TransportersArrivalAction_FormCaravan("MessageShuttleArrived"));
                });
            }

            return null;
        }

        /// <summary>
        /// 任意有地图的世界物体都能用的穿梭机降落。落点由玩家点选，朝向取飞机本身的默认朝向。
        /// 和原版定居点那条同名，抵达动作是 landInShuttle 为真的 LandInSpecificCell，飞船留在落点上，不返航。
        /// </summary>
        private static FloatMenuOption LandingOption(List<CompTransporter> pods, MapParent parent, Action<PlanetTile, TransportersArrivalAction> launchAction)
        {
            ThingDef shuttleDef = pods.Count > 0 ? pods[0].parent?.def : null;
            Rot4 rotation = (shuttleDef ?? ThingDefOf.Shuttle).defaultPlacingRot;
            return new FloatMenuOption("LandInExistingMap".Translate(parent.Label), delegate
            {
                Map map = parent.Map;
                Current.Game.CurrentMap = map;
                CameraJumper.TryHideWorld();
                Find.Targeter.BeginTargeting(TargetingParameters.ForCell(), delegate(LocalTargetInfo x)
                {
                    launchAction(parent.Tile, new TransportersArrivalAction_LandInSpecificCell(parent, x.Cell, rotation, landInShuttle: true));
                }, null, null, CompLaunchable.TargeterMouseAttachment);
            });
        }

        private static MapParent MapParentAt(PlanetTile tile)
        {
            List<WorldObject> worldObjects = Find.WorldObjects.AllWorldObjects;
            for (int i = 0; i < worldObjects.Count; i++)
            {
                if (worldObjects[i].Tile == tile && worldObjects[i] is MapParent parent && parent.HasMap)
                {
                    return parent;
                }
            }

            return null;
        }

        /// <summary>
        /// 货舱里有没有玩家阵营的单位。小人、机械族和囚犯都算：赠送会把它们交给对方阵营，运输舱会把它们当货物丢下。
        /// 只看看得见的容器，飞机本身不算在内。只用于太空无人机，机械师飞船的乘员判定在 MechanitorShuttleCrew。
        /// </summary>
        public static bool CrewAboard(CompLaunchable launchable)
        {
            CompTransporter transporter = launchable?.parent?.GetComp<CompTransporter>();
            ThingOwner container = transporter?.innerContainer;
            if (container == null)
            {
                return false;
            }

            Faction player = Faction.OfPlayerSilentFail;
            for (int i = 0; i < container.Count; i++)
            {
                if (container[i] is Pawn pawn && !pawn.Dead && (pawn.Faction == player || pawn.HostFaction == player))
                {
                    return true;
                }
            }

            return false;
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
