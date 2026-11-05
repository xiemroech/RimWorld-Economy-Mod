using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace DshEconomy
{
    /// <summary>
    /// 文化制度：私有制 / 公有制。
    /// - 默认（无相关文化）→ 强制私有制：社会化上限 50%（不允许免费分配）。
    /// - 集体主义 / 和睦团体 → 强制公有制：社会化下限 50%（至少半公共）。
    /// 也可在「社会化」页手动指定，覆盖自动判定。
    /// </summary>
    public static class EconomyDoctrine
    {
        public const float PrivateCap = 75f;    // 私有制下普通分类上限 75%（医疗/植入/成瘾品可到 100%）
        public const float PublicFloor = 50f;   // 公有制下社会化不得低于 50%

        public enum Mode { Auto = 0, ForcePrivate = 1, ForcePublic = 2 }

        /// <summary>会强制公有制的文化模因（collectivist = 集体主义；loyalist = 和睦团体）。</summary>
        public static readonly string[] PublicMemes = { "OA_RK_meme_Friendly", "RK_AlienSolidarity", "VME_HardcoreIndustrialism" };

        /// <summary>原版「赤胆忠心」：只有在没有文化扩展 mod 时才算公有制。</summary>
        public const string LoyalistMeme = "Loyalist";

        public static bool ExpansionCultureLoaded()
        {
            foreach (string n in PublicMemes)
                if (DefDatabase<MemeDef>.GetNamedSilentFail(n) != null) return true;
            return false;
        }

        public static bool IsPublicMeme(MemeDef m)
        {
            if (m == null) return false;
            for (int i = 0; i < PublicMemes.Length; i++)
                if (m.defName == PublicMemes[i]) return true;
            if (!ExpansionCultureLoaded() && m.defName == LoyalistMeme) return true;
            return false;
        }

        public static bool IsPublic(Map map)
        {
            MapEconomyComponent comp = map != null ? map.GetComponent<MapEconomyComponent>() : null;
            int mode = comp != null ? comp.doctrineMode : 0;
            if (mode == (int)Mode.ForcePublic || mode == (int)Mode.ForcePrivate) return mode == (int)Mode.ForcePublic;
            return AutoDetectPublic(map);
        }

        public const string PrivatePrecept = "DshEco_PreceptPrivateOwnership";
        public const string PublicPrecept = "DshEco_PreceptPublicOwnership";

        public static bool PreceptSaysPublic(Ideo ideo, out bool found)
        {
            found = false;
            if (ideo == null || ideo.precepts == null) return false;
            for (int i = 0; i < ideo.precepts.Count; i++)
            {
                Precept pc = ideo.precepts[i];
                if (pc == null || pc.def == null) continue;
                if (pc.def.defName == PublicPrecept) { found = true; return true; }
                if (pc.def.defName == PrivatePrecept) { found = true; return false; }
            }
            return false;
        }

        /// <summary>按“殖民地的文化”（玩家派系的主文化）判定，而不是按个人。</summary>
        public static bool AutoDetectPublic(Map map)
        {
            try
            {
                Faction player = Faction.OfPlayer;
                if (player == null || player.ideos == null) return false;

                Ideo primary = player.ideos.PrimaryIdeo;
                if (primary != null) return HasPublicMeme(primary);

                // 没有主文化（多文化派系）时：任意一个官方文化带公有制就算
                List<Ideo> all = new List<Ideo>(player.ideos.AllIdeos);
                for (int i = 0; i < all.Count; i++)
                {
                    Ideo id = all[i];
                    if (id == null) continue;
                    if (HasPublicMeme(id)) return true;
                }
            }
            catch { }
            return false;
        }

        private static int lastMemeLogTick = -99999;

        public static void LogMemeDiagnostics(Map map)
        {
            try
            {
                int now = Find.TickManager != null ? Find.TickManager.TicksGame : 0;
                if (now - lastMemeLogTick < 2500) return;
                lastMemeLogTick = now;
                Faction player = Faction.OfPlayer;
                Ideo ideo = player != null && player.ideos != null ? player.ideos.PrimaryIdeo : null;
                string list = "";
                if (ideo != null && ideo.memes != null)
                {
                    for (int i = 0; i < ideo.memes.Count; i++)
                    {
                        MemeDef m = ideo.memes[i];
                        if (m == null) continue;
                        list += m.defName + "(" + SafeLabel(m) + ") ";
                    }
                }
                string pre = "";
                if (ideo != null)
                {
                    List<Precept> ps = ideo.PreceptsListForReading;
                    for (int i = 0; i < ps.Count; i++)
                    {
                        Precept pc = ps[i];
                        if (pc == null || pc.def == null) continue;
                        if (pc.def.defName == "DshEco_PreceptPublicOwnership" || pc.def.defName == "DshEco_PreceptPrivateOwnership")
                            pre += pc.def.defName + " ";
                    }
                }
                Log.Message("[DOCTRINE] 文化=" + (ideo != null ? ideo.name : "无")
                    + " 模因=[" + list.Trim() + "] 本mod戒律=[" + pre.Trim() + "] → " + (IsPublic(map) ? "公有制" : "私有制"));
            }
            catch { }
        }

        private static string SafeLabel(Def d)
        {
            try { return d.LabelCap.ToString(); } catch { return d.defName; }
        }

        public static bool HasPublicMeme(Ideo ideo)
        {
            if (ideo == null || ideo.memes == null) return false;
            for (int i = 0; i < ideo.memes.Count; i++)
            {
                MemeDef m = ideo.memes[i];
                if (m == null) continue;
                string dn = m.defName ?? "";
                string lb = "";
                try { lb = m.LabelCap.ToString(); } catch { }
                if (IsPublicMeme(m)) return true;   // 和睦团体 / 齐心协力，或（无扩展时）赤胆忠心
                if (lb.Contains("和睦") || lb.Contains("齐心协力")) return true;
                if (!ExpansionCultureLoaded() && lb.Contains("赤胆忠心")) return true;
            }
            return false;
        }

        /// <summary>兼容旧调用：按制度给出取值。</summary>
        /// <summary>私有制下，食物与基础消费品的社会化不得超过 75%；其它分类不限；公有制完全不限。</summary>
        public const float ExpenseCap = 75f;

        public static bool IsBasicConsumable(EconomyCategoryDef cat)
        {
            if (cat == null) return false;
            string n = cat.defName;
            return n == "DshEco_Food" || n == "DshEco_Medicine" || n == "DshEco_Drugs";
        }

        /// <summary>私有制下的下限：只有医疗、医用植入物（仿生/血清/基因/器官）、成瘾品可以低于 50%。</summary>
        public const float PrivateFloor = 50f;

        public static bool FreeBelowHalf(EconomyCategoryDef cat)
        {
            if (cat == null) return false;
            string n = cat.defName;
            return n == "DshEco_Medicine" || n == "DshEco_Augment" || n == "DshEco_Drugs";
        }

        public static float Cap(Map map, EconomyCategoryDef cat, float value)
        {
            float v = Mathf.Clamp(value, 0f, 100f);
            if (IsPublic(map)) return v;                     // 公有制不限
            if (FreeBelowHalf(cat)) return v;                // 医疗 / 植入物 / 成瘾品 0–100%
            return Mathf.Min(ExpenseCap, v);                 // 私有制下其余分类 0–75%
        }

        /// <summary>公有制下税制不得低于平均税（只能比例或累进）。</summary>
        public static void EnforceTax(Map map, TaxPolicy policy)
        {
            if (policy == null || !IsPublic(map)) return;
            // 让三档权重非递减：低 ≤ 中 ≤ 高（比例税 = 三者相等）
            if (policy.lowWeight > policy.midWeight) policy.lowWeight = policy.midWeight;
            if (policy.midWeight > policy.highWeight) policy.midWeight = policy.highWeight;
            if (policy.lowWeight > policy.midWeight) policy.lowWeight = policy.midWeight;
        }

        public static string Describe(Map map)
        {
            MapEconomyComponent comp = map != null ? map.GetComponent<MapEconomyComponent>() : null;
            int mode = comp != null ? comp.doctrineMode : 0;
            bool pub = IsPublic(map);
            string name = pub ? "公有制" : "私有制";
            string src = mode == (int)Mode.ForcePublic || mode == (int)Mode.ForcePrivate
                ? "手动指定" : (pub ? "文化：集体主义/和睦团体" : "默认：无相关文化");
            return "当前制度：" + name + "（" + src + "，社会化"
                + (pub ? "下限 " + PublicFloor.ToString("0") : "上限 " + PrivateCap.ToString("0")) + "%）";
        }
    }
}
