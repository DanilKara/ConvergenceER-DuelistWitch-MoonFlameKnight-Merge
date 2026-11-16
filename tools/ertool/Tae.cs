using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace ErTool
{
    // Minimal TAE3 (64-bit, ER) reader for inspection
    static class Tae
    {
        static long L(byte[] b, long o) { return BitConverter.ToInt64(b, (int)o); }
        static int I(byte[] b, long o) { return BitConverter.ToInt32(b, (int)o); }
        static float F(byte[] b, long o) { return BitConverter.ToSingle(b, (int)o); }

        static string U16(byte[] b, long o)
        {
            int e = (int)o; while (b[e] != 0 || b[e + 1] != 0) e += 2;
            return Encoding.Unicode.GetString(b, (int)o, e - (int)o);
        }

        public static string Dump(byte[] b)
        {
            var sb = new StringBuilder();
            sb.AppendLine("ID=" + I(b, 0x50) + " flags=" + BitConverter.ToString(b, 0x40, 8) + " bank=" + L(b, 0x30) + " skel=" + U16(b, L(b, 0xB0)) + " sib=" + U16(b, L(b, 0xB8)));
            int animCount = I(b, 0x54);
            long animsOff = L(b, 0x58), groupsOff = L(b, 0x60);
            long gc = L(b, groupsOff), go = L(b, groupsOff + 8);
            for (int g = 0; g < gc; g++) sb.AppendLine("animgroup " + I(b, go + g * 16) + ".." + I(b, go + g * 16 + 4) + " first=" + L(b, go + g * 16 + 8));
            for (int a = 0; a < animCount; a++)
            {
                long id = L(b, animsOff + a * 16), off = L(b, animsOff + a * 16 + 8);
                long evh = L(b, off), evg = L(b, off + 8), tim = L(b, off + 16), af = L(b, off + 24);
                int ec = I(b, off + 32), egc = I(b, off + 36), tc = I(b, off + 40);
                long aft = L(b, af), afo = L(b, af + 8);
                string afs;
                if (aft == 1) afs = "Std? name=\"" + U16(b, L(b, afo)) + "\" raw=" + BitConverter.ToString(b, (int)afo + 8, 0x18);
                else afs = "type" + aft + " raw=" + BitConverter.ToString(b, (int)afo, 0x20);
                sb.AppendLine("ANIM " + id + " file[" + afs + "] events=" + ec + " groups=" + egc + " times=" + tc);
                var evHeaderIndex = new Dictionary<long, int>();
                for (int e = 0; e < ec; e++)
                {
                    long h = evh + e * 0x18;
                    evHeaderIndex[h] = e;
                    float st = F(b, L(b, h)), en = F(b, L(b, h + 8));
                    long d = L(b, h + 16);
                    int type = I(b, d), unk = I(b, d + 4);
                    long po = L(b, d + 8);
                    // params end: next event data or groups
                    long pend = (e + 1 < ec) ? L(b, evh + (e + 1) * 0x18 + 16) : evg;
                    int plen = (int)Math.Max(0, pend - po);
                    sb.AppendLine("  ev" + e + " t=" + st.ToString("R") + ".." + en.ToString("R") + " type=" + type + " unk=" + unk + " params=" + BitConverter.ToString(b, (int)po, plen));
                }
                for (int g = 0; g < egc; g++)
                {
                    long gp = evg + g * 0x20;
                    long cnt = L(b, gp), vo = L(b, gp + 8), to = L(b, gp + 16);
                    var idx = new List<string>();
                    for (int k = 0; k < cnt; k++) { long v = L(b, vo + 8 * k); int ei; idx.Add(evHeaderIndex.TryGetValue(v, out ei) ? ei.ToString() : "?" + v); }
                    sb.AppendLine("  group type=" + L(b, to) + "," + L(b, to + 8) + " events=" + string.Join(",", idx));
                }
            }
            return sb.ToString();
        }
    }
}
