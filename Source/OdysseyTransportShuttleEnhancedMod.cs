using System.Reflection;
using HarmonyLib;
using Verse;

namespace OdysseyTransportShuttleEnhanced
{
    /// <summary>
    /// 模组入口。游戏在 LoadedModManager.CreateModClasses 里构造本类，构造函数里 PatchAll。
    ///
    /// 联动关系：补丁分两路。Patch_Pilot 改 CompShuttle.HasPilot / CanLaunch，决定谁能让飞船起飞。
    /// Patch_CommandRange 改 Pawn_MechanitorTracker.CanCommandTo 和 DrawCommandRadius，
    /// 让绑定者在机械师飞船旁边多出一圈可控范围。带宽和绑定名单在 Hediff_MechanitorShuttleLink，
    /// 由 CompMechanitorShuttleLink 的操作按钮写入。
    ///
    /// 注意：HarmonyId 与 About.xml 的 packageId 相同。补丁必须在构造函数里挂上。
    /// 本模组与 Estrella6066.odysseyTransportShuttle.zh 互相 incompatibleWith，因为两边都会注入同一套 DefInjected 键。
    /// </summary>
    public class OdysseyTransportShuttleEnhancedMod : Mod
    {
        public const string HarmonyId = "Estrella6066.odysseyTransportShuttleEnhanced";

        public OdysseyTransportShuttleEnhancedMod(ModContentPack content) : base(content)
        {
            Harmony harmony = new Harmony(HarmonyId);
            harmony.PatchAll(Assembly.GetExecutingAssembly());
        }
    }
}
