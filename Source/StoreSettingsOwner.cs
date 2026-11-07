using System.Collections.Generic;
using RimWorld;
using Verse;

namespace DshEconomy
{
    /// <summary>
    /// 让"私有容器"在原版自己的过滤器判定里就只接受物主的东西：
    /// 原版 TryFindBestBetterStoreCellForWorker 第一行就是 slotGroup.Settings.AllowedToAccept(t)，
    /// 为假时直接跳过这个组去下一个 —— 这才是原版的切换方式，不需要事后否决格子。
    /// </summary>
    public static class StorageSettingsOwnerIndex
    {
        private static readonly Dictionary<StorageSettings, Pawn> cache = new Dictionary<StorageSettings, Pawn>();
        private static int builtTick = -99999;

        /// <summary>兜底扫描时临时置真：只按容器原始过滤器判断，避免陈旧产权缓存误拒公共组。</summary>
        public static bool Bypass;

        public static void Rebuild(Map map)
        {
            cache.Clear();
            builtTick = Find.TickManager != null ? Find.TickManager.TicksGame : 0;
            if (map == null) return;
            if (map.listerBuildings == null) return;
            EconomyRealEstate inst = Current.Game != null ? Current.Game.GetComponent<EconomyRealEstate>() : null;
            if (inst == null) return;
            foreach (Building b in map.listerBuildings.allBuildingsColonist)
            {
                Building_Storage st = b as Building_Storage;
                if (st == null) continue;
                Pawn owner = EconomyRealEstate.OwnerOfBuilding(st);
                if (owner == null) continue;
                StorageSettings ss = null;
                try { ss = st.GetStoreSettings(); } catch { }
                if (ss != null) cache[ss] = owner;

                // 存储 mod 的容器常常自带另一份 settings：把存放组那份也登记上
                try
                {
                    SlotGroup g = st.GetSlotGroup();
                    if (g != null && g.Settings != null && !ReferenceEquals(g.Settings, ss))
                        cache[g.Settings] = owner;
                }
                catch { }
            }
        }

        public static Pawn OwnerOf(StorageSettings s)
        {
            if (s == null) return null;
            Pawn p;
            return cache.TryGetValue(s, out p) ? p : null;
        }

        public static void EnsureFresh(Map map)
        {
            int now = Find.TickManager != null ? Find.TickManager.TicksGame : 0;
            if (now - builtTick > 2500) Rebuild(map);
        }

        public static void Postfix(StorageSettings __instance, Thing t, ref bool __result)
        {
            if (Bypass) return;
            if (!__result || __instance == null || t == null) return;
            try
            {
                Pawn owner = OwnerOf(__instance);
                if (owner == null) return;
                if (EconomyPrivate.OwnerOf(t) != owner) __result = false;
            }
            catch { }
        }
    }
}
