using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace OdysseyTransportShuttleEnhanced
{
    /// <summary>
    /// 旧版「只有机械族驾驶」的抵达动作。新的起飞不再创建本类。
    /// 类还留着，是为了正在飞行的旧存档能把这个动作读出来。新的投送失败改走 TransportersArrivalAction_MechShuttleAbort。
    /// OTS.MechShuttleLost 和 OTS.MechShuttleReturning 已从文案里删除，这里不再翻译它们。
    /// </summary>
    public class TransportersArrivalAction_MechShuttlePod : TransportersArrivalAction
    {
        private TransportersArrivalAction inner;

        private PlanetTile originTile = PlanetTile.Invalid;

        public TransportersArrivalAction_MechShuttlePod()
        {
        }

        public TransportersArrivalAction_MechShuttlePod(TransportersArrivalAction inner, PlanetTile originTile)
        {
            this.inner = inner;
            this.originTile = originTile;
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
        }

        public override void Arrived(List<ActiveTransporterInfo> transporters, PlanetTile tile)
        {
            if (inner == null && DroneCargoDelivery.NobodyWillReceive(transporters, tile))
            {
                DroneCargoDelivery.AbandonLikeTransportPods(transporters, tile, banishPlayerPawns: false, showVanillaLostMessage: false);
                return;
            }

            WorldObjectDef flightDef = DroneReturnFlight.FlightDefOf(transporters);
            List<Thing> shuttles = DroneReturnFlight.ExtractShuttles(transporters, "MechanitorShuttle");
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

            DroneReturnFlight.Launch(shuttles, tile, originTile, flightDef, null, "OTS.MechShuttleReturned", "OTS.MechShuttleLowFuel");
        }
    }

    /// <summary>
    /// 只有机械族驾驶、穿梭机流程又不成立时的抵达动作。
    ///
    /// 联动关系：ShuttleArrivalCascade 在这种船上不提供赠送和运输舱选项，只提交本动作。
    /// 抵达后 DroneReturnFlight.LaunchLoaded 把机械族、物品和飞船一起送回出发格，不交货，也不销毁。
    /// StillValid 必须为真，否则 TravellingTransporters 会换成运输舱的销毁兜底。
    /// </summary>
    public class TransportersArrivalAction_MechShuttleAbort : TransportersArrivalAction
    {
        private PlanetTile originTile = PlanetTile.Invalid;

        public TransportersArrivalAction_MechShuttleAbort()
        {
        }

        public TransportersArrivalAction_MechShuttleAbort(PlanetTile originTile)
        {
            this.originTile = originTile;
        }

        public override bool GeneratesMap => false;

        public override FloatMenuAcceptanceReport StillValid(IEnumerable<IThingHolder> pods, PlanetTile destinationTile)
        {
            return true;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref originTile, "originTile");
        }

        public override void Arrived(List<ActiveTransporterInfo> transporters, PlanetTile tile)
        {
            PlanetTile home = originTile;
            if (!home.Valid)
            {
                Map map = Find.AnyPlayerHomeMap;
                if (map != null)
                {
                    home = map.Tile;
                }
            }

            DroneReturnFlight.LaunchLoaded(transporters, tile, home, "MechanitorShuttle", "OTS.MechShuttleReturningWithCargo", "OTS.MechShuttleReturnedWithCargo", "OTS.MechShuttleLowFuelCargo");
        }
    }
}
