using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace OdysseyTransportShuttleEnhanced
{
    /// <summary>
    /// 记在机械师身上的绑定。一艘飞船一份引用，带宽是 8 乘以仍存在的飞船数。
    ///
    /// 联动关系：CompMechanitorShuttleLink 在绑定、解除、摧毁时调用 AddShuttle / RemoveShuttle。
    /// CurStage 的 statOffsets 写入 MechBandwidth，Pawn_MechanitorTracker.TotalBandwidth 读这个属性。
    /// 数量变化时要先丢掉缓存的 HediffStage，再 Notify_BandwidthChanged，否则控制名单还按旧带宽算。
    /// Patch_CommandRange 通过 Of 和 Covers 读取这份名单。
    ///
    /// 注意：建筑 tickerType 为 Normal，飞船飞在世界地图上时组件不再 Tick。
    /// 本 Hediff 跟着机械师走，用 PostTickInterval 把已经摧毁的飞船剔掉。取消生成但没摧毁的飞船要留着，带宽才不会在飞行中断掉。
    /// 名单空了之后 ShouldRemove 为真，游戏会把它摘掉。摘掉前 Notify 已经按 0 艘算过。
    /// </summary>
    public class Hediff_MechanitorShuttleLink : Hediff
    {
        public const int BandwidthPerShuttle = 8;

        public const float CommandRadius = 24.9f;

        public const float CommandRadiusSquared = 620.01f;

        private List<Thing> shuttles = new List<Thing>();

        private HediffStage curStage;

        private int lastNotifiedCount = -1;

        public override bool ShouldRemove => shuttles.Count == 0;

        public override HediffStage CurStage
        {
            get
            {
                if (shuttles.Count == 0)
                {
                    return null;
                }

                if (curStage == null)
                {
                    StatModifier modifier = new StatModifier();
                    modifier.stat = StatDefOf.MechBandwidth;
                    modifier.value = shuttles.Count * BandwidthPerShuttle;
                    curStage = new HediffStage();
                    curStage.statOffsets = new List<StatModifier> { modifier };
                }

                return curStage;
            }
        }

        /// <summary>
        /// 供绑定上限计数。飞行中的飞船不在地图上，只留在这份名单里，调用方要把它和地图上正在等待绑定的船合并。
        /// </summary>
        public void CollectShuttles(HashSet<Thing> result)
        {
            if (result == null)
            {
                return;
            }

            for (int i = 0; i < shuttles.Count; i++)
            {
                Thing shuttle = shuttles[i];
                if (shuttle != null && !shuttle.Destroyed)
                {
                    result.Add(shuttle);
                }
            }
        }

        public static Hediff_MechanitorShuttleLink Of(Pawn pawn)
        {
            if (pawn?.health?.hediffSet == null)
            {
                return null;
            }

            HediffDef def = DefDatabase<HediffDef>.GetNamedSilentFail("OTS_MechanitorShuttleBandwidth");
            if (def == null)
            {
                return null;
            }

            return pawn.health.hediffSet.GetFirstHediffOfDef(def) as Hediff_MechanitorShuttleLink;
        }

        public static void AddShuttle(Pawn pawn, Thing shuttle)
        {
            if (pawn == null || shuttle == null || OTSDefOf.OTS_MechanitorShuttleBandwidth == null)
            {
                return;
            }

            Hediff_MechanitorShuttleLink link = Of(pawn);
            if (link == null)
            {
                link = (Hediff_MechanitorShuttleLink)HediffMaker.MakeHediff(OTSDefOf.OTS_MechanitorShuttleBandwidth, pawn);
                link.TryAdd(shuttle);
                pawn.health.AddHediff(link);
                link.NotifyIfCountChanged();
                return;
            }

            if (link.TryAdd(shuttle))
            {
                link.NotifyIfCountChanged();
            }
        }

        public static void RemoveShuttle(Pawn pawn, Thing shuttle)
        {
            Hediff_MechanitorShuttleLink link = Of(pawn);
            if (link != null && link.TryRemove(shuttle))
            {
                link.NotifyIfCountChanged();
            }
        }

        public bool Covers(Map map, IntVec3 cell)
        {
            if (map == null)
            {
                return false;
            }

            for (int i = 0; i < shuttles.Count; i++)
            {
                Thing shuttle = shuttles[i];
                if (shuttle == null || shuttle.Destroyed || !shuttle.Spawned || shuttle.Map != map)
                {
                    continue;
                }

                if (shuttle.Position.DistanceToSquared(cell) < CommandRadiusSquared)
                {
                    return true;
                }
            }

            return false;
        }

        public void DrawRadii(Pawn_MechanitorTracker tracker)
        {
            Map map = tracker.Pawn.MapHeld;
            if (map == null)
            {
                return;
            }

            for (int i = 0; i < shuttles.Count; i++)
            {
                Thing shuttle = shuttles[i];
                if (shuttle == null || shuttle.Destroyed || !shuttle.Spawned || shuttle.Map != map)
                {
                    continue;
                }

                GenDraw.DrawRadiusRing(shuttle.Position, CommandRadius, Color.white, (IntVec3 cell) => tracker.CanCommandTo(cell));
            }
        }

        public override void PostTickInterval(int delta)
        {
            base.PostTickInterval(delta);
            if (!pawn.IsHashIntervalTick(60, delta))
            {
                return;
            }

            if (PruneDestroyed())
            {
                NotifyIfCountChanged();
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref shuttles, "otsShuttles", LookMode.Reference);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (shuttles == null)
                {
                    shuttles = new List<Thing>();
                }

                PruneDestroyed();
                curStage = null;
                lastNotifiedCount = shuttles.Count;
            }
        }

        private bool TryAdd(Thing shuttle)
        {
            if (shuttles.Contains(shuttle))
            {
                return false;
            }

            shuttles.Add(shuttle);
            curStage = null;
            return true;
        }

        private bool TryRemove(Thing shuttle)
        {
            if (!shuttles.Remove(shuttle))
            {
                return false;
            }

            curStage = null;
            return true;
        }

        private bool PruneDestroyed()
        {
            int removed = shuttles.RemoveAll((Thing shuttle) => shuttle == null || shuttle.Destroyed);
            if (removed > 0)
            {
                curStage = null;
                return true;
            }

            return false;
        }

        private void NotifyIfCountChanged()
        {
            if (lastNotifiedCount == shuttles.Count)
            {
                return;
            }

            lastNotifiedCount = shuttles.Count;
            pawn.mechanitor?.Notify_BandwidthChanged();
        }
    }
}
