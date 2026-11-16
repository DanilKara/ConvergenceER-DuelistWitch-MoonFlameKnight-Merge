using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace ErTool
{
    class EsdCmd
    {
        public int Bank, Id;
        public List<byte[]> Args = new List<byte[]>();
        public EsdCmd Clone() { return new EsdCmd { Bank = Bank, Id = Id, Args = Args.Select(a => (byte[])a.Clone()).ToList() }; }
    }

    class EsdCond
    {
        public EsdState Next; // may be null
        public List<EsdCmd> Pass = new List<EsdCmd>();
        public List<EsdCond> Subs = new List<EsdCond>();
        public byte[] Eval;
    }

    class EsdState
    {
        public long Id;
        public List<EsdCond> Conds = new List<EsdCond>();
        public List<EsdCmd> Entry = new List<EsdCmd>(), Exit = new List<EsdCmd>(), While = new List<EsdCmd>();
    }

    class EsdGroup
    {
        public long Id;
        public List<EsdState> States = new List<EsdState>();
    }

    // Long-format ESD ("fsSL") as used by Elden Ring talk scripts
    class Esd
    {
        public byte[] Head; // 0x6C
        public byte[] DataHead; // 0x48
        public List<EsdGroup> Groups = new List<EsdGroup>();

        const int D = 0x6C;
        static long L(byte[] b, long o) { return BitConverter.ToInt64(b, (int)(o)); }

        public static Esd Read(byte[] b)
        {
            if (Encoding.ASCII.GetString(b, 0, 4) != "fsSL") throw new Exception("not fsSL ESD");
            var e = new Esd();
            e.Head = new byte[D]; Buffer.BlockCopy(b, 0, e.Head, 0, D);
            e.DataHead = new byte[0x48]; Buffer.BlockCopy(b, D, e.DataHead, 0, 0x48);
            long sgOff = L(b, D + 0x18), sgCount = L(b, D + 0x20);
            var stateAt = new Dictionary<long, EsdState>();
            var condAt = new Dictionary<long, EsdCond>();
            // first create states
            var groupRaw = new List<long[]>();
            for (long i = 0; i < sgCount; i++)
            {
                long p = D + sgOff + i * 0x20;
                long id = L(b, p), so = L(b, p + 8), sc = L(b, p + 16);
                var g = new EsdGroup { Id = id };
                for (long k = 0; k < sc; k++)
                {
                    long sp = so + k * 0x48;
                    var st = new EsdState { Id = L(b, D + sp) };
                    stateAt[sp] = st;
                    g.States.Add(st);
                }
                e.Groups.Add(g);
                {
                    long xp = D + so + sc * 0x48;
                    int match = -1;
                    for (int k = 0; k < sc; k++)
                    {
                        bool same = true;
                        for (int z = 0; z < 0x48; z++) if (b[xp + z] != b[D + so + k * 0x48 + z]) { same = false; break; }
                        if (same) { match = k; break; }
                    }
                }
            }
            Func<long, long, List<EsdCmd>> readCmds = null;
            readCmds = (off, cnt) =>
            {
                var l = new List<EsdCmd>();
                for (long k = 0; k < cnt; k++)
                {
                    long p = D + off + k * 0x18;
                    var c = new EsdCmd { Bank = BitConverter.ToInt32(b, (int)p), Id = BitConverter.ToInt32(b, (int)p + 4) };
                    long ao = L(b, p + 8), ac = L(b, p + 16);
                    for (long a = 0; a < ac; a++)
                    {
                        long q = D + ao + a * 0x10;
                        long eo = L(b, q), el = L(b, q + 8);
                        var x = new byte[el]; Buffer.BlockCopy(b, (int)(D + eo), x, 0, (int)el);
                        c.Args.Add(x);
                    }
                    l.Add(c);
                }
                return l;
            };
            Func<long, long, List<EsdCond>> readConds = null;
            readConds = (off, cnt) =>
            {
                var l = new List<EsdCond>();
                for (long k = 0; k < cnt; k++)
                {
                    long co = L(b, D + off + k * 8);
                    EsdCond c;
                    if (!condAt.TryGetValue(co, out c))
                    {
                        c = new EsdCond();
                        condAt[co] = c;
                        long p = D + co;
                        long ns = L(b, p);
                        c.Next = ns == -1 ? null : stateAt[ns];
                        c.Pass = readCmds(L(b, p + 8), L(b, p + 16));
                        c.Subs = readConds(L(b, p + 24), L(b, p + 32));
                        long eo = L(b, p + 40), el = L(b, p + 48);
                        c.Eval = new byte[el]; Buffer.BlockCopy(b, (int)(D + eo), c.Eval, 0, (int)el);
                    }
                    l.Add(c);
                }
                return l;
            };
            foreach (var kv in stateAt.OrderBy(x => x.Key))
            {
                long p = D + kv.Key;
                var st = kv.Value;
                st.Conds = readConds(L(b, p + 8), L(b, p + 16));
                st.Entry = readCmds(L(b, p + 24), L(b, p + 32));
                st.Exit = readCmds(L(b, p + 40), L(b, p + 48));
                st.While = readCmds(L(b, p + 56), L(b, p + 64));
            }
            return e;
        }

        public byte[] Write()
        {
            var states = Groups.SelectMany(g => g.States).ToList();
            var phys = new List<EsdState>();
            foreach (var g in Groups) { phys.AddRange(g.States); if (g.States.Count > 0) phys.Add(g.States[0]); }
            // condition slots
            var slots = new List<EsdCond>();
            var stateCondSlot = new Dictionary<EsdState, int>();
            foreach (var s in states) { stateCondSlot[s] = slots.Count; slots.AddRange(s.Conds); }
            var conds = new List<EsdCond>();
            var condIndex = new Dictionary<EsdCond, int>();
            var condSubSlot = new Dictionary<EsdCond, int>();
            for (int i = 0; i < slots.Count; i++)
            {
                var c = slots[i];
                if (condIndex.ContainsKey(c)) continue;
                condIndex[c] = conds.Count; conds.Add(c);
                condSubSlot[c] = slots.Count; slots.AddRange(c.Subs);
            }
            // commands
            var cmds = new List<EsdCmd>();
            var listStart = new Dictionary<List<EsdCmd>, int>();
            Action<List<EsdCmd>> alloc = l => { if (l.Count > 0) { listStart[l] = cmds.Count; cmds.AddRange(l); } };
            foreach (var s in states) { alloc(s.Entry); alloc(s.Exit); alloc(s.While); }
            foreach (var c in conds) alloc(c.Pass);
            int argCount = cmds.Sum(c => c.Args.Count);

            long sgOff = 0x48;
            long stOff = sgOff + 0x20L * Groups.Count;
            long cOff = stOff + 0x48L * phys.Count;
            long cmdOff = cOff + 0x38L * conds.Count;
            long argOff = cmdOff + 0x18L * cmds.Count;
            long slotOff = argOff + 0x10L * argCount;
            long exprOff = slotOff + 8L * slots.Count;

            var expr = new MemoryStream();
            var ms = new MemoryStream(); var bw = new BinaryWriter(ms);
            bw.Write(Head);
            byte[] dh = (byte[])DataHead.Clone();
            BitConverter.GetBytes(sgOff).CopyTo(dh, 0x18);
            BitConverter.GetBytes((long)Groups.Count).CopyTo(dh, 0x20);
            bw.Write(dh);
            var stateOffset = new Dictionary<EsdState, long>();
            for (int i = 0; i < phys.Count; i++) if (!stateOffset.ContainsKey(phys[i])) stateOffset[phys[i]] = stOff + 0x48L * i;
            int si = 0;
            foreach (var g in Groups)
            {
                long so = stOff + 0x48L * si;
                bw.Write(g.Id); bw.Write(so); bw.Write((long)g.States.Count); bw.Write(so);
                si += g.States.Count + (g.States.Count > 0 ? 1 : 0);
            }
            Func<List<EsdCmd>, long> lo = l => l.Count > 0 ? cmdOff + 0x18L * listStart[l] : -1L;
            foreach (var s in phys)
            {
                bw.Write(s.Id);
                bw.Write(slotOff + 8L * stateCondSlot[s]); bw.Write((long)s.Conds.Count);
                bw.Write(lo(s.Entry)); bw.Write((long)s.Entry.Count);
                bw.Write(lo(s.Exit)); bw.Write((long)s.Exit.Count);
                bw.Write(lo(s.While)); bw.Write((long)s.While.Count);
            }
            foreach (var c in conds)
            {
                bw.Write(c.Next == null ? -1L : stateOffset[c.Next]);
                bw.Write(lo(c.Pass)); bw.Write((long)c.Pass.Count);
                bw.Write(c.Subs.Count > 0 ? slotOff + 8L * condSubSlot[c] : -1L); bw.Write((long)c.Subs.Count);
                bw.Write(exprOff + expr.Length); bw.Write((long)c.Eval.Length);
                expr.Write(c.Eval, 0, c.Eval.Length);
            }
            int ai = 0;
            var argExpr = new List<byte[]>();
            foreach (var c in cmds)
            {
                bw.Write(c.Bank); bw.Write(c.Id);
                bw.Write(c.Args.Count > 0 ? argOff + 0x10L * ai : -1L); bw.Write((long)c.Args.Count);
                ai += c.Args.Count; argExpr.AddRange(c.Args);
            }
            // args come before condition evaluators in the expression blob in the original files? keep simple: append after
            foreach (var a in argExpr)
            {
                bw.Write(exprOff + expr.Length); bw.Write((long)a.Length);
                expr.Write(a, 0, a.Length);
            }
            foreach (var c in slots) bw.Write(cOff + 0x38L * condIndex[c]);
            expr.WriteTo(ms);
            long dataSize = ms.Length - D;
            byte[] o = ms.ToArray();
            Action<int, int> w32 = (pos, v) => BitConverter.GetBytes(v).CopyTo(o, pos);
            w32(0x14, (int)dataSize);
            w32(0x28, Groups.Count);
            w32(0x30, phys.Count);
            w32(0x38, conds.Count);
            w32(0x40, cmds.Count);
            w32(0x48, argCount);
            w32(0x4C, (int)slotOff);
            w32(0x50, slots.Count);
            w32(0x54, (int)dataSize);
            w32(0x5C, (int)dataSize);
            w32(0x64, (int)dataSize);
            return o;
        }

        public static string Hex(byte[] d) { return BitConverter.ToString(d).Replace("-", ""); }

        public string Dump()
        {
            var sb = new StringBuilder();
            var visited = new HashSet<EsdCond>();
            Action<string, List<EsdCmd>> dc = (tag, l) => { foreach (var c in l) sb.AppendLine("    " + tag + " " + c.Bank + ":" + c.Id + "(" + string.Join(", ", c.Args.Select(Hex)) + ")"); };
            Action<EsdCond, string> dcond = null;
            dcond = (c, ind) =>
            {
                sb.AppendLine(ind + "COND " + Hex(c.Eval) + " -> " + (c.Next == null ? "none" : c.Next.Id.ToString()));
                foreach (var p in c.Pass) sb.AppendLine(ind + "  pass " + p.Bank + ":" + p.Id + "(" + string.Join(", ", p.Args.Select(Hex)) + ")");
                foreach (var s in c.Subs) dcond(s, ind + "  ");
            };
            foreach (var g in Groups)
            {
                sb.AppendLine("GROUP " + g.Id);
                foreach (var s in g.States)
                {
                    sb.AppendLine("  STATE " + s.Id);
                    dc("entry", s.Entry); dc("exit", s.Exit); dc("while", s.While);
                    foreach (var c in s.Conds) dcond(c, "    ");
                }
            }
            return sb.ToString();
        }
    }
}
