using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace DshEconomy
{
    /// <summary>
    /// 强制启用「财产制度」戒律：按文化的模因自动把对应戒律加进玩家文化，并禁止移除。
    /// 有集体主义/和睦团体 → 公有制戒律；否则 → 私有制戒律。
    /// </summary>
    public static class EconomyPreceptForcer
    {
        public const string PublicDef = "DshEco_PreceptPublicOwnership";
        public const string PrivateDef = "DshEco_PreceptPrivateOwnership";

        private static int lastRun = -99999;

        public static void Tick(Map map)
        {
            int now = Find.TickManager != null ? Find.TickManager.TicksGame : 0;
            if (now - lastRun < 2500) return;
            lastRun = now;
            Enforce(map);
        }

        private static bool inEnforce;

        private static bool ranOnce;

        /// <summary>读档/进图后补一次（此时文化数据才真正齐全），之后靠 RecachePrecepts 事件。</summary>
        public static void EnsureOnce(Map map)
        {
            if (ranOnce) return;
            ranOnce = true;
            Enforce(map);
        }

        public static void ResetOnce() { ranOnce = false; }

        public static void Enforce(Map map)
        {
            if (inEnforce) return;
            inEnforce = true;
            try
            {
                // 全局所有文化都补：文化界面里能看到的不止玩家派系的文化，
                // 只改玩家派系会出现「别的文化没有财产制度」。
                List<Ideo> all = new List<Ideo>();
                if (Find.IdeoManager != null && Find.IdeoManager.IdeosListForReading != null)
                {
                    for (int i = 0; i < Find.IdeoManager.IdeosListForReading.Count; i++)
                    {
                        Ideo id = Find.IdeoManager.IdeosListForReading[i];
                        if (id != null) all.Add(id);
                    }
                }
                if (all.Count == 0)
                {
                    Faction player = Faction.OfPlayer;
                    if (player == null || player.ideos == null) return;
                    all.AddRange(player.ideos.AllIdeos);
                }

                for (int k = 0; k < all.Count; k++) EnforceIdeo(all[k]);
            }
            catch (Exception e)
            {
                Log.Warning("[DSH-ECONOMY] 强制戒律失败：" + e.Message);
            }
            finally { inEnforce = false; }
        }

        private static void EnforceIdeo(Ideo ideo)
        {
            if (ideo == null) return;
            try
            {
                bool wantPublic = EconomyDoctrine.HasPublicMeme(ideo);
                string want = wantPublic ? PublicDef : PrivateDef;
                string other = wantPublic ? PrivateDef : PublicDef;

                PreceptDef def = DefDatabase<PreceptDef>.GetNamedSilentFail(want);
                if (def == null) return;

                // 先移除反向戒律
                List<Precept> list = new List<Precept>(ideo.PreceptsListForReading);
                for (int i = 0; i < list.Count; i++)
                {
                    Precept pc = list[i];
                    if (pc?.def == null) continue;
                    if (pc.def.defName == other)
                    {
                        Bypass = true;
                        try { ideo.RemovePrecept(pc, false); }
                        finally { Bypass = false; }
                        Log.Message("[DOCTRINE] 移除戒律 " + other);
                    }
                }

                // 已有正确戒律就不动
                list = new List<Precept>(ideo.PreceptsListForReading);
                for (int i = 0; i < list.Count; i++)
                    if (list[i]?.def != null && list[i].def.defName == want) return;

                Precept made = PreceptMaker.MakePrecept(def);
                if (made == null) return;
                ideo.AddPrecept(made, true, null);
                Log.Message("[DOCTRINE] 强制启用戒律 " + want + "（文化=" + ideo.name + "）");
            }
            catch (Exception e)
            {
                Log.Warning("[DSH-ECONOMY] 强制戒律失败（" + ideo.name + "）：" + e.Message);
            }
        }

        /// <summary>把「公有制」挂到公有模因上：文化说明里会列为规定戒律，生成时也会自动带上。</summary>
        public static void InjectMemeRequirements()
        {
            try
            {
                PreceptDef pub = DefDatabase<PreceptDef>.GetNamedSilentFail(PublicDef);
                if (pub == null) return;

                // PreceptDef.requiredMemes / associatedMemes 都是 List<MemeDef>，用反射拿字段
                System.Reflection.FieldInfo req = typeof(PreceptDef).GetField("requiredMemes",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                System.Reflection.FieldInfo assoc = typeof(PreceptDef).GetField("associatedMemes",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);

                if (req == null)
                {
                    Log.Warning("[DSH-ECONOMY] 未找到 PreceptDef.requiredMemes，跳过模因挂接");
                    return;
                }

                List<MemeDef> reqList = req.GetValue(pub) as List<MemeDef>;
                if (reqList == null) { reqList = new List<MemeDef>(); req.SetValue(pub, reqList); }
                List<MemeDef> assocList = assoc != null ? assoc.GetValue(pub) as List<MemeDef> : null;
                if (assocList == null && assoc != null) { assocList = new List<MemeDef>(); assoc.SetValue(pub, assocList); }

                string[] memes = EconomyDoctrine.PublicMemes;
                for (int i = 0; i < memes.Length; i++)
                {
                    MemeDef meme = DefDatabase<MemeDef>.GetNamedSilentFail(memes[i]);
                    if (meme == null) continue;
                    if (!reqList.Contains(meme)) reqList.Add(meme);
                    if (assocList != null && !assocList.Contains(meme)) assocList.Add(meme);
                    Log.Message("[DOCTRINE] 公有制 ← 模因 " + meme.defName);
                }

                // 无扩展文化时，赤胆忠心也算公有制
                if (!EconomyDoctrine.ExpansionCultureLoaded())
                {
                    MemeDef loyal = DefDatabase<MemeDef>.GetNamedSilentFail(EconomyDoctrine.LoyalistMeme);
                    if (loyal != null)
                    {
                        if (!reqList.Contains(loyal)) reqList.Add(loyal);
                        if (assocList != null && !assocList.Contains(loyal)) assocList.Add(loyal);
                        Log.Message("[DOCTRINE] 公有制 ← 模因 Loyalist");
                    }
                }
            }
            catch (Exception e)
            {
                Log.Warning("[DSH-ECONOMY] 注入模因必需戒律失败：" + e.Message);
            }
        }

        /// <summary>文化被编辑（加/删模因、改戒律）后原版会重算戒律，这里立刻跟着换向。</summary>
        public static void RecachePostfix()
        {
            if (inEnforce) return;
            Enforce(null);
        }

        /// <summary>为 true 时允许本类自己移除戒律（换向用），玩家操作仍然被拦。</summary>
        internal static bool Bypass;

        /// <summary>禁止玩家在文化里删掉/换掉这两条戒律。</summary>
        public static bool RemovePreceptPrefix(Precept precept)
        {
            if (Bypass) return true;
            if (precept?.def == null) return true;
            string dn = precept.def.defName;
            return dn != PublicDef && dn != PrivateDef;
        }

        /// <summary>原版据此决定文化界面里能不能点「移除」——这两条一律算必需。</summary>
        public static void PreceptIsRequiredPostfix(PreceptDef precept, ref bool __result)
        {
            if (precept == null) return;
            bool isPublicPrecept = precept.defName == PublicDef;
            bool isPrivatePrecept = precept.defName == PrivateDef;
            if (!isPublicPrecept && !isPrivatePrecept) return;

            // 只锁“当前文化该有的那一条”；反向/过期的那条允许被清掉，
            // 否则玩家删掉集体主义模因后公有制会永远赖着不走。
            Ideo ideo = Faction.OfPlayer?.ideos?.PrimaryIdeo;
            bool wantPublic = ideo != null && EconomyDoctrine.HasPublicMeme(ideo);
            if (isPublicPrecept == wantPublic) __result = true;
        }

        /// <summary>不许把制度换成与模因相反的那一条。</summary>
        public static bool CanAddPreceptPrefix(PreceptDef preceptDef, ref AcceptanceReport __result)
        {
            if (preceptDef == null) return true;
            string dn = preceptDef.defName;
            if (dn != PublicDef && dn != PrivateDef) return true;

            Ideo ideo = Faction.OfPlayer?.ideos?.PrimaryIdeo;
            bool wantPublic = ideo != null ? EconomyDoctrine.HasPublicMeme(ideo) : false;
            bool isPublicPrecept = dn == PublicDef;
            if (isPublicPrecept == wantPublic) return true;

            __result = false;
            return false;
        }
    }
}
