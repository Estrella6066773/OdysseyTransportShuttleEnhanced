using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace OdysseyTransportShuttleEnhanced
{
    /// <summary>
    /// 把选中的抵达动作原样执行，再让被摘出的载具空载返回。
    ///
    /// 联动关系：Patch_DroneTryLaunch 只在不是穿梭机流程时套上本类。太空无人机的交易交接和空投舱都用它；
    /// 机械师飞船只在交易交接时用它，并写入 MechanitorShuttle 和对应提示。穿梭机降落、访问、交易不经过这里。
    /// 抵达时先按 shuttleDefName 把载具取出，避免被赠送或销毁，再执行玩家选中的动作。
    /// 动作为空时补运输舱兜底。最后 DroneReturnFlight 只带走载具。
    ///
    /// 注意：StillValid 必须一直为真。一旦返回假，TravellingTransporters 会丢掉本动作，载具就会留在货舱里被一起销毁。
    /// 赠送前如果定居点有足够的交易请求货物，先按交易请求提交，剩下的仍全部赠送。
    /// 旧存档没有 shuttleDefName，按太空无人机读。
    /// </summary>
    public class TransportersArrivalAction_DronePod : TransportersArrivalAction
    {
        private TransportersArrivalAction inner;

        private PlanetTile originTile = PlanetTile.Invalid;

        private string shuttleDefName = "TransportDrone";

        private string returningMessage = "OTS.DroneReturning";

        private string returnedMessage = "OTS.DroneReturned";

        private string lowFuelMessage = "OTS.DroneLowFuel";

        public TransportersArrivalAction_DronePod()
        {
        }

        public TransportersArrivalAction_DronePod(TransportersArrivalAction inner, PlanetTile originTile)
        {
            this.inner = inner;
            this.originTile = originTile;
        }

        public TransportersArrivalAction_DronePod(TransportersArrivalAction inner, PlanetTile originTile, string shuttleDefName, string returningMessage, string returnedMessage, string lowFuelMessage)
            : this(inner, originTile)
        {
            this.shuttleDefName = shuttleDefName;
            this.returningMessage = returningMessage ?? "";
            this.returnedMessage = returnedMessage;
            this.lowFuelMessage = lowFuelMessage;
        }

        public override bool GeneratesMap => inner != null && inner.GeneratesMap;

        public override bool ShouldUseLongEvent(List<ActiveTransporterInfo> pods, PlanetTile tile)
        {
            return inner != null && inner.ShouldUseLongEvent(pods, tile);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Deep.Look(ref inner, "inner");
            Scribe_Values.Look(ref originTile, "originTile");
            Scribe_Values.Look(ref shuttleDefName, "shuttleDefName", "TransportDrone");
            Scribe_Values.Look(ref returningMessage, "returningMessage", "OTS.DroneReturning");
            Scribe_Values.Look(ref returnedMessage, "returnedMessage", "OTS.DroneReturned");
            Scribe_Values.Look(ref lowFuelMessage, "lowFuelMessage", "OTS.DroneLowFuel");
        }

        public override void Arrived(List<ActiveTransporterInfo> transporters, PlanetTile tile)
        {
            WorldObjectDef flightDef = DroneReturnFlight.FlightDefOf(transporters);
            List<Thing> drones = DroneReturnFlight.ExtractShuttles(transporters, shuttleDefName);
            if (inner is TransportersArrivalAction_GiveGift)
            {
                DroneCargoDelivery.TryFulfillTradeRequest(transporters, tile);
            }

            if (inner != null)
            {
                inner.Arrived(transporters, tile);
            }
            else
            {
                DroneCargoDelivery.ResolveUnspecifiedPodArrival(transporters, tile);
            }

            DroneReturnFlight.Launch(drones, tile, originTile, flightDef, returningMessage, returnedMessage, lowFuelMessage);
        }
    }

    /// <summary>
    /// 返程降落。太空无人机的空载返回、机械师飞船交货后的空载返回、只有机械族时带着货舱返回，都落到这里。
    ///
    /// 联动关系：DroneReturnFlight.Launch / LaunchLoaded 在扣燃料之前算出这趟返程燃料够不够，
    /// 连同起飞格和朝向写进本动作。TravellingTransporters 抵达后调用 Arrived，然后销毁世界物体，
    /// 所以飞机必须在 Arrived 返回前落地，不能留在运输舱里。
    ///
    /// 燃料不够：在这张地图上随机迫降。落点可以有屋顶，但不能是厚山顶；屋顶由下落体在撞地时砸开。
    /// 燃料够：先回起飞那一格；那一格被占了就去飞船降落信标圈出的降落点；再没有就随机找没有屋顶的空地。
    /// 这三处都没有，才用和燃料不够相同的迫降。迫降也找不到不压厚山的格子时，不再落到地图中心。
    /// 那一档由 DroneReturnFlight.ScrapAndAirdrop 把飞船按建造消耗拆成原材料，连同货舱里的东西走空投舱。
    ///
    /// 注意：不要调用 TransportersArrivalActionUtility.DropShuttle。它会在目标格不合法时先在附近 20 格、
    /// 再在全图另找一块能正常降落的格子，迫降会被挪到屋顶外面，起飞格被占时也不会去降落点。
    /// 旧存档没有 crashLanding 和 originCell，按燃料充足、没有起飞格处理，于是从降落点接着往下找。
    /// </summary>
    public class TransportersArrivalAction_DroneReturn : TransportersArrivalAction
    {
        private string returnedMessage = "OTS.DroneReturned";

        private bool crashLanding;

        private IntVec3 originCell = IntVec3.Invalid;

        private Rot4 originRot = Rot4.North;

        private PlanetTile originTile = PlanetTile.Invalid;

        public TransportersArrivalAction_DroneReturn()
        {
        }

        public TransportersArrivalAction_DroneReturn(string returnedMessage, bool crashLanding, IntVec3 originCell, Rot4 originRot, PlanetTile originTile)
        {
            if (!string.IsNullOrEmpty(returnedMessage))
            {
                this.returnedMessage = returnedMessage;
            }

            this.crashLanding = crashLanding;
            this.originCell = originCell;
            this.originRot = originRot.IsValid ? originRot : Rot4.North;
            this.originTile = originTile;
        }

        public override bool GeneratesMap => false;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref returnedMessage, "returnedMessage", "OTS.DroneReturned");
            Scribe_Values.Look(ref crashLanding, "crashLanding", false);
            Scribe_Values.Look(ref originCell, "originCell", IntVec3.Invalid);
            Scribe_Values.Look(ref originTile, "originTile", PlanetTile.Invalid);
            int rotInt = originRot.AsInt;
            Scribe_Values.Look(ref rotInt, "originRot", Rot4.North.AsInt);
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                originRot = new Rot4(rotInt);
            }
        }

        public override void Arrived(List<ActiveTransporterInfo> transporters, PlanetTile tile)
        {
            Map map = DroneReturnFlight.MapAt(tile) ?? Find.AnyPlayerHomeMap;
            if (map == null)
            {
                return;
            }

            bool anyLanded = false;
            for (int i = 0; i < transporters.Count; i++)
            {
                Thing shuttle = transporters[i].GetShuttle();
                if (shuttle == null || shuttle.Destroyed)
                {
                    continue;
                }

                Rot4 rot = originRot.IsValid ? originRot : shuttle.Rotation;
                if (!rot.IsValid)
                {
                    rot = shuttle.def.defaultPlacingRot;
                }

                if (!TryChooseCell(map, shuttle.def, ref rot, out IntVec3 cell))
                {
                    DroneReturnFlight.ScrapAndAirdrop(transporters[i], map);
                    continue;
                }

                anyLanded = true;
                DroneReturnFlight.DropExactly(transporters[i], map, cell, rot);
            }

            if (anyLanded)
            {
                Messages.Message(returnedMessage.Translate(), new GlobalTargetInfo(map.Tile), MessageTypeDefOf.PositiveEvent);
            }
        }

        /// <summary>
        /// 选出落地格，并在迫降需要转向时改 rot。起飞格只在抵达地图就是出发地图时使用。
        /// 返回假表示整张图都放不下这艘飞船，调用方改走解体空投，不能再改用地图中心硬降。
        /// </summary>
        private bool TryChooseCell(Map map, ThingDef shuttleDef, ref Rot4 rot, out IntVec3 cell)
        {
            if (!crashLanding)
            {
                if (map.Tile == originTile && originCell.IsValid && RoyalTitlePermitWorker_CallShuttle.ShuttleCanLandHere(originCell, map, shuttleDef, rot))
                {
                    cell = originCell;
                    return true;
                }

                if (DroneReturnFlight.TryFindLandingZone(map, shuttleDef, ref rot, out cell))
                {
                    return true;
                }

                if (DroneReturnFlight.TryFindUnroofedPad(map, shuttleDef, ref rot, out cell))
                {
                    return true;
                }
            }

            if (DroneReturnFlight.TryFindCrashCell(map, shuttleDef, ref rot, out cell))
            {
                return true;
            }

            if (DroneReturnFlight.TryFindNonThickCell(map, shuttleDef, ref rot, out cell))
            {
                return true;
            }

            cell = IntVec3.Invalid;
            return false;
        }
    }

    /// <summary>
    /// 运输舱货物处理，以及把飞机单独送回去。
    /// </summary>
    public static class DroneCargoDelivery
    {
        public static void ResolveUnspecifiedPodArrival(List<ActiveTransporterInfo> transporters, PlanetTile tile)
        {
            Map map = DroneReturnFlight.MapAt(tile);
            if (map != null)
            {
                new TransportersArrivalAction_LandInSpecificCell(map.Parent, DropCellFinder.RandomDropSpot(map)).Arrived(transporters, tile);
                return;
            }

            if (TransportersArrivalAction_FormCaravan.CanFormCaravanAt(transporters, tile))
            {
                new TransportersArrivalAction_FormCaravan().Arrived(transporters, tile);
                return;
            }

            List<Caravan> caravans = Find.WorldObjects.Caravans;
            for (int i = 0; i < caravans.Count; i++)
            {
                if (caravans[i].Tile == tile && TransportersArrivalAction_GiveToCaravan.CanGiveTo(transporters, caravans[i]))
                {
                    new TransportersArrivalAction_GiveToCaravan(caravans[i]).Arrived(transporters, tile);
                    return;
                }
            }

            AbandonLikeTransportPods(transporters, tile);
        }

        public static void TryFulfillTradeRequest(List<ActiveTransporterInfo> transporters, PlanetTile tile)
        {
            Settlement settlement = SettlementAt(tile);
            TradeRequestComp request = settlement?.GetComponent<TradeRequestComp>();
            if (request == null || !request.ActiveRequest || CountGiveable(transporters, request.requestThingDef) < request.requestCount)
            {
                return;
            }

            int remaining = request.requestCount;
            for (int i = 0; i < transporters.Count && remaining > 0; i++)
            {
                ThingOwner container = transporters[i].innerContainer;
                for (int j = container.Count - 1; j >= 0 && remaining > 0; j--)
                {
                    Thing thing = container[j];
                    if (thing.def != request.requestThingDef || !CanGiveToTradeRequest(thing))
                    {
                        continue;
                    }

                    int take = Mathf.Min(remaining, thing.stackCount);
                    Thing given = take >= thing.stackCount ? thing : thing.SplitOff(take);
                    if (given == thing)
                    {
                        container.Remove(given);
                    }

                    given.Destroy();
                    remaining -= take;
                }
            }

            request.Disable();
            if (settlement.Faction != null)
            {
                Faction.OfPlayer.TryAffectGoodwillWith(settlement.Faction, 12, canSendMessage: true, canSendHostilityLetter: true, HistoryEventDefOf.QuestGoodwillReward);
            }

            QuestUtility.SendQuestTargetSignals(settlement.questTags, "TradeRequestFulfilled", settlement.Named("SUBJECT"));
        }

        public static bool NobodyWillReceive(List<ActiveTransporterInfo> transporters, PlanetTile tile)
        {
            if (DroneReturnFlight.MapAt(tile) != null)
            {
                return false;
            }

            if (TransportersArrivalAction_FormCaravan.CanFormCaravanAt(transporters, tile))
            {
                return false;
            }

            List<Caravan> caravans = Find.WorldObjects.Caravans;
            for (int i = 0; i < caravans.Count; i++)
            {
                if (caravans[i].Tile == tile && TransportersArrivalAction_GiveToCaravan.CanGiveTo(transporters, caravans[i]))
                {
                    return false;
                }
            }

            return true;
        }

        public static void AbandonLikeTransportPods(List<ActiveTransporterInfo> transporters, PlanetTile tile, bool banishPlayerPawns = true, bool showVanillaLostMessage = true)
        {
            if (banishPlayerPawns)
            {
                for (int i = 0; i < transporters.Count; i++)
                {
                    ThingOwner container = transporters[i].innerContainer;
                    for (int j = 0; j < container.Count; j++)
                    {
                        if (container[j] is Pawn pawn && (pawn.Faction == Faction.OfPlayer || pawn.HostFaction == Faction.OfPlayer))
                        {
                            PawnBanishUtility.Banish(pawn, tile);
                        }
                    }
                }
            }

            bool showLostMessage = true;
            if (ModsConfig.BiotechActive)
            {
                showLostMessage = false;
                for (int i = 0; i < transporters.Count && !showLostMessage; i++)
                {
                    ThingOwner container = transporters[i].innerContainer;
                    for (int j = 0; j < container.Count; j++)
                    {
                        if (container[j].def != ThingDefOf.Wastepack)
                        {
                            showLostMessage = true;
                            break;
                        }
                    }
                }
            }

            for (int i = 0; i < transporters.Count; i++)
            {
                ThingOwner container = transporters[i].innerContainer;
                for (int j = 0; j < container.Count; j++)
                {
                    container[j].Notify_AbandonedAtTile(tile);
                }
            }

            for (int i = 0; i < transporters.Count; i++)
            {
                transporters[i].innerContainer.ClearAndDestroyContentsOrPassToWorld();
            }

            if (showVanillaLostMessage && showLostMessage)
            {
                Messages.Message("MessageTransportPodsArrivedAndLost".Translate(), new GlobalTargetInfo(tile), MessageTypeDefOf.NegativeEvent);
            }
        }

        private static Settlement SettlementAt(PlanetTile tile)
        {
            List<Settlement> settlements = Find.WorldObjects.Settlements;
            for (int i = 0; i < settlements.Count; i++)
            {
                if (settlements[i].Tile == tile)
                {
                    return settlements[i];
                }
            }

            return null;
        }

        private static int CountGiveable(List<ActiveTransporterInfo> transporters, ThingDef def)
        {
            int count = 0;
            for (int i = 0; i < transporters.Count; i++)
            {
                ThingOwner container = transporters[i].innerContainer;
                for (int j = 0; j < container.Count; j++)
                {
                    Thing thing = container[j];
                    if (thing.def == def && CanGiveToTradeRequest(thing))
                    {
                        count += thing.stackCount;
                    }
                }
            }

            return count;
        }

        private static bool CanGiveToTradeRequest(Thing thing)
        {
            if (thing.GetRotStage() != RotStage.Fresh)
            {
                return false;
            }

            if (thing is Apparel apparel && apparel.WornByCorpse)
            {
                return false;
            }

            CompQuality quality = thing.TryGetComp<CompQuality>();
            return quality == null || quality.Quality >= QualityCategory.Normal;
        }
    }

    /// <summary>
    /// 从货舱拿出太空无人机，并单独开一趟返程。返程货舱里只有飞机。
    /// </summary>
    public static class DroneReturnFlight
    {
        public static WorldObjectDef FlightDefOf(List<ActiveTransporterInfo> transporters)
        {
            for (int i = 0; i < transporters.Count; i++)
            {
                if (transporters[i].parent is TravellingTransporters travelling)
                {
                    return travelling.def;
                }
            }

            return DefDatabase<WorldObjectDef>.GetNamedSilentFail("TransportDrone");
        }

        public static List<Thing> ExtractDrones(List<ActiveTransporterInfo> transporters)
        {
            return ExtractShuttles(transporters, "TransportDrone");
        }

        public static List<Thing> ExtractShuttles(List<ActiveTransporterInfo> transporters, string defName)
        {
            List<Thing> drones = new List<Thing>();
            for (int i = 0; i < transporters.Count; i++)
            {
                Thing shuttle = transporters[i].GetShuttle();
                if (shuttle == null || shuttle.Destroyed || shuttle.def.defName != defName)
                {
                    continue;
                }

                transporters[i].RemoveShuttle();
                drones.Add(shuttle);
            }

            return drones;
        }

        public static void Launch(List<Thing> drones, PlanetTile from, PlanetTile to, WorldObjectDef flightDef, string returningMessage = "OTS.DroneReturning", string returnedMessage = "OTS.DroneReturned", string lowFuelMessage = "OTS.DroneLowFuel")
        {
            if (drones.Count == 0 || flightDef == null || !to.Valid)
            {
                return;
            }

            for (int i = 0; i < drones.Count; i++)
            {
                Thing drone = drones[i];
                if (drone == null || drone.Destroyed)
                {
                    continue;
                }

                bool enoughFuel = ConsumeReturnFuel(drone, from, to, lowFuelMessage);
                ActiveTransporterInfo info = new ActiveTransporterInfo();
                info.sentTransporterDef = drone.def;
                info.SetShuttle(drone);

                TravellingTransporters flight = (TravellingTransporters)WorldObjectMaker.MakeWorldObject(flightDef);
                flight.SetFaction(Faction.OfPlayer);
                flight.Tile = from;
                flight.destinationTile = to;
                flight.arrivalAction = new TransportersArrivalAction_DroneReturn(returnedMessage, !enoughFuel, drone.Position, drone.Rotation, to);
                Find.WorldObjects.Add(flight);
                flight.AddTransporter(info, justLeftTheMap: false);
            }

            if (HasReturnNotice(returningMessage))
            {
                Messages.Message(returningMessage.Translate(), new GlobalTargetInfo(from), MessageTypeDefOf.NeutralEvent);
            }
        }

        /// <summary>
        /// 投送失败的返程。货舱里的机械族、物品和飞船一起送回，不在目的地卸货。
        /// 降落时 DropExactly 会把这些东西放进飞船自己的货舱，玩家飞船只把单位卸下来，物品仍留在船上。
        /// 落点由返程动作按燃料决定，不在这里选格。调用前先确认目的地有效，避免东西已经从旧货舱取出却无处可去。
        /// </summary>
        public static void LaunchLoaded(List<ActiveTransporterInfo> transporters, PlanetTile from, PlanetTile to, string shuttleDefName, string returningMessage, string returnedMessage, string lowFuelMessage)
        {
            WorldObjectDef flightDef = FlightDefOf(transporters);
            if (flightDef == null || !to.Valid)
            {
                return;
            }

            Thing shuttle = null;
            List<Thing> cargo = new List<Thing>();
            for (int i = 0; i < transporters.Count; i++)
            {
                Thing candidate = transporters[i].GetShuttle();
                if (shuttle == null && candidate != null && !candidate.Destroyed && candidate.def.defName == shuttleDefName)
                {
                    transporters[i].RemoveShuttle();
                    shuttle = candidate;
                }

                ThingOwner container = transporters[i].innerContainer;
                for (int j = container.Count - 1; j >= 0; j--)
                {
                    Thing thing = container[j];
                    container.Remove(thing);
                    cargo.Add(thing);
                }
            }

            if (shuttle == null || shuttle.Destroyed)
            {
                if (transporters.Count > 0)
                {
                    for (int i = 0; i < cargo.Count; i++)
                    {
                        transporters[0].innerContainer.TryAdd(cargo[i]);
                    }

                    if (shuttle != null)
                    {
                        transporters[0].innerContainer.TryAdd(shuttle);
                    }
                }

                return;
            }

            bool enoughFuel = ConsumeReturnFuel(shuttle, from, to, lowFuelMessage);
            ActiveTransporterInfo info = new ActiveTransporterInfo();
            info.sentTransporterDef = shuttle.def;
            for (int i = 0; i < cargo.Count; i++)
            {
                info.innerContainer.TryAdd(cargo[i]);
            }

            info.SetShuttle(shuttle);
            TravellingTransporters flight = (TravellingTransporters)WorldObjectMaker.MakeWorldObject(flightDef);
            flight.SetFaction(Faction.OfPlayer);
            flight.Tile = from;
            flight.destinationTile = to;
            flight.arrivalAction = new TransportersArrivalAction_DroneReturn(returnedMessage, !enoughFuel, shuttle.Position, shuttle.Rotation, to);
            Find.WorldObjects.Add(flight);
            flight.AddTransporter(info, justLeftTheMap: false);
            if (HasReturnNotice(returningMessage))
            {
                Messages.Message(returningMessage.Translate(), new GlobalTargetInfo(from), MessageTypeDefOf.NeutralEvent);
            }
        }

        /// <summary>
        /// 空字符串和已删除的两句提示不再弹出。旧存档里如果还写着这两句的键，起飞时也跳过，避免界面露出未翻译的键名。
        /// 落地提示仍用 returnedMessage，不经过这里。
        /// </summary>
        private static bool HasReturnNotice(string key)
        {
            return !key.NullOrEmpty() && key != "OTS.MechShuttleReturning" && key != "OTS.MechShuttleLost";
        }

        public static Map MapAt(PlanetTile tile)
        {
            List<Map> maps = Find.Maps;
            for (int i = 0; i < maps.Count; i++)
            {
                if (maps[i].Tile == tile)
                {
                    return maps[i];
                }
            }

            return null;
        }

        /// <summary>
        /// 按去程距离扣返程燃料。返回值表示扣之前燃料是否够付这一趟。
        /// 不够时仍然扣光现有燃料并继续返航，调用方据此改成迫降。没有燃料组件时视为够，避免没有油箱的东西被判成迫降。
        /// </summary>
        private static bool ConsumeReturnFuel(Thing drone, PlanetTile from, PlanetTile to, string lowFuelMessage)
        {
            if (!(drone is ThingWithComps thing) || !from.Valid || !to.Valid)
            {
                return true;
            }

            CompLaunchable launchable = thing.GetComp<CompLaunchable>();
            CompRefuelable fuel = thing.GetComp<CompRefuelable>();
            if (launchable == null || fuel == null)
            {
                return true;
            }

            int distance = Find.WorldGrid.TraversalDistanceBetween(from, to, passImpassable: true, int.MaxValue, canTraverseLayers: true);
            float cost = launchable.FuelNeededToLaunchAtDist(distance, to.Layer);
            bool enough = fuel.Fuel >= cost;
            if (!enough)
            {
                Messages.Message(lowFuelMessage.Translate(), new GlobalTargetInfo(from), MessageTypeDefOf.NegativeEvent);
            }

            fuel.ConsumeFuel(Mathf.Min(fuel.Fuel, cost));
            return enough;
        }

        /// <summary>
        /// 阶梯选址全部失败后的兜底。调用方是 TransportersArrivalAction_DroneReturn.Arrived。
        /// 必须在 Arrived 返回前把飞船和货物从运输舱拿出来，TravellingTransporters 返回后会销毁这趟飞行。
        ///
        /// 原材料来自 CostListAdjusted，按建造消耗整份生成，不套殖民者拆除的折损。building.leavingsBlacklist 里的东西不掉。
        /// 油箱里剩下的化合燃料不另算：原版拆除也不返还油箱，而且建造消耗里如果已经有化合燃料，再倒一遍会重复。
        /// 飞船用 DestroyMode.Vanish 销毁，避免 GenLeaving 再掉一份材料。机械师飞船的绑定在 PostDestroy 里解开。
        ///
        /// 空投对齐 DropPodUtility.DropThingsNear：每件东西一个空投舱，从贸易空投点附近找落点，允许打穿普通屋顶。
        /// 不设禁止，这些是玩家自己的货。红色消息的 LookTargets 是这些落点，点消息会跳到空投格子。
        /// </summary>
        public static void ScrapAndAirdrop(ActiveTransporterInfo transporter, Map map)
        {
            Thing shuttle = transporter.RemoveShuttle();
            List<Thing> payload = new List<Thing>();
            if (shuttle != null && !shuttle.Destroyed)
            {
                TakeOut(shuttle.TryGetComp<CompTransporter>()?.innerContainer, payload, shuttle);
                AddBuildMaterials(shuttle, payload);
                shuttle.Destroy(DestroyMode.Vanish);
            }

            TakeOut(transporter.innerContainer, payload, shuttle);

            IntVec3 center = DropCellFinder.TradeDropSpot(map);
            if (!center.IsValid)
            {
                center = map.Center;
            }

            List<TargetInfo> drops = new List<TargetInfo>();
            for (int i = 0; i < payload.Count; i++)
            {
                IntVec3 cell = FindItemDropCell(map, center);
                ActiveTransporterInfo info = new ActiveTransporterInfo();
                info.openDelay = 110;
                if (!info.innerContainer.TryAdd(payload[i]))
                {
                    GenPlace.TryPlaceThing(payload[i], cell, map, ThingPlaceMode.Near);
                }
                else
                {
                    DropPodUtility.MakeDropPodAt(cell, map, info, Faction.OfPlayer);
                }

                drops.Add(new TargetInfo(cell, map));
            }

            LookTargets look = drops.Count > 0 ? new LookTargets(drops) : new LookTargets(center, map);
            Messages.Message("OTS.ShuttleScrapped".Translate(), look, MessageTypeDefOf.NegativeEvent);
        }

        /// <summary>
        /// 落点顺序和 DropPodUtility.DropThingGroupsNear 在 canRoofPunch 为真时相同：
        /// 先在中心附近找能打穿普通屋顶的格子，找不到再随机一块不压厚山、能走的格子。
        /// 全图都是厚山时才退回中心，避免货物还留在即将被销毁的运输舱里。
        /// </summary>
        private static IntVec3 FindItemDropCell(Map map, IntVec3 center)
        {
            if (DropCellFinder.TryFindDropSpotNear(center, map, out IntVec3 result, allowFogged: true, canRoofPunch: true))
            {
                return result;
            }

            IntVec3 random = CellFinderLoose.RandomCellWith((IntVec3 c) => c.Walkable(map) && !(c.GetRoof(map)?.isThickRoof ?? false), map);
            if (random.IsValid)
            {
                return random;
            }

            return center.IsValid ? center : map.Center;
        }

        private static void AddBuildMaterials(Thing shuttle, List<Thing> payload)
        {
            List<ThingDefCountClass> costs = shuttle.CostListAdjusted();
            if (costs == null)
            {
                return;
            }

            List<ThingDef> blacklist = shuttle.def.building?.leavingsBlacklist;
            for (int i = 0; i < costs.Count; i++)
            {
                ThingDefCountClass cost = costs[i];
                if (cost?.thingDef == null || cost.count <= 0)
                {
                    continue;
                }

                if (blacklist != null && blacklist.Contains(cost.thingDef))
                {
                    continue;
                }

                int left = cost.count;
                int limit = Mathf.Max(1, cost.thingDef.stackLimit);
                while (left > 0)
                {
                    Thing made = ThingMaker.MakeThing(cost.thingDef);
                    int stack = Mathf.Min(left, limit);
                    made.stackCount = stack;
                    payload.Add(made);
                    left -= stack;
                }
            }
        }

        private static void TakeOut(ThingOwner container, List<Thing> payload, Thing shuttle)
        {
            if (container == null)
            {
                return;
            }

            for (int i = container.Count - 1; i >= 0; i--)
            {
                Thing thing = container[i];
                if (thing == null || thing == shuttle || thing.Destroyed)
                {
                    continue;
                }

                container.Remove(thing);
                payload.Add(thing);
            }
        }

        /// <summary>
        /// 把飞机落到指定格，不改格子。货舱里其余东西先转入飞机自己的运输舱，玩家飞机只把单位卸下来。
        /// 前置条件：cell 已在地图内。抵达任务会按这个格子生成下落体，屋顶在下落体撞地时处理。
        /// 选不出格子时不要调用这里，Arrived 会改走 ScrapAndAirdrop。
        /// </summary>
        public static void DropExactly(ActiveTransporterInfo transporter, Map map, IntVec3 cell, Rot4 rotation)
        {
            TransportersArrivalActionUtility.RemovePawnsFromWorldPawns(Gen.YieldSingle(transporter));
            Thing thing = transporter.RemoveShuttle();
            if (thing == null)
            {
                return;
            }

            if (!rotation.IsValid)
            {
                rotation = thing.def.defaultPlacingRot;
            }

            thing.Rotation = rotation;
            CompTransporter compTransporter = thing.TryGetComp<CompTransporter>();
            compTransporter.innerContainer.TryAddRangeOrTransfer(transporter.innerContainer);
            TransportShipDef shipDef = thing.TryGetComp<CompShuttle>()?.Props.shipDef ?? TransportShipDefOf.Ship_Shuttle;
            TransportShip transportShip = compTransporter.Shuttle.shipParent;
            if (transportShip == null)
            {
                transportShip = TransportShipMaker.MakeTransportShip(shipDef, null, thing);
            }

            if (!cell.IsValid)
            {
                cell = map.Center;
            }

            transportShip.ArriveAt(cell, map.Parent);
            if (shipDef == null || !shipDef.playerShuttle)
            {
                transportShip.AddJobs(ShipJobDefOf.Unload, ShipJobDefOf.FlyAway);
            }
            else
            {
                ShipJob_Unload shipJob_Unload = (ShipJob_Unload)ShipJobMaker.MakeShipJob(ShipJobDefOf.Unload);
                shipJob_Unload.dropMode = TransportShipDropMode.PawnsOnly;
                transportShip.AddJob(shipJob_Unload);
            }
        }

        /// <summary>
        /// 王权降落信标圈出的、当前没有遮挡的降落点。没有王权，或信标范围内放不下这艘飞船时返回假。
        /// GetLandingZones 返回的是共用列表，读完就只留下矩形，避免后面的查找把它清掉。
        /// </summary>
        public static bool TryFindLandingZone(Map map, ThingDef shuttleDef, ref Rot4 rot, out IntVec3 cell)
        {
            cell = IntVec3.Invalid;
            if (!ModsConfig.RoyaltyActive)
            {
                return false;
            }

            List<ShipLandingArea> zones = ShipLandingBeaconUtility.GetLandingZones(map);
            List<CellRect> rects = new List<CellRect>();
            for (int i = 0; i < zones.Count; i++)
            {
                if (zones[i].Clear)
                {
                    rects.Add(zones[i].MyRect);
                }
            }

            if (rects.Count == 0)
            {
                return false;
            }

            rects.Shuffle();
            Rot4 preferred = rot;
            if (TryCellInZones(rects, map, shuttleDef, preferred, out cell))
            {
                return true;
            }

            for (int i = 0; i < 4; i++)
            {
                Rot4 facing = new Rot4(i);
                if (facing == preferred)
                {
                    continue;
                }

                if (TryCellInZones(rects, map, shuttleDef, facing, out cell))
                {
                    rot = facing;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 先试降落点中心。中心放不下整艘飞船时，再在信标圈出的格子里找能放下的位置。
        /// 不能改成全图随机再判断是否落在圈内，降落点很小，随机几乎碰不到。
        /// </summary>
        private static bool TryCellInZones(List<CellRect> rects, Map map, ThingDef shuttleDef, Rot4 rot, out IntVec3 cell)
        {
            for (int i = 0; i < rects.Count; i++)
            {
                IntVec3 center = rects[i].CenterCell;
                if (RoyalTitlePermitWorker_CallShuttle.ShuttleCanLandHere(center, map, shuttleDef, rot))
                {
                    cell = center;
                    return true;
                }

                foreach (IntVec3 candidate in rects[i])
                {
                    if (candidate == center)
                    {
                        continue;
                    }

                    if (RoyalTitlePermitWorker_CallShuttle.ShuttleCanLandHere(candidate, map, shuttleDef, rot))
                    {
                        cell = candidate;
                        return true;
                    }
                }
            }

            cell = IntVec3.Invalid;
            return false;
        }

        /// <summary>
        /// 随机一块能正常降落、而且占地和交互格都没有屋顶的空地。
        /// </summary>
        public static bool TryFindUnroofedPad(Map map, ThingDef shuttleDef, ref Rot4 rot, out IntVec3 cell)
        {
            return TryRotations(map, ref rot, (candidate, facing) => CanLandWithoutRoof(candidate, map, shuttleDef, facing), out cell);
        }

        /// <summary>
        /// 随机迫降格。占地可以有屋顶，但不能有厚山屋顶，也不能压在建筑或树上。
        /// 下落体自身的占地如果会压到厚山，也排除，避免撞地时把山体砸开。
        /// 先避开单位；全图没有这样的格子时，才允许落在单位身上。
        /// </summary>
        public static bool TryFindCrashCell(Map map, ThingDef shuttleDef, ref Rot4 rot, out IntVec3 cell)
        {
            if (TryRotations(map, ref rot, (candidate, facing) => CanCrashAt(candidate, map, shuttleDef, facing, allowPawns: false), out cell))
            {
                return true;
            }

            return TryRotations(map, ref rot, (candidate, facing) => CanCrashAt(candidate, map, shuttleDef, facing, allowPawns: true), out cell);
        }

        /// <summary>
        /// 空地已经找完时的底线：只要占地和下落体不压厚山即可，建筑和树不再躲开。
        /// 这里再失败，返程动作不会落到地图中心，而会把飞船拆掉空投。
        /// </summary>
        public static bool TryFindNonThickCell(Map map, ThingDef shuttleDef, ref Rot4 rot, out IntVec3 cell)
        {
            return TryRotations(map, ref rot, (candidate, facing) => FootprintAvoidsThickRoof(candidate, map, shuttleDef, facing), out cell);
        }

        private static bool TryRotations(Map map, ref Rot4 rot, Func<IntVec3, Rot4, bool> test, out IntVec3 cell)
        {
            Rot4 preferred = rot;
            if (CellFinder.TryFindRandomCell(map, c => test(c, preferred), out cell))
            {
                return true;
            }

            for (int i = 0; i < 4; i++)
            {
                Rot4 facing = new Rot4(i);
                if (facing == preferred)
                {
                    continue;
                }

                Rot4 captured = facing;
                if (CellFinder.TryFindRandomCell(map, c => test(c, captured), out cell))
                {
                    rot = captured;
                    return true;
                }
            }

            cell = IntVec3.Invalid;
            return false;
        }

        private static bool CanLandWithoutRoof(IntVec3 cell, Map map, ThingDef shuttleDef, Rot4 rot)
        {
            if (!RoyalTitlePermitWorker_CallShuttle.ShuttleCanLandHere(cell, map, shuttleDef, rot))
            {
                return false;
            }

            foreach (IntVec3 item in GenAdj.OccupiedRect(cell, rot, shuttleDef.size))
            {
                if (item.Roofed(map))
                {
                    return false;
                }
            }

            IntVec3 interaction = ThingUtility.InteractionCellWhenAt(shuttleDef, cell, rot, map);
            return !interaction.InBounds(map) || !interaction.Roofed(map);
        }

        private static bool CanCrashAt(IntVec3 cell, Map map, ThingDef shuttleDef, Rot4 rot, bool allowPawns)
        {
            CellRect shipRect = GenAdj.OccupiedRect(cell, rot, shuttleDef.size);
            if (!shipRect.InBounds(map) || !OpenGround(shipRect, map, allowPawns))
            {
                return false;
            }

            return FootprintAvoidsThickRoof(cell, map, shuttleDef, rot);
        }

        private static bool FootprintAvoidsThickRoof(IntVec3 cell, Map map, ThingDef shuttleDef, Rot4 rot)
        {
            CellRect shipRect = GenAdj.OccupiedRect(cell, rot, shuttleDef.size);
            if (!shipRect.InBounds(map))
            {
                return false;
            }

            foreach (IntVec3 item in shipRect)
            {
                RoofDef roof = item.GetRoof(map);
                if (roof != null && roof.isThickRoof)
                {
                    return false;
                }
            }

            foreach (IntVec3 item in SkyfallerRect(cell, shuttleDef))
            {
                if (!item.InBounds(map))
                {
                    continue;
                }

                RoofDef roof = item.GetRoof(map);
                if (roof != null && roof.isThickRoof)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool OpenGround(CellRect rect, Map map, bool allowPawns)
        {
            foreach (IntVec3 cell in rect)
            {
                if (!cell.Walkable(map))
                {
                    return false;
                }

                TerrainDef terrain = cell.GetTerrain(map);
                if (terrain.IsWater || terrain.exposesToVacuum)
                {
                    return false;
                }

                RoofDef roof = cell.GetRoof(map);
                if (roof != null && roof.isThickRoof)
                {
                    return false;
                }

                if (cell.GetEdifice(map) != null)
                {
                    return false;
                }

                List<Thing> things = cell.GetThingList(map);
                for (int i = 0; i < things.Count; i++)
                {
                    Thing thing = things[i];
                    if (thing.def.plant != null && thing.def.plant.IsTree)
                    {
                        return false;
                    }

                    if (thing is Skyfaller || thing is IActiveTransporter)
                    {
                        return false;
                    }

                    if (!allowPawns && thing is Pawn)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// 抵达任务生成的下落体不沿用飞船朝向，占地按北向计算。定义上没有单独尺寸时退回飞船占地。
        /// </summary>
        private static CellRect SkyfallerRect(IntVec3 cell, ThingDef shuttleDef)
        {
            ThingDef skyfaller = shuttleDef.GetCompProperties<CompProperties_Shuttle>()?.shipDef?.arrivingSkyfaller;
            IntVec2 size = skyfaller != null && skyfaller.size.x > 0 && skyfaller.size.z > 0 ? skyfaller.size : shuttleDef.size;
            return GenAdj.OccupiedRect(cell, Rot4.North, size);
        }
    }
}
