using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using HarmonyLib;
using Verse;

namespace DshEconomy
{
    /// <summary>
    /// 反射体检器。
    /// 不复制任何补丁代码，只拦截 AccessTools.Method 的每一次调用，
    /// 把「找到 / 没找到」记下来，并写进 Assemblies\audit-report.txt。
    /// 好处：审计用的就是真实调用，永远不会和补丁代码脱节；
    ///       报告落到文件，不怕日志被截断。
    /// </summary>
    public static class ReflectionAudit
    {
        private static bool installed;
        private static bool recording;
        private static int total, found, missing;
        private static int hooked;
        private static readonly List<string> report = new List<string>();

        /// <summary>在 EconomyBootstrap 静态构造的第一行调用。</summary>
        public static void Begin()
        {
            if (installed) return;
            installed = true;
            try
            {
                List<MethodInfo> targets = FindMethodOverloads();
                if (targets.Count == 0)
                {
                    Log.Warning("[DSH-ECONOMY] 反射体检器未安装：AccessTools 里没有任何 (Type, string, ...) 的 Method 重载");
                    DumpAllOverloads();
                    return;
                }

                Harmony h = new Harmony("xiemore.economy.audit");
                for (int i = 0; i < targets.Count; i++)
                {
                    try
                    {
                        h.Patch(targets[i], postfix: new HarmonyMethod(typeof(ReflectionAudit), nameof(Postfix)));
                        hooked++;
                        Log.Message("[DSH-ECONOMY] 体检器挂钩成功：" + Describe(targets[i]));
                    }
                    catch (Exception e)
                    {
                        Log.Warning("[DSH-ECONOMY] 体检器挂钩失败 " + Describe(targets[i]) + "：" + e.Message);
                    }
                }

                recording = true;
                report.Add("=== 反射体检报告 ===");
                report.Add("挂钩重载数：" + hooked + "    生成时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                report.Add("");
                Log.Message("[DSH-ECONOMY] 反射体检器已安装，挂钩 " + hooked + " 个重载，开始记录查询");
            }
            catch (Exception e)
            {
                Log.Warning("[DSH-ECONOMY] 反射体检器安装失败：" + e.Message);
            }
        }

        /// <summary>不依赖具体签名：凡是名叫 Method、头两个参数是 (Type, string) 的静态方法，全挂。</summary>
        private static List<MethodInfo> FindMethodOverloads()
        {
            List<MethodInfo> result = new List<MethodInfo>();
            MethodInfo[] all = typeof(AccessTools).GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            for (int i = 0; i < all.Length; i++)
            {
                MethodInfo m = all[i];
                if (m == null || m.Name != "Method") continue;
                ParameterInfo[] ps = m.GetParameters();
                if (ps.Length < 2) continue;
                if (ps[0].ParameterType != typeof(Type)) continue;
                if (ps[1].ParameterType != typeof(string)) continue;
                result.Add(m);
            }
            return result;
        }

        private static void DumpAllOverloads()
        {
            try
            {
                MethodInfo[] all = typeof(AccessTools).GetMethods(
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                for (int i = 0; i < all.Length; i++)
                {
                    if (all[i] != null && all[i].Name == "Method")
                        Log.Warning("[DSH-ECONOMY] 现有重载：" + Describe(all[i]));
                }
            }
            catch (Exception e)
            {
                Log.Warning("[DSH-ECONOMY] 枚举 AccessTools 重载失败：" + e.Message);
            }
        }

        /// <summary>在 EconomyBootstrap 静态构造的最后调用。</summary>
        public static void Report()
        {
            if (!installed) return;
            recording = false;

            string head = "共查询 " + total + " 次，找到 " + found + "，没找到 " + missing;
            Log.Message("[DSH-ECONOMY] 体检汇总：" + head);

            int idx = 0;
            for (int i = 0; i < report.Count; i++)
            {
                if (report[i].StartsWith("×")) idx++;
            }

            string path = WriteFile(head);
            if (path != null) Log.Message("[DSH-ECONOMY] 体检报告已写入：" + path);
        }

        private static string WriteFile(string head)
        {
            try
            {
                report.Add("");
                report.Add("汇总：" + head);
                string dir = Path.GetDirectoryName(typeof(ReflectionAudit).Assembly.Location);
                string path = Path.Combine(dir, "audit-report.txt");
                File.WriteAllLines(path, report.ToArray(), Encoding.UTF8);
                return path;
            }
            catch (Exception e)
            {
                Log.Warning("[DSH-ECONOMY] 体检报告写文件失败：" + e.Message);
                return null;
            }
        }

        private static void Postfix(object[] __args, MethodBase __result)
        {
            if (!recording) return;
            try
            {
                if (__args == null || __args.Length < 2) return;
                Type type = __args[0] as Type;
                string name = __args[1] as string;
                Type[] pars = __args.Length > 2 && __args[2] is Type[] ? (Type[])__args[2] : null;

                string label = Describe(type) + "." + name + "(" + Describe(pars) + ")";
                total++;
                if (__result == null)
                {
                    missing++;
                    Log.Warning("[DSH-ECONOMY] 体检× " + label);
                    report.Add("× " + label);
                    List<string> hints = HintsFor(type, name);
                    for (int i = 0; i < hints.Count; i++)
                    {
                        report.Add("    实际有：" + hints[i]);
                        Log.Warning("[DSH-ECONOMY]    实际有：" + hints[i]);
                    }
                }
                else
                {
                    found++;
                    Log.Message("[DSH-ECONOMY] 体检√ " + label);
                    report.Add("√ " + label);
                }
            }
            catch
            {
                // 体检器自己绝不能把游戏搞崩
            }
        }

        /// <summary>找不到时，把这个类型里同名方法的真实签名列出来，直接给出答案。</summary>
        private static List<string> HintsFor(Type type, string name)
        {
            List<string> hints = new List<string>();
            try
            {
                if (type == null || string.IsNullOrEmpty(name)) return hints;
                MethodInfo[] all = type.GetMethods(
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
                for (int i = 0; i < all.Length && hints.Count < 8; i++)
                {
                    if (all[i] != null && all[i].Name == name) hints.Add(Describe(all[i]));
                }
                if (hints.Count == 0) hints.Add("该类里根本没有叫 " + name + " 的方法");
            }
            catch
            {
            }
            return hints;
        }

        private static string Describe(MethodInfo m)
        {
            if (m == null) return "?";
            ParameterInfo[] ps = m.GetParameters();
            string[] parts = new string[ps.Length];
            for (int i = 0; i < ps.Length; i++)
            {
                string s = Describe(ps[i].ParameterType);
                if (ps[i].ParameterType.IsByRef) s = "ref " + s.TrimEnd('&');
                parts[i] = s + " " + ps[i].Name;
            }
            return m.Name + "(" + string.Join(", ", parts) + ")";
        }

        private static string Describe(Type t)
        {
            if (t == null) return "?";
            if (!t.IsGenericType) return t.Name;
            string n = t.Name;
            int tick = n.IndexOf('`');
            if (tick > 0) n = n.Substring(0, tick);
            Type[] ga = t.GetGenericArguments();
            string[] parts = new string[ga.Length];
            for (int i = 0; i < ga.Length; i++) parts[i] = Describe(ga[i]);
            return n + "<" + string.Join(", ", parts) + ">";
        }

        private static string Describe(Type[] ts)
        {
            if (ts == null) return "不限参数";
            if (ts.Length == 0) return "无参数";
            string[] parts = new string[ts.Length];
            for (int i = 0; i < ts.Length; i++)
            {
                string s = Describe(ts[i]);
                if (ts[i] != null && ts[i].IsByRef) s = "ref " + s.TrimEnd('&');
                parts[i] = s;
            }
            return string.Join(", ", parts);
        }
    }
}
