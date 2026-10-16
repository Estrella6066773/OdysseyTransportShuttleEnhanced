using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace OdysseyTransportShuttleEnhanced
{
    /// <summary>
    /// 挂在 MechanitorShuttle 上的绑定组件。操作按钮仿照频带节点：打开名单，选一名机械师。
    ///
    /// 联动关系：第一次绑定立刻写入 boundMechanitor，并调用 Hediff_MechanitorShuttleLink 提供带宽。
    /// 解除绑定，或改绑另一名机械师，会先摘掉当前带宽，再把 rebindReadyTick 设成一天之后。
    /// 这一天里 Patch_CanLaunch 拒绝起飞，绑定按钮关闭。已经选定的新机械师要等这一天结束，TryFinishReset 才把带宽加上。
    /// 一艘飞船同时只绑一位。同一位机械师最多绑定 3 艘，每艘各提供 8 点机械带宽，算法与频带节点按座叠加相同。
    /// 已绑定和正在等待生效的都算进这 3 艘。飞行中的飞船不在地图上，只记在机械师的 Hediff 里，计数时要和地图上的船合并，不能漏掉。
    /// Patch_Pilot 另行读取舱内有没有机械师，不读这个绑定；没绑定不影响谁能开，绑定也不把驾驶权收成只有这一位。
    ///
    /// 注意：BindIcon 在静态字段里用 ContentFinder 取贴图，必须标 StaticConstructorOnStartup。
    /// 否则启动时的缺特性检查会在后台线程第一次碰到这个类，贴图加载不在主线程，游戏会直接退出。
    /// 上一份日志就停在这条警告上，后面没有托管异常。
    /// 原版定义没有 tickerType，默认是 Never。Biotech\Patches\MechanitorShuttle.xml 把它改成 Normal，CompTick 才会被调用。
    /// 游戏不会因此调用 CompTickRare，所以清理放在 CompTick，并且每 250 tick 才做一次。一天的等待也靠这次 CompTick 收尾。
    /// 飞走之后 Thing 还在、只是取消生成，PostDestroy 不会发生，绑定和带宽保留。真正摧毁时 PostDestroy 清掉绑定，不再进入这一天的等待。
    /// 存档用 Scribe_References，等待截止时刻用 Scribe_Values。读档后 PostSpawnSetup 把 Hediff 补回去，并在等待已经到期时完成重新绑定。
    /// 环世界的一天是 60000 tick。截止时刻用 TicksGame，地图卸载期间时间仍在走，重新进图时补完成。
    /// </summary>
    public class CompProperties_MechanitorShuttleLink : CompProperties
    {
        public CompProperties_MechanitorShuttleLink()
        {
            compClass = typeof(CompMechanitorShuttleLink);
        }
    }

    [StaticConstructorOnStartup]
    public class CompMechanitorShuttleLink : ThingComp
    {
        public const int RebindTicks = 60000;

        public const int MaxShipsPerMechanitor = 3;

        private Pawn boundMechanitor;

        private Pawn pendingMechanitor;

        private int rebindReadyTick = -1;

        private static readonly Texture2D BindIcon = ContentFinder<Texture2D>.Get("UI/Gizmos/BandNodeTuning", false);

        public Pawn BoundMechanitor => boundMechanitor;

        public bool IsResetting => rebindReadyTick >= 0 && Find.TickManager.TicksGame < rebindReadyTick;

        public int TicksLeft => IsResetting ? rebindReadyTick - Find.TickManager.TicksGame : 0;

        public static bool IsPlayerMechanitor(Pawn pawn)
        {
            return pawn != null
                && !pawn.Dead
                && pawn.Faction == Faction.OfPlayer
                && MechanitorUtility.IsMechanitor(pawn);
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_References.Look(ref boundMechanitor, "otsBoundMechanitor");
            Scribe_References.Look(ref pendingMechanitor, "otsPendingMechanitor");
            Scribe_Values.Look(ref rebindReadyTick, "otsRebindReadyTick", -1);
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            EnsureLink();
        }

        public override void CompTick()
        {
            TryFinishReset();
            if (parent.IsHashIntervalTick(250))
            {
                EnsureLink();
            }
        }

        public override void PostDestroy(DestroyMode mode, Map previousMap)
        {
            base.PostDestroy(mode, previousMap);
            ClearBinding();
        }

        public override string CompInspectStringExtra()
        {
            if (IsResetting)
            {
                string timeLeft = TicksLeft.ToStringTicksToPeriod();
                if (CompMechanitorShuttleLink.IsPlayerMechanitor(pendingMechanitor))
                {
                    return "OTS.Rebinding".Translate(pendingMechanitor.LabelShort, timeLeft);
                }

                return "OTS.Resetting".Translate(timeLeft);
            }

            if (boundMechanitor == null)
            {
                return "OTS.Unbound".Translate();
            }

            return "OTS.BoundTo".Translate(boundMechanitor.LabelShort);
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo gizmo in base.CompGetGizmosExtra())
            {
                yield return gizmo;
            }

            List<Pawn> candidates = new List<Pawn>();
            CollectMechanitors(candidates);

            Command_Action command = new Command_Action();
            command.defaultLabel = (boundMechanitor == null ? "OTS.BindTo".Translate() : "OTS.RebindTo".Translate()) + "...";
            // {1} 用 ToStringTicksToPeriod，句子里不再写「天」。游戏按当前语言填上数字和单位，例如「1天」。
            command.defaultDesc = "OTS.BindDesc".Translate(Hediff_MechanitorShuttleLink.BandwidthPerShuttle, RebindTicks.ToStringTicksToPeriod());
            command.icon = BindIcon;
            if (IsResetting)
            {
                command.Disable("OTS.RebindWait".Translate(TicksLeft.ToStringTicksToPeriod()));
            }
            else if (candidates.Count == 0 && boundMechanitor == null)
            {
                command.Disable("OTS.NoMechanitor".Translate());
            }
            else
            {
                command.action = OpenBindMenu;
            }

            yield return command;
        }

        public void Bind(Pawn pawn)
        {
            if (IsResetting || !IsPlayerMechanitor(pawn) || boundMechanitor == pawn)
            {
                return;
            }

            if (!WithinBindLimit(pawn))
            {
                return;
            }

            if (boundMechanitor != null)
            {
                BeginReset(pawn);
                return;
            }

            ApplyBind(pawn);
        }

        public void Unbind()
        {
            if (IsResetting || boundMechanitor == null)
            {
                return;
            }

            BeginReset(null);
        }

        /// <summary>
        /// 摧毁时调用。只摘掉带宽，不开始一天的等待。等待是给还在的飞船用的。
        /// </summary>
        private void ClearBinding()
        {
            Pawn pawn = boundMechanitor;
            boundMechanitor = null;
            pendingMechanitor = null;
            rebindReadyTick = -1;
            if (pawn != null)
            {
                Hediff_MechanitorShuttleLink.RemoveShuttle(pawn, parent);
            }
        }

        /// <summary>
        /// 立刻失去当前带宽。next 有值时，一天结束后绑定到这个人；没有值时，一天结束后保持未绑定。
        /// 已经在等待时不能再调用，否则会把截止时刻往后推。
        /// </summary>
        private void BeginReset(Pawn next)
        {
            Pawn pawn = boundMechanitor;
            boundMechanitor = null;
            if (pawn != null)
            {
                Hediff_MechanitorShuttleLink.RemoveShuttle(pawn, parent);
            }

            pendingMechanitor = IsPlayerMechanitor(next) ? next : null;
            rebindReadyTick = Find.TickManager.TicksGame + RebindTicks;
        }

        private void ApplyBind(Pawn pawn)
        {
            pendingMechanitor = null;
            rebindReadyTick = -1;
            boundMechanitor = pawn;
            Hediff_MechanitorShuttleLink.AddShuttle(pawn, parent);
        }

        private void TryFinishReset()
        {
            if (rebindReadyTick < 0)
            {
                return;
            }

            if (pendingMechanitor != null && !IsPlayerMechanitor(pendingMechanitor))
            {
                pendingMechanitor = null;
            }

            if (Find.TickManager.TicksGame < rebindReadyTick)
            {
                return;
            }

            Pawn next = pendingMechanitor;
            pendingMechanitor = null;
            rebindReadyTick = -1;
            if (IsPlayerMechanitor(next) && WithinBindLimit(next))
            {
                ApplyBind(next);
            }
        }

        private void EnsureLink()
        {
            TryFinishReset();
            if (IsResetting)
            {
                return;
            }

            if (boundMechanitor != null && !IsPlayerMechanitor(boundMechanitor))
            {
                BeginReset(null);
                return;
            }

            if (boundMechanitor != null)
            {
                Hediff_MechanitorShuttleLink.AddShuttle(boundMechanitor, parent);
            }
        }

        private void OpenBindMenu()
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>();
            if (boundMechanitor != null)
            {
                options.Add(new FloatMenuOption("OTS.Unbind".Translate(), Unbind));
            }

            List<Pawn> candidates = new List<Pawn>();
            CollectMechanitors(candidates);
            for (int i = 0; i < candidates.Count; i++)
            {
                Pawn pawn = candidates[i];
                if (pawn == boundMechanitor)
                {
                    continue;
                }

                Pawn localPawn = pawn;
                if (BindingCount(localPawn, parent) >= MaxShipsPerMechanitor)
                {
                    options.Add(new FloatMenuOption(localPawn.LabelShortCap + " (" + "OTS.BindLimit".Translate(MaxShipsPerMechanitor) + ")", null));
                }
                else
                {
                    options.Add(new FloatMenuOption(localPawn.LabelShortCap, () => Bind(localPawn)));
                }
            }

            if (options.Count == 0)
            {
                options.Add(new FloatMenuOption("OTS.NoMechanitor".Translate(), null));
            }

            Find.WindowStack.Add(new FloatMenu(options));
        }

        /// <summary>
        /// 这艘飞船自己不算。已经绑给此人的、以及正在等一天后绑给此人的，都算。
        /// 超过上限时弹出新句子，不改已有说明。等待结束时若名额已被占满，这次绑定不生效。
        /// </summary>
        private bool WithinBindLimit(Pawn pawn)
        {
            if (BindingCount(pawn, parent) < MaxShipsPerMechanitor)
            {
                return true;
            }

            Messages.Message("OTS.BindLimit".Translate(MaxShipsPerMechanitor), parent, MessageTypeDefOf.RejectInput);
            return false;
        }

        private static int BindingCount(Pawn pawn, Thing except)
        {
            HashSet<Thing> ships = new HashSet<Thing>();
            Hediff_MechanitorShuttleLink.Of(pawn)?.CollectShuttles(ships);
            ThingDef shuttleDef = DefDatabase<ThingDef>.GetNamedSilentFail("MechanitorShuttle");
            if (shuttleDef != null)
            {
                List<Map> maps = Find.Maps;
                for (int i = 0; i < maps.Count; i++)
                {
                    List<Thing> things = maps[i].listerThings.ThingsOfDef(shuttleDef);
                    for (int j = 0; j < things.Count; j++)
                    {
                        CompMechanitorShuttleLink link = things[j].TryGetComp<CompMechanitorShuttleLink>();
                        if (link == null)
                        {
                            continue;
                        }

                        if (link.boundMechanitor == pawn || (link.IsResetting && link.pendingMechanitor == pawn))
                        {
                            ships.Add(things[j]);
                        }
                    }
                }
            }

            ships.Remove(except);
            int count = 0;
            foreach (Thing shuttle in ships)
            {
                if (shuttle != null && !shuttle.Destroyed)
                {
                    count++;
                }
            }

            return count;
        }

        private void CollectMechanitors(List<Pawn> result)
        {
            Map map = parent.Map;
            if (map == null)
            {
                return;
            }

            foreach (Pawn spawned in map.mapPawns.AllPawnsSpawned)
            {
                if (IsPlayerMechanitor(spawned))
                {
                    result.Add(spawned);
                }
            }

            ThingOwner container = parent.TryGetComp<CompTransporter>()?.innerContainer;
            if (container == null)
            {
                return;
            }

            for (int i = 0; i < container.Count; i++)
            {
                if (container[i] is Pawn pawn && IsPlayerMechanitor(pawn) && !result.Contains(pawn))
                {
                    result.Add(pawn);
                }
            }
        }
    }
}
