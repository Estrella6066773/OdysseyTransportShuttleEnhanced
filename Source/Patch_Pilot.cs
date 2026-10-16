using HarmonyLib;
using RimWorld;
using Verse;

namespace OdysseyTransportShuttleEnhanced
{
    /// <summary>
    /// 改起飞资格。原版 CompShuttle.HasPilot 要求舱内有自由殖民者，且驾驶能力大于 0.1。
    ///
    /// 联动关系：CompLaunchable.CanLaunch 在通过燃料、屋顶、冷却和载重检查之后，返回 Shuttle.CanLaunch。
    /// CanLaunch 在飞船已生成且 HasPilot 为假时返回 CommandShuttleNoPilot。本类用 Prefix 换掉 HasPilot 的结果，
    /// 所以后面的天气检查仍然有效。
    ///
    /// 太空无人机：HasPilot 恒为真。空船也可以起飞，对应说明里的「不需要驾驶员」。
    /// 机械师飞船：玩家阵营的机械师或机械族都能起飞，不看驾驶能力。
    /// 目的地分段在 ShuttleArrivalCascade，不在这里按乘员改流程。
    /// 没有这两类乘员时不能起飞，不要退回「任何人有驾驶能力就能开」。
    /// </summary>
    [HarmonyPatch(typeof(CompShuttle), nameof(CompShuttle.HasPilot), MethodType.Getter)]
    public static class Patch_HasPilot
    {
        public static bool Prefix(CompShuttle __instance, ref bool __result)
        {
            string defName = __instance.parent.def.defName;
            if (defName == "TransportDrone")
            {
                __result = true;
                return false;
            }

            if (defName != "MechanitorShuttle")
            {
                return true;
            }

            __result = MechanitorShuttleCrew.HasPilot(__instance.Transporter?.innerContainer);
            return false;
        }
    }

    /// <summary>
    /// 机械师飞船没有机械师在船上时，把原版「没有驾驶员」换成说明原因的句子。
    /// 天气导致的失败发生在 HasPilot 已经为真之后，那些句子不能换掉。
    /// </summary>
    [HarmonyPatch(typeof(CompShuttle), nameof(CompShuttle.CanLaunch), MethodType.Getter)]
    public static class Patch_CanLaunch
    {
        public static void Postfix(CompShuttle __instance, ref AcceptanceReport __result)
        {
            if (__instance.parent.def.defName != "MechanitorShuttle")
            {
                return;
            }

            CompMechanitorShuttleLink link = __instance.parent.GetComp<CompMechanitorShuttleLink>();
            if (link != null && link.IsResetting)
            {
                __result = "OTS.RebindWait".Translate(link.TicksLeft.ToStringTicksToPeriod());
                return;
            }

            if (__result.Accepted)
            {
                return;
            }

            if (__result.Reason != "CommandShuttleNoPilot".Translate())
            {
                return;
            }

            __result = "OTS.NeedMechanitorAboard".Translate();
        }
    }

    /// <summary>
    /// 机械师飞船的乘员判定。机械师和机械族都看玩家阵营，不看驾驶能力。
    /// HasPilot 只决定能不能起飞。只有机械族、没有机械师时，ShuttleArrivalCascade 不提供交货，投送失败则整船返回。
    /// </summary>
    public static class MechanitorShuttleCrew
    {
        public static bool IsPlayerMechanoid(Pawn pawn)
        {
            return pawn != null && !pawn.Dead && pawn.Faction == Faction.OfPlayer && pawn.RaceProps.IsMechanoid;
        }

        public static bool HasPilot(ThingOwner container)
        {
            if (container == null)
            {
                return false;
            }

            for (int i = 0; i < container.Count; i++)
            {
                if (container[i] is Pawn pawn && (CompMechanitorShuttleLink.IsPlayerMechanitor(pawn) || IsPlayerMechanoid(pawn)))
                {
                    return true;
                }
            }

            return false;
        }

        public static bool OnlyMechanoids(CompLaunchable launchable)
        {
            if (launchable?.parent?.def?.defName != "MechanitorShuttle")
            {
                return false;
            }

            CompTransporter transporter = launchable.parent.GetComp<CompTransporter>();
            ThingOwner container = transporter?.innerContainer;
            if (container == null)
            {
                return false;
            }

            bool mechanitor = false;
            bool mechanoid = false;
            for (int i = 0; i < container.Count; i++)
            {
                if (!(container[i] is Pawn pawn))
                {
                    continue;
                }

                if (CompMechanitorShuttleLink.IsPlayerMechanitor(pawn))
                {
                    mechanitor = true;
                }
                else if (IsPlayerMechanoid(pawn))
                {
                    mechanoid = true;
                }
            }

            return mechanoid && !mechanitor;
        }
    }
}
