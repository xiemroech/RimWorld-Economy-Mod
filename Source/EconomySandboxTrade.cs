using System.Linq;
using RimWorld;
using Verse;

namespace DshEconomy
{
    /// <summary>测试沙盒用：召唤商队 / 立刻开一次贸易。</summary>
    public static class EconomySandboxTrade
    {
        public static string SummonCaravan(Map map)
        {
            if (map == null) return "没有地图";
            IncidentDef def = IncidentDefOf.TraderCaravanArrival;
            IncidentParms parms = StorytellerUtility.DefaultParmsNow(IncidentCategoryDefOf.Misc, map);
            bool ok = def.Worker.TryExecute(parms);
            Log.Message("[SANDBOX] 召唤商队：" + (ok ? "已派出" : "失败"));
            return ok ? "商队已出发，稍等片刻到图边缘" : "召唤失败（可能没有可用的友好派系）";
        }

        public static string OpenOrbitalTrade(Map map)
        {
            if (map == null) return "没有地图";
            IncidentDef def = IncidentDefOf.OrbitalTraderArrival;
            IncidentParms parms = StorytellerUtility.DefaultParmsNow(IncidentCategoryDefOf.Misc, map);
            bool ok = def.Worker.TryExecute(parms);
            if (!ok) return "轨道商船呼叫失败（可能已有船在轨道上）";

            TradeShip ship = map.passingShipManager?.passingShips?.OfType<TradeShip>().FirstOrDefault();
            if (ship == null)
            {
                Log.Message("[SANDBOX] 轨道商船：已呼叫但没找到船对象");
                return "船已在轨道上，点开信件里的「交易」即可";
            }
            return OpenTrade(map, ship);
        }

        public static string OpenTradeWithNearestTrader(Map map)
        {
            if (map == null) return "没有地图";
            Pawn trader = map.mapPawns.AllPawnsSpawned
                .Where(p => p.Faction != null && p.Faction != Faction.OfPlayer && p.RaceProps.Humanlike)
                .OrderBy(p => p.Position.DistanceToSquared(map.Center))
                .FirstOrDefault();
            if (trader == null) return "图里没有可交易的访客";
            ITrader t = trader as ITrader;
            if (t == null) return "最近的访客不是商人";
            return OpenTrade(map, t);
        }

        /// <summary>给当前交易对象塞白银，让商队也能收购（原版商队的采购力来自随身白银）。</summary>
        public static string GiveTraderSilver(int amount)
        {
            ITrader t = null;
            try { t = TradeSession.trader; } catch { }
            if (t == null) return "当前没有交易对象";
            Pawn p = t as Pawn;
            if (p == null) return "该商人不是实体商队（轨道船自带白银），无需加钱";
            Thing silver = ThingMaker.MakeThing(ThingDefOf.Silver);
            silver.stackCount = UnityEngine.Mathf.Max(1, amount);
            bool added = p.inventory.innerContainer.TryAdd(silver);
            if (!added) return "塞白银失败：商人背包放不下";
            Log.Message("[SANDBOX] 给商人 " + p.LabelShort + " 塞了 " + amount + " 白银");
            return "已给 " + p.LabelShort + " 塞了 " + amount + " 白银，重新打开交易窗口即可收购";
        }

        public static string OpenTrade(Map map, ITrader trader)
        {
            Pawn negotiator = map.mapPawns.FreeColonists
                .Where(p => !p.Downed && p.health.capacities.CapableOf(PawnCapacityDefOf.Talking))
                .OrderByDescending(p => p.skills?.GetSkill(SkillDefOf.Social)?.Level ?? 0)
                .FirstOrDefault();
            if (negotiator == null) return "没有能说话的殖民者";
            Find.WindowStack.Add(new Dialog_Trade(negotiator, trader));
            Log.Message("[SANDBOX] 立即贸易：谈判者=" + negotiator.LabelShort + " 商人=" + trader.TraderName);
            return "已打开与「" + (trader.TraderKind != null ? trader.TraderKind.label : trader.TraderName) + "」的交易窗口";
        }
    }
}
