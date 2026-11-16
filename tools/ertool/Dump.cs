using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace ErTool
{
    static class MiniDump
    {
        class Mod { public ulong Base; public uint Size; public string Name; }

        public static string Analyze(string path)
        {
            byte[] b = File.ReadAllBytes(path);
            var sb = new StringBuilder();
            if (Encoding.ASCII.GetString(b, 0, 4) != "MDMP") return "not a minidump";
            uint nStreams = BitConverter.ToUInt32(b, 8), dirRva = BitConverter.ToUInt32(b, 12);
            var streams = new Dictionary<uint, Tuple<uint, uint>>();
            for (int i = 0; i < nStreams; i++)
            {
                int p = (int)dirRva + i * 12;
                uint type = BitConverter.ToUInt32(b, p);
                if (!streams.ContainsKey(type)) streams[type] = Tuple.Create(BitConverter.ToUInt32(b, p + 4), BitConverter.ToUInt32(b, p + 8));
            }
            var mods = new List<Mod>();
            if (streams.ContainsKey(4))
            {
                int p = (int)streams[4].Item2; uint n = BitConverter.ToUInt32(b, p); p += 4;
                for (int i = 0; i < n; i++, p += 108)
                {
                    var m = new Mod { Base = BitConverter.ToUInt64(b, p), Size = BitConverter.ToUInt32(b, p + 8) };
                    uint nr = BitConverter.ToUInt32(b, p + 20);
                    uint len = BitConverter.ToUInt32(b, (int)nr);
                    m.Name = Path.GetFileName(Encoding.Unicode.GetString(b, (int)nr + 4, (int)len));
                    mods.Add(m);
                }
            }
            Func<ulong, string> sym = a =>
            {
                foreach (var m in mods) if (a >= m.Base && a < m.Base + m.Size) return m.Name + "+0x" + (a - m.Base).ToString("X");
                return null;
            };
            // memory ranges for stack lookup
            var mem = new List<Tuple<ulong, long, long>>(); // start, fileoffset, size
            if (streams.ContainsKey(5))
            {
                int p = (int)streams[5].Item2; uint n = BitConverter.ToUInt32(b, p); p += 4;
                for (int i = 0; i < n; i++, p += 16) mem.Add(Tuple.Create(BitConverter.ToUInt64(b, p), (long)BitConverter.ToUInt32(b, p + 12), (long)BitConverter.ToUInt32(b, p + 8)));
            }
            if (streams.ContainsKey(9))
            {
                int p = (int)streams[9].Item2; ulong n = BitConverter.ToUInt64(b, p); long baseRva = (long)BitConverter.ToUInt64(b, p + 8); p += 16;
                long off = baseRva;
                for (ulong i = 0; i < n; i++, p += 16) { ulong st = BitConverter.ToUInt64(b, p), sz = BitConverter.ToUInt64(b, p + 8); mem.Add(Tuple.Create(st, off, (long)sz)); off += (long)sz; }
            }
            if (streams.ContainsKey(15))
            {
                int mp = (int)streams[15].Item2;
                uint created = BitConverter.ToUInt32(b, mp + 12);
                uint stamp = BitConverter.ToUInt32(b, 20);
                sb.AppendLine("uptime at crash: " + (stamp - created) + " s, process user cpu " + BitConverter.ToUInt32(b, mp + 16) + " s");
            }
            if (streams.ContainsKey(24))
            {
                int tp = (int)streams[24].Item2; uint tn = BitConverter.ToUInt32(b, tp); tp += 4;
                var names = new Dictionary<uint, string>();
                for (int i = 0; i < tn; i++, tp += 12)
                {
                    uint id = BitConverter.ToUInt32(b, tp); long rva = (long)BitConverter.ToUInt64(b, tp + 4);
                    uint len = BitConverter.ToUInt32(b, (int)rva);
                    names[id] = Encoding.Unicode.GetString(b, (int)rva + 4, (int)len);
                }
                if (streams.ContainsKey(6)) { uint et = BitConverter.ToUInt32(b, (int)streams[6].Item2); string tname; sb.AppendLine("crash thread name: " + (names.TryGetValue(et, out tname) ? tname : "?")); }
            }
            if (streams.ContainsKey(6))
            {
                int p = (int)streams[6].Item2;
                uint tid = BitConverter.ToUInt32(b, p);
                uint code = BitConverter.ToUInt32(b, p + 8);
                ulong addr = BitConverter.ToUInt64(b, p + 24);
                uint np = BitConverter.ToUInt32(b, p + 32);
                var prms = new List<string>();
                for (int i = 0; i < np && i < 4; i++) prms.Add("0x" + BitConverter.ToUInt64(b, p + 40 + 8 * i).ToString("X"));
                uint ctxRva = BitConverter.ToUInt32(b, p + 40 + 8 * 15 + 4);
                sb.AppendLine("exception 0x" + code.ToString("X8") + " at 0x" + addr.ToString("X") + " (" + (sym(addr) ?? "?") + ") params " + string.Join(",", prms) + " thread " + tid);
                ulong rsp = BitConverter.ToUInt64(b, (int)ctxRva + 0x98);
                ulong rip = BitConverter.ToUInt64(b, (int)ctxRva + 0xF8);
                string[] regs = { "rax", "rcx", "rdx", "rbx", "rsp", "rbp", "rsi", "rdi", "r8", "r9", "r10", "r11", "r12", "r13", "r14", "r15" };
                var rl = new List<string>();
                for (int i = 0; i < 16; i++) rl.Add(regs[i] + "=" + BitConverter.ToUInt64(b, (int)ctxRva + 0x78 + 8 * i).ToString("X"));
                sb.AppendLine("rip=" + rip.ToString("X") + " " + string.Join(" ", rl));
                // scan stack for return addresses
                var frames = new List<string>();
                foreach (var r in mem)
                {
                    if (rsp < r.Item1 || rsp >= r.Item1 + (ulong)r.Item3) continue;
                    long start = r.Item2 + (long)(rsp - r.Item1);
                    long end = Math.Min(r.Item2 + r.Item3, start + 0x3000);
                    for (long q = start; q + 8 <= end && frames.Count < 40; q += 8)
                    {
                        ulong v = BitConverter.ToUInt64(b, (int)q);
                        string s = sym(v);
                        if (s != null && !s.StartsWith("ntdll") && !s.StartsWith("KERNEL")) frames.Add(s);
                    }
                }
                sb.AppendLine("stack candidates: " + string.Join(" | ", frames));
            }
            sb.AppendLine("modules: " + string.Join(", ", mods.Where(m => !m.Name.StartsWith("api-ms")).Select(m => m.Name).Take(200)));
            return sb.ToString();
        }
    }
}
