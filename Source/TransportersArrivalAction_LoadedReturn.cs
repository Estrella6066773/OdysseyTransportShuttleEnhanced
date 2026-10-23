using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace OdysseyTransportShuttleEnhanced
{
    /// <summary>
    /// 太空无人机载人时的兜底返航：不交货、不投送，飞机带着人和货一起飞回起飞格。
    ///
    /// 联动关系：Patch_DroneTryLaunch 在船上有人、选中的又是纯货运动作时改用本类。
    /// 纯货运动作包括赠送、交给远行队、运输舱式落地和空动作；运输舱式落地会先试着改写成穿梭机式落地，
    /// 改不成才落到这里。菜单已经载人时去掉赠送段，所以正常操作里这个是保险，不是主路径。
    ///
    /// StillValid 必须一直为真。返回假时 TravellingTransporters 会换掉这个动作，
    /// 改按地图或运输舱兜底，人和飞机就分开了。
    /// 提示只有「已经返回」和「燃料不够」两句，沿用太空无人机原有的键，不新增文案。
    /// </summary>
    public class TransportersArrivalAction_LoadedReturn : TransportersArrivalAction
    {
        private PlanetTile originTile = PlanetTile.Invalid;

        public TransportersArrivalAction_LoadedReturn()
        {
        }

        public TransportersArrivalAction_LoadedReturn(PlanetTile originTile)
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

            if (!home.Valid)
            {
                return;
            }

            DroneReturnFlight.LaunchLoaded(transporters, tile, home, "TransportDrone", null, "OTS.DroneReturned", "OTS.DroneLowFuel");
        }
    }
}
