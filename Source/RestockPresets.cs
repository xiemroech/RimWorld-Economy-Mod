using System.Collections.Generic;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace DshEconomy
{
    /// <summary>补货预设：存在 mod 设置里，格式 "名字|defName:min:max,defName:min:max"。</summary>
    public static class RestockPresets
    {
        public static List<string> List => DshEconomyMod.Settings.restockPresets;

        public static string NameOf(string entry)
        {
            int i = entry.IndexOf('|');
            return i < 0 ? entry : entry.Substring(0, i);
        }

        public static string Save(string name, List<RestockRule> rules)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(string.IsNullOrEmpty(name) ? "预设" : name.Trim());
            sb.Append('|');
            bool first = true;
            foreach (RestockRule r in rules)
            {
                if (r == null || r.def == null) continue;
                if (!first) sb.Append(',');
                first = false;
                sb.Append(r.def.defName).Append(':').Append(Mathf.RoundToInt(r.pct));
            }
            string entry = sb.ToString();
            List.RemoveAll(x => NameOf(x) == NameOf(entry));
            List.Add(entry);
            return entry;
        }

        public static List<RestockRule> Decode(string entry)
        {
            List<RestockRule> outList = new List<RestockRule>();
            int i = entry.IndexOf('|');
            if (i < 0) return outList;
            string body = entry.Substring(i + 1);
            foreach (string part in body.Split(','))
            {
                if (string.IsNullOrEmpty(part)) continue;
                string[] bits = part.Split(':');
                if (bits.Length < 2) continue;
                ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(bits[0]);
                if (def == null) continue;
                int pct;
                if (!int.TryParse(bits[1], out pct)) pct = 10;
                outList.Add(new RestockRule
                {
                    def = def,
                    pct = Mathf.Clamp(pct, 0, 100),
                    minPct = 0f,
                    maxPct = 0f
                });
            }
            return outList;
        }

        public static void Apply(string entry, RestockSettings comp)
        {
            if (comp == null) return;
            List<RestockRule> rules = Decode(entry);
            comp.rules.Clear();
            foreach (RestockRule r in rules) comp.rules.Add(r);
            comp.detailed = true;
        }
    }
}
