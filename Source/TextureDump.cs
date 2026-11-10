using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using RimWorld;
using UnityEngine;
using Verse;

namespace DshEconomy
{
    /// <summary>
    /// 一次性把若干原版物品贴图从游戏里导出成 PNG，供画新贴图时参考。
    /// 只在第一次进地图时跑一次，结果写到 Config/DshEcoTextures。
    /// </summary>
    public static class TextureDump
    {
        private static bool done;

        private static readonly string[] DefNames =
        {
            "BlocksGranite", "BlocksSandstone", "BlocksLimestone", "BlocksSlate", "BlocksMarble",
            "Silver", "Gold", "Steel", "Plasteel", "Uranium",
            "DshEco_RawSilver"
        };

        public static void Force()
        {
            done = false;
            Once();
        }

        public static void Once()
        {
            if (done) return;
            done = true;
            try
            {
                string dir = Path.Combine(GenFilePaths.ConfigFolderPath, "DshEcoTextures");
                Directory.CreateDirectory(dir);
                int ok = 0;
                foreach (string defName in DefNames)
                {
                    ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
                    if (def == null)
                    {
                        DevLog.Msg("[TEXDUMP] 找不到定义 " + defName);
                        continue;
                    }
                    Texture2D tex = Resolve(def);
                    if (tex == null)
                    {
                        DevLog.Msg("[TEXDUMP] 取不到贴图 " + defName + " path=" + (def.graphicData != null ? def.graphicData.texPath : "?")
                            + " graphic=" + (def.graphic != null ? def.graphic.GetType().Name : "null"));
                        continue;
                    }
                    string file = Path.Combine(dir, defName + ".png");
                    if (SavePng(tex, file)) ok++;
                }
                DevLog.Msg("[DSH-ECONOMY] 贴图导出完成：" + ok + " 张 → " + dir);
            }
            catch (Exception e)
            {
                Log.Warning("[DSH-ECONOMY] 贴图导出失败：" + e);
            }
        }

        private static Texture2D Resolve(ThingDef def)
        {
            // 1) 直接从 def 的 Graphic 上拿（叠放类物品用 _a 那张）
            try
            {
                if (def.graphic != null)
                {
                    Material mat = def.graphic.MatSingle;
                    if (mat != null && mat.mainTexture is Texture2D t1) return t1;
                    Material matN = def.graphic.MatAt(Rot4.North);
                    if (matN != null && matN.mainTexture is Texture2D t2) return t2;
                }
            }
            catch { }

            // 2) 按 texPath 找，叠放物品再试 _a
            if (def.graphicData != null && !def.graphicData.texPath.NullOrEmpty())
            {
                string p = def.graphicData.texPath;
                Texture2D t = ContentFinder<Texture2D>.Get(p, false);
                if (t != null) return t;
                t = ContentFinder<Texture2D>.Get(p + "_a", false);
                if (t != null) return t;
            }
            return null;
        }

        private static bool SavePng(Texture2D tex, string path)
        {
            int w = tex.width, h = tex.height;
            RenderTexture rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32);
            RenderTexture prev = RenderTexture.active;
            Graphics.Blit(tex, rt);
            RenderTexture.active = rt;
            var readable = new Texture2D(w, h, TextureFormat.RGBA32, false);
            readable.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            readable.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);

            Color32[] px = readable.GetPixels32();
            byte[] raw = new byte[h * (1 + w * 4)];
            int o = 0;
            for (int y = h - 1; y >= 0; y--)          // ReadPixels 原点在左下，PNG 从上到下
            {
                raw[o++] = 0;                          // filter type
                int row = y * w;
                for (int x = 0; x < w; x++)
                {
                    Color32 c = px[row + x];
                    raw[o++] = c.r;
                    raw[o++] = c.g;
                    raw[o++] = c.b;
                    raw[o++] = c.a;
                }
            }
            WritePng(path, w, h, raw);
            return true;
        }

        private static void WritePng(string path, int w, int h, byte[] raw)
        {
            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
            {
                fs.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, 0, 8);

                byte[] ihdr = new byte[13];
                WriteInt(ihdr, 0, w);
                WriteInt(ihdr, 4, h);
                ihdr[8] = 8;    // bit depth
                ihdr[9] = 6;    // RGBA
                WriteChunk(fs, "IHDR", ihdr);

                byte[] compressed;
                using (var ms = new MemoryStream())
                {
                    using (var z = new ZLibStream(ms, CompressionLevel.Optimal, true))
                    {
                        z.Write(raw, 0, raw.Length);
                    }
                    compressed = ms.ToArray();
                }
                WriteChunk(fs, "IDAT", compressed);
                WriteChunk(fs, "IEND", new byte[0]);
            }
        }

        private static void WriteInt(byte[] buf, int off, int v)
        {
            buf[off] = (byte)(v >> 24);
            buf[off + 1] = (byte)(v >> 16);
            buf[off + 2] = (byte)(v >> 8);
            buf[off + 3] = (byte)v;
        }

        private static void WriteChunk(Stream s, string type, byte[] data)
        {
            byte[] len = new byte[4];
            WriteInt(len, 0, data.Length);
            s.Write(len, 0, 4);
            byte[] t = System.Text.Encoding.ASCII.GetBytes(type);
            s.Write(t, 0, 4);
            s.Write(data, 0, data.Length);
            uint crc = Crc32(t, 0xFFFFFFFF);
            crc = Crc32(data, crc) ^ 0xFFFFFFFF;
            byte[] c = new byte[4];
            WriteInt(c, 0, unchecked((int)crc));
            s.Write(c, 0, 4);
        }

        private static readonly uint[] CrcTable = BuildCrcTable();

        private static uint[] BuildCrcTable()
        {
            var table = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                uint c = n;
                for (int k = 0; k < 8; k++)
                    c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                table[n] = c;
            }
            return table;
        }

        private static uint Crc32(byte[] buf, uint crc)
        {
            for (int i = 0; i < buf.Length; i++)
                crc = CrcTable[(crc ^ buf[i]) & 0xFF] ^ (crc >> 8);
            return crc;
        }
    }
}

