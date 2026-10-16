using RimWorld;
using Verse;

namespace OdysseyTransportShuttleEnhanced
{
    /// <summary>
    /// 本模组自己的 Hediff。定义写在 Biotech/Defs 里，只在启用生物科技时加载。
    ///
    /// 注意：字段带 MayRequireBiotech。没装生物科技时游戏不会因为找不到这个 Def 而报错。
    /// 不要在 Mod 构造函数里访问它，那时 Def 还没加载。
    /// </summary>
    [DefOf]
    public static class OTSDefOf
    {
        [MayRequireBiotech]
        public static HediffDef OTS_MechanitorShuttleBandwidth;

        static OTSDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(OTSDefOf));
        }
    }
}
