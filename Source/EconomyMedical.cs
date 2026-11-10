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
                    + " 现价为 0（价值 " + EconomyValue.Of(medicine.def, map).ToString("0.#")
                    + " × 系数 " + EconomyPrice.CombinedFactor(medicine.def, map).ToString("0.00")
                    + " × 银价 " + EconomyPrice.SilverIndex(map).ToString("0.00")
                    + "），免费，不结算。设社会化 <100% 才有药费。");
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

