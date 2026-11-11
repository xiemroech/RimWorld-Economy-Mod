using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace DshEconomy
{
    /// <summary>医疗服务费政策：医生按医疗等级向病人收费。</summary>
    public class MedicalPolicy : IExposable
    {
        public bool enabled = true;
        public float baseFee = 10f;
        public float perSkillLevel = 3f;

        public int FeeFor(Pawn doctor)
        {
            int skill = 0;
            if (doctor != null && doctor.skills != null && doctor.skills.GetSkill(SkillDefOf.Medicine) != null)
            {
                skill = doctor.skills.GetSkill(SkillDefOf.Medicine).Level;
            }
            return Mathf.RoundToInt(Mathf.Max(0f, baseFee) + Mathf.Max(0f, perSkillLevel) * skill);
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref enabled, "enabled", true);
            Scribe_Values.Look(ref baseFee, "baseFee", 10f);
            Scribe_Values.Look(ref perSkillLevel, "perSkillLevel", 3f);
        }
    }

    public static class EconomyMedical
    {
        /// <summary>
        /// 医疗这一类的公有化程度（0~1）。医疗费按"私有份额"收：
        /// 公有 100% = 全免费，公有 0% = 全额自付，中间按比例。
        /// </summary>
        public static float PublicShare(Map map)
        {
            EconomyCategoryDef med = DefDatabase<EconomyCategoryDef>.GetNamedSilentFail("DshEco_Medicine");
            MapEconomyComponent c = map != null ? map.GetComponent<MapEconomyComponent>() : null;
            if (med == null || c == null) return 0f;
            return Mathf.Clamp01(c.GetPublicization(med) / 100f);
        }

        /// <summary>
        /// 公费医疗的"报销比例"（0~1）。用三次方而不是线性：
        /// 公有化 50% 只报 12.5%，80% 报 51%，95% 报 86%，100% 全报。
        /// ——低档几乎没优惠（不会"50% 就敢随便躺"），但又没有 99/100 的断崖。
        /// </summary>
        public static float SubsidyRate(Map map)
        {
            MapEconomyComponent mc = map != null ? map.GetComponent<MapEconomyComponent>() : null;
            if (mc != null && mc.medical != null && !mc.medical.enabled) return 1f;   // 收费开关关着 = 全报
            float pub = PublicShare(map);
            if (pub <= 0f) return 0f;
            if (pub >= 1f) return 1f;
            return pub * pub * pub;
        }

        /// <summary>把"标价"折成病人实际要付的钱。返回 0 表示全报。</summary>
        public static int ChargeAfterSubsidy(Map map, int listPrice)
        {
            if (listPrice <= 0) return 0;
            float pay = 1f - SubsidyRate(map);
            if (pay <= 0.001f) return 0;
            return Mathf.Max(1, Mathf.RoundToInt(listPrice * pay));
        }

        /// <summary>
        /// 什么样的伤才值得躺床。公有化 50% = 原版 8；越公有越愿意躺（100% → 0.1）；
        /// 越私有越不愿意躺（0% → 60，基本不躺）。
        /// </summary>
        public static float RestThreshold(Map map)
        {
            float pub = PublicShare(map);
            if (pub >= 0.5f) return Mathf.Lerp(8f, 0.1f, (pub - 0.5f) * 2f);
            return Mathf.Lerp(60f, 8f, pub * 2f);
        }

        /// <summary>流血到多少才值得躺床，口径与 RestThreshold 一致。</summary>
        public static float BloodLossThreshold(Map map)
        {
            float pub = PublicShare(map);
            if (pub >= 0.5f) return Mathf.Lerp(0.35f, 0.05f, (pub - 0.5f) * 2f);
            return Mathf.Lerp(0.9f, 0.35f, pub * 2f);
        }

        /// <summary>治疗用掉的医疗物品，按价格管道的现价记到病人头上。</summary>
        public static void ChargeMedicine(Pawn doctor, Pawn patient, Thing medicine)
        {
            if (patient == null || medicine == null) return;
            Map map = patient.MapHeld != null ? patient.MapHeld : (doctor != null ? doctor.MapHeld : null);
            if (map == null) return;

            MapEconomyComponent comp = map.GetComponent<MapEconomyComponent>();
            if (comp == null) return;   // 药品费用与「医疗收费」开关解耦：只要吃了药就按现价记账

            int price = Mathf.Max(0, Mathf.RoundToInt(EconomyPrice.UnitPrice(medicine.def, map)));
            if (price <= 0)
            {
                DevLog.Msg("[MED] " + patient.LabelShort + " 用的 " + medicine.def.LabelCap
                    + " 现价为 0，免费，不结算。");
                return;
            }

            int paid = 0;
            if (patient.IsColonist)
            {
                paid = EconomyBank.TakePrivateSilver(map, patient, price);
                if (paid < price)
                {
                    EconomySatisfactionEvents.Add(patient, "无力支付医疗费", -1f, 3);
                    EconomySatisfaction.Recompute(patient);
                }
            }
            else
            {
                paid = Mathf.Min(price, EconomyBank.ColonySilver(map));
                if (paid > 0) EconomyBank.SpendColonySilver(map, paid);
            }

            DevLog.Msg("[MED] " + (doctor != null ? doctor.LabelShort : "自己") + " 给 " + patient.LabelShort
                + " 用了 " + medicine.def.LabelCap + "，按现价记 " + price + " 银（实付 " + paid + "）");
        }

        /// <summary>医生给病人治疗时收费：病人个人白银 → 医生个人白银。</summary>
        public static void Charge(Pawn doctor, Pawn patient)
        {
            if (doctor == null || patient == null || doctor == patient) return;

            Map map = patient.MapHeld != null ? patient.MapHeld : doctor.MapHeld;
            if (map == null) return;

            MapEconomyComponent comp = map.GetComponent<MapEconomyComponent>();
            if (comp == null || comp.medical == null || !comp.medical.enabled) return;

            int fee = comp.medical.FeeFor(doctor);
            if (fee <= 0) return;

            if (doctor.IsColonist && patient.IsColonist)
            {
                // 病人付给"殖民地"（玩家），不是付给医生个人。
                // TakePrivateSilver 只摘私有标签，银币留在原地变公共，不能再 GiveColonySilver。
                int paid = EconomyBank.TakePrivateSilver(map, patient, fee);
                if (paid <= 0) return;
            }
            else
            {
                // 非殖民者（囚犯/访客）或自诊：从公共库存出
                int available = EconomyBank.ColonySilver(map);
                int paid = Mathf.Min(fee, available);
                if (paid > 0) EconomyBank.SpendColonySilver(map, paid);
            }
        }
    }

    /// <summary>不引用 HarmonyLib，避免 Harmony 未加载时拖垮整个程序集。</summary>
    public static class EconomyHarmonyProbe
    {
        public static bool HarmonyLoaded()
        {
            Assembly[] asms = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < asms.Length; i++)
            {
                if (asms[i].GetName().Name == "0Harmony") return true;
            }
            return false;
        }
    }

    /// <summary>真正引用 HarmonyLib 的类，只有确认 Harmony 已加载后才会被调用。</summary>
    public static class EconomyMedicalPatcher
    {
        public const string Id = "dsh.economy";
        public static bool Patched;

        public static void Apply()
        {
            if (Patched) return;
            try
            {
                Harmony harmony = new Harmony(Id);
                MethodInfo tend = AccessTools.Method(typeof(TendUtility), "DoTend");
                if (tend == null)
                {
                    Log.Warning("[DSH-ECONOMY] 未找到 TendUtility.DoTend，医疗收费未启用");
                    return;
                }
                MethodInfo postfix = AccessTools.Method(typeof(MedicalTendPatch), "Postfix");
                harmony.Patch(tend, postfix: new HarmonyMethod(postfix));
                Patched = true;
                DevLog.Msg("[DSH-ECONOMY] 医疗收费补丁已应用");
            }
            catch (Exception e)
            {
                Log.Warning("[DSH-ECONOMY] 医疗收费补丁失败，已跳过：" + e.Message);
            }
        }
    }

    public static class MedicalTendPatch
    {
        private static int seen;

        public static void Postfix(Pawn doctor, Pawn patient, Medicine medicine)
        {
            if (seen < 20)
            {
                seen++;
                try
                {
                    Map map = patient != null ? patient.MapHeld : null;
                    MapEconomyComponent c = map != null ? map.GetComponent<MapEconomyComponent>() : null;
                    DevLog.Msg("[MED-DIAG] tend doctor=" + (doctor != null ? doctor.LabelShort : "none")
                        + " patient=" + (patient != null ? patient.LabelShort : "none")
                        + " med=" + (medicine != null ? medicine.def.defName : "none")
                        + " enabled=" + (c != null && c.medical != null ? c.medical.enabled.ToString() : "nocomp"));
                }
                catch { }
            }
            EconomyMedical.Charge(doctor, patient);
            if (medicine != null) EconomyMedical.ChargeMedicine(doctor, patient, medicine);
        }
    }
}

