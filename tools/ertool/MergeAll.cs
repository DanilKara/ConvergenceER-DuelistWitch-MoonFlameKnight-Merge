using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace ErTool
{
    static class MergeAll
    {
        public static TextWriter Log = Console.Out;
        static string B, W, K, O; // mod roots
        static int conflicts = 0;

        // ---- class relocation settings (Twin Moon Knight moves from class index 29 to 30) ----
        const int OldIdx = 29, NewIdx = 30;
        const int OldText = 297119, NewText = 297102;

        static void Warn(string s) { Log.WriteLine("  !! " + s); conflicts++; }

        public static int Run(string b, string w, string k, string o, string mergedBeh, string mergedHks)
        {
            B = b; W = w; K = k; O = o;
            var files = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var root in new[] { W, K })
                foreach (var f in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
                    files.Add(f.Substring(root.Length + 1).Replace('/', '\\'));
            foreach (var rel in files)
            {
                string pb = Path.Combine(B, rel), pw = Path.Combine(W, rel), pk = Path.Combine(K, rel), po = Path.Combine(O, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(po));
                bool hb = File.Exists(pb), hw = File.Exists(pw), hk = File.Exists(pk);
                string lower = rel.ToLowerInvariant();
                if (lower == "regulation.bin") { Log.WriteLine("[regulation] " + rel); File.WriteAllBytes(po, MergeRegulation(pb, pw, pk)); continue; }
                if (lower == @"event\m10_01_00_00.emevd.dcx") { Log.WriteLine("[emevd] " + rel); File.WriteAllBytes(po, MergeM10(pb, pw, pk)); continue; }
                if (lower == @"script\talk\m60_00_00_00.talkesdbnd.dcx") { Log.WriteLine("[esd] " + rel); File.WriteAllBytes(po, MergeTalk(pb, pw, pk)); continue; }
                if (lower == @"action\script\c0000.hks") { Log.WriteLine("[hks] " + rel + " (3-way text merge)"); File.Copy(mergedHks, po, true); continue; }
                if (lower == @"menu\hi\05_dummy.tpf.dcx") { Log.WriteLine("[tpf] " + rel); File.WriteAllBytes(po, MergeTpf(pw, pk)); continue; }
                if (lower.StartsWith(@"msg\") && lower.EndsWith(".msgbnd.dcx") && hw && hk) { Log.WriteLine("[msg] " + rel); File.WriteAllBytes(po, MergeMsg(pb, pw, pk)); continue; }
                if (hw && hk)
                {
                    byte[] bw = File.ReadAllBytes(pw), bk = File.ReadAllBytes(pk);
                    if (Program.Eq(bw, bk)) { File.WriteAllBytes(po, bw); Log.WriteLine("[same] " + rel); continue; }
                    byte[] bb = hb ? File.ReadAllBytes(pb) : null;
                    if (bb != null && Program.Eq(bb, bw)) { File.WriteAllBytes(po, bk); Log.WriteLine("[knight] " + rel); continue; }
                    if (bb != null && Program.Eq(bb, bk)) { File.WriteAllBytes(po, bw); Log.WriteLine("[witch] " + rel); continue; }
                    if (lower.EndsWith("bnd.dcx"))
                    {
                        Log.WriteLine("[binder] " + rel);
                        var over = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
                        if (lower.EndsWith(@"chr\c0000.anibnd.dcx")) over["a778.tae"] = null; // take knight's
                        if (lower.EndsWith(@"chr\c0000.behbnd.dcx")) over[@"behaviors\c0000.hkx"] = File.ReadAllBytes(mergedBeh);
                        bool renumberGroups = lower.EndsWith(".ffxbnd.dcx");
                        File.WriteAllBytes(po, MergeBinder(pb, pw, pk, over, renumberGroups));
                        continue;
                    }
                    Warn("unhandled conflicting file " + rel + " — taking witch's");
                    File.WriteAllBytes(po, bw);
                    continue;
                }
                File.Copy(hw ? pw : pk, po, true);
                Log.WriteLine("[" + (hw ? "witch" : "knight") + "-only] " + rel);
            }
            Log.WriteLine("done, conflicts/warnings: " + conflicts);
            return conflicts;
        }

        static byte[] ReadDcx(string p, out Dcx d) { bool e; return Program.Open(p, out d, out e); }

        // ---------------- binders ----------------
        static string EntryKey(BndFile f) { return f.Name.ToLowerInvariant(); }
        static bool KeyMatches(string key, string suffix) { return key.EndsWith("\\" + suffix.ToLowerInvariant()); }

        static byte[] MergeBinder(string pb, string pw, string pk, Dictionary<string, byte[]> over, bool renumberGroups)
        {
            Dcx dw, dx;
            var bb = Bnd4.Read(ReadDcx(pb, out dx)); var bw = Bnd4.Read(ReadDcx(pw, out dw)); var bk = Bnd4.Read(ReadDcx(pk, out dx));
            var mb = bb.Files.ToDictionary(EntryKey); var mw = bw.Files.ToDictionary(EntryKey); var mk = bk.Files.ToDictionary(EntryKey);
            var result = bw.Files.Select(f => f.Clone()).ToList();
            var rmap = result.ToDictionary(EntryKey);
            int replaced = 0, added = 0;
            foreach (var kf in bk.Files)
            {
                string key = EntryKey(kf);
                var ov = over.FirstOrDefault(x => KeyMatches(key, x.Key));
                BndFile b0, w0;
                mb.TryGetValue(key, out b0); mw.TryGetValue(key, out w0);
                if (ov.Key != null)
                {
                    rmap[key].Bytes = ov.Value ?? kf.Bytes;
                    Log.WriteLine("    override " + Program.Short(kf.Name) + (ov.Value == null ? " <- knight" : " <- merged"));
                    continue;
                }
                if (b0 != null && Program.Eq(b0.Bytes, kf.Bytes)) continue; // knight unchanged
                if (w0 != null)
                {
                    if (Program.Eq(w0.Bytes, kf.Bytes)) continue;
                    if (b0 != null && Program.Eq(b0.Bytes, w0.Bytes)) { rmap[key].Bytes = kf.Bytes; replaced++; continue; }
                    Warn("binder entry conflict " + kf.Name + " — keeping witch's");
                    continue;
                }
                // knight-only new entry
                var nf = kf.Clone();
                result.Add(nf); rmap[key] = nf; added++;
            }
            // entries deleted by knight
            foreach (var b0 in bb.Files) if (!mk.ContainsKey(EntryKey(b0))) Warn("knight removed entry " + b0.Name + " (ignored)");
            // IDs
            if (renumberGroups)
            {
                var groups = result.GroupBy(f => f.ID / 100000).OrderBy(g => g.Key);
                var ordered = new List<BndFile>();
                foreach (var g in groups)
                {
                    int i = 0;
                    foreach (var f in g) { f.ID = g.Key * 100000 + i++; ordered.Add(f); }
                }
                result = ordered;
            }
            else
            {
                var used = new HashSet<int>();
                int next = result.Max(f => f.ID) + 1;
                foreach (var f in result)
                {
                    if (!used.Add(f.ID)) { Log.WriteLine("    id " + f.ID + " taken, " + Program.Short(f.Name) + " -> " + next); f.ID = next; used.Add(next); next++; }
                }
                // keep sorted by ID if witch's binder was sorted
                bool sorted = true;
                for (int i = 1; i < bw.Files.Count; i++) if (bw.Files[i].ID < bw.Files[i - 1].ID) { sorted = false; break; }
                if (sorted) result = result.OrderBy(f => f.ID).ToList();
            }
            Log.WriteLine("    entries: witch " + bw.Files.Count + ", +" + added + " from knight, " + replaced + " replaced by knight -> " + result.Count);
            bw.Files = result;
            return dw.Compress(bw.Write());
        }

        // ---------------- TPF ----------------
        static byte[] MergeTpf(string pw, string pk)
        {
            Dcx dw, dk;
            var tw = Tpf.Read(ReadDcx(pw, out dw)); var tk = Tpf.Read(ReadDcx(pk, out dk));
            var names = new HashSet<string>(tw.Textures.Select(t => t.Name));
            int added = 0;
            foreach (var t in tk.Textures)
            {
                if (names.Contains(t.Name))
                {
                    if (!Program.Eq(tw.Textures.First(x => x.Name == t.Name).Data, t.Data)) Warn("texture differs " + t.Name + " — keeping witch's");
                    continue;
                }
                tw.Textures.Add(t); added++;
            }
            Log.WriteLine("    textures: witch " + (tw.Textures.Count - added) + " + knight " + added);
            return dw.Compress(tw.Write());
        }

        // ---------------- FMG ----------------
        static byte[] MergeMsg(string pb, string pw, string pk)
        {
            Dcx dw, dx;
            var bb = Bnd4.Read(ReadDcx(pb, out dx)); var bw = Bnd4.Read(ReadDcx(pw, out dw)); var bk = Bnd4.Read(ReadDcx(pk, out dx));
            int total = 0;
            foreach (var wf in bw.Files)
            {
                var bf = bb.Files.First(x => x.Name == wf.Name); var kf = bk.Files.First(x => x.Name == wf.Name);
                string nm = Program.Short(wf.Name);
                var fk = Fmg.Read(kf.Bytes);
                var fb = Fmg.Read(bf.Bytes);
                bool reloc = nm == "GR_MenuText.fmg" || nm == "GR_LineHelp.fmg";
                if (reloc)
                {
                    string s;
                    if (fk.Entries.TryGetValue(OldText, out s) && s != null)
                    {
                        string existing;
                        if (fk.Entries.TryGetValue(NewText, out existing) && existing != null) throw new Exception("text id " + NewText + " not free in " + nm);
                        fk.Entries[NewText] = s;
                        string bs; fb.Entries.TryGetValue(OldText, out bs);
                        if (fb.Entries.ContainsKey(OldText)) fk.Entries[OldText] = bs; else fk.Entries.Remove(OldText);
                    }
                }
                if (Program.Eq(kf.Bytes, bf.Bytes) && !reloc) continue;
                var fw = Fmg.Read(wf.Bytes);
                int changes = 0;
                var ids = new SortedSet<int>(fb.Entries.Keys.Concat(fw.Entries.Keys).Concat(fk.Entries.Keys));
                foreach (int id in ids)
                {
                    string sb, sw, sk;
                    bool hb = fb.Entries.TryGetValue(id, out sb), hw = fw.Entries.TryGetValue(id, out sw), hk = fk.Entries.TryGetValue(id, out sk);
                    bool kc = hb != hk || sb != sk;
                    if (!kc) continue;
                    bool wc = hb != hw || sb != sw;
                    if (wc && !(hw == hk && sw == sk)) { Warn("text conflict " + nm + " " + id + " — keeping witch's"); continue; }
                    if (hk) fw.Entries[id] = sk; else fw.Entries.Remove(id);
                    changes++;
                }
                if (changes > 0) { wf.Bytes = fw.Write(); total += changes; }
            }
            Log.WriteLine("    " + total + " text entries taken from knight");
            return dw.Compress(bw.Write());
        }

        // ---------------- regulation ----------------
        static Param P(Bnd4 b, string name) { return Param.Read(b.Files.First(f => Program.Short(f.Name) == name).Bytes); }

        static void Relocate(Param p, int oldId, int newId, Action<PRow> modify)
        {
            if (p.Rows.Any(r => r.ID == newId)) throw new Exception("row " + newId + " already exists in " + p.ParamType);
            var row = p.Rows.Single(r => r.ID == oldId);
            row.ID = newId;
            if (modify != null) modify(row);
            p.Rows = p.Rows.OrderBy(r => r.ID).ToList();
        }

        static byte[] MergeRegulation(string pb, string pw, string pk)
        {
            byte[] rawW = File.ReadAllBytes(pw);
            Dcx dw, dx;
            var bb = Bnd4.Read(ReadDcx(pb, out dx)); var bw = Bnd4.Read(ReadDcx(pw, out dw)); var bk = Bnd4.Read(ReadDcx(pk, out dx));
            // relocate knight's class
            var kInit = P(bk, "CharaInitParam.param");
            Relocate(kInit, 3000 + OldIdx, 3000 + NewIdx, null);
            Relocate(kInit, 3100 + 2 * OldIdx, 3100 + 2 * NewIdx, null);
            Relocate(kInit, 3100 + 2 * OldIdx + 1, 3100 + 2 * NewIdx + 1, null);
            var kMenu = P(bk, "CharMakeMenuListItemParam.param");
            Relocate(kMenu, 100200 + OldIdx, 100200 + NewIdx, r =>
            {
                if (BitConverter.ToInt32(r.Data, 0) != OldIdx || BitConverter.ToInt32(r.Data, 4) != OldText) throw new Exception("unexpected menu row layout");
                BitConverter.GetBytes(NewIdx).CopyTo(r.Data, 0);
                BitConverter.GetBytes(NewText).CopyTo(r.Data, 4);
            });
            var kOverride = new Dictionary<string, Param> { { "CharaInitParam.param", kInit }, { "CharMakeMenuListItemParam.param", kMenu } };
            Log.WriteLine("    Twin Moon Knight: class index " + OldIdx + " -> " + NewIdx + " (CharaInit " + (3000 + NewIdx) + "/" + (3100 + 2 * NewIdx) + "/" + (3101 + 2 * NewIdx) + ", menu row " + (100200 + NewIdx) + ", text " + NewText + ")");
            foreach (var wf in bw.Files)
            {
                string nm = Program.Short(wf.Name);
                var bf = bb.Files.First(f => f.Name == wf.Name); var kf = bk.Files.First(f => f.Name == wf.Name);
                Param pkm;
                bool kChanged = kOverride.ContainsKey(nm) || !Program.Eq(kf.Bytes, bf.Bytes);
                if (!kChanged) continue;
                if (!kOverride.TryGetValue(nm, out pkm)) pkm = Param.Read(kf.Bytes);
                if (Program.Eq(wf.Bytes, bf.Bytes) && !kOverride.ContainsKey(nm)) { wf.Bytes = kf.Bytes; Log.WriteLine("    " + nm + ": knight's"); continue; }
                var pbm = Param.Read(bf.Bytes); var pwm = Param.Read(wf.Bytes);
                if (pbm.RowSize != pwm.RowSize || pbm.RowSize != pkm.RowSize) throw new Exception("row size mismatch " + nm);
                Func<List<PRow>, Dictionary<string, PRow>> key = rows =>
                {
                    var d = new Dictionary<string, PRow>(); var cnt = new Dictionary<int, int>();
                    foreach (var r in rows) { int c; cnt.TryGetValue(r.ID, out c); cnt[r.ID] = c + 1; d[r.ID + "#" + c] = r; }
                    return d;
                };
                var db = key(pbm.Rows); var dwm = key(pwm.Rows); var dk = key(pkm.Rows);
                var all = new HashSet<string>(db.Keys.Concat(dwm.Keys).Concat(dk.Keys));
                int fromK = 0;
                foreach (var kk in all)
                {
                    PRow rb, rw, rk; db.TryGetValue(kk, out rb); dwm.TryGetValue(kk, out rw); dk.TryGetValue(kk, out rk);
                    if (RowEq(rk, rb)) continue;
                    if (RowEq(rw, rk)) continue;
                    if (RowEq(rw, rb))
                    {
                        if (rk == null) dwm.Remove(kk); else dwm[kk] = rk;
                        fromK++; continue;
                    }
                    if (rb != null && rw != null && rk != null)
                    {
                        // byte-level merge
                        var nd = (byte[])rw.Data.Clone();
                        int bc = 0;
                        for (int i = 0; i < nd.Length; i++)
                            if (rk.Data[i] != rb.Data[i]) { if (rw.Data[i] != rb.Data[i] && rw.Data[i] != rk.Data[i]) bc++; else nd[i] = rk.Data[i]; }
                        if (bc > 0) Warn(nm + " row " + kk + ": " + bc + " conflicting bytes (witch kept)");
                        dwm[kk] = new PRow { ID = rw.ID, Data = nd, Name = rw.Name };
                        fromK++; continue;
                    }
                    Warn(nm + " row " + kk + " conflict (witch kept)");
                }
                pwm.Rows = dwm.Values.OrderBy(r => r.ID).ToList();
                // stable order for duplicate IDs: by key suffix
                pwm.Rows = dwm.OrderBy(kv => kv.Value.ID).ThenBy(kv => int.Parse(kv.Key.Substring(kv.Key.IndexOf('#') + 1))).Select(kv => kv.Value).ToList();
                wf.Bytes = pwm.Write();
                Log.WriteLine("    " + nm + ": " + fromK + " rows from knight merged into witch's");
            }
            byte[] dcx = dw.Compress(bw.Write());
            byte[] iv = new byte[16]; Buffer.BlockCopy(rawW, 0, iv, 0, 16);
            return Regulation.Encrypt(dcx, iv);
        }

        static bool RowEq(PRow a, PRow b)
        {
            if (a == null || b == null) return a == b;
            return a.Name == b.Name && Program.Eq(a.Data, b.Data);
        }

        // ---------------- events ----------------
        static byte[] MergeM10(string pb, string pw, string pk)
        {
            Dcx dw, dx;
            byte[] rw = ReadDcx(pw, out dw), rk = ReadDcx(pk, out dx);
            if (!Program.Eq(rw, rk)) throw new Exception("m10 emevd differs between mods — not expected");
            var e = Emevd.Read(rw);
            var ev0 = e.Events.First(x => x.Id == 0);
            int idx = ev0.Instrs.FindIndex(i => i.Bank == 2000 && i.Id == 0 && i.Args.Length == 32 && BitConverter.ToInt32(i.Args, 4) == 10012510 && BitConverter.ToInt32(i.Args, 12) == OldIdx);
            if (idx < 0) throw new Exception("class event for index " + OldIdx + " not found");
            if (ev0.Instrs.Any(i => i.Bank == 2000 && i.Id == 0 && i.Args.Length == 32 && BitConverter.ToInt32(i.Args, 4) == 10012510 && BitConverter.ToInt32(i.Args, 12) == NewIdx)) throw new Exception("index 30 already initialized");
            var src = ev0.Instrs[idx];
            var ni = new EvInstr { Bank = src.Bank, Id = src.Id, Args = (byte[])src.Args.Clone() };
            BitConverter.GetBytes(NewIdx).CopyTo(ni.Args, 0);   // event slot
            BitConverter.GetBytes(NewIdx).CopyTo(ni.Args, 12);  // class index
            ev0.Instrs.Insert(idx + 1, ni);
            foreach (var p in ev0.Params) if (p.InstrIndex > idx) p.InstrIndex++;
            Log.WriteLine("    added InitializeEvent(" + NewIdx + ", 10012510, " + BitConverter.ToInt32(ni.Args, 8) + ", " + NewIdx + ", 0, 0, 0, 0) for Twin Moon Knight");
            return dw.Compress(e.Write());
        }

        // ---------------- talk ESD (Kale) ----------------
        static byte[] MergeTalk(string pb, string pw, string pk)
        {
            Dcx dw, dx;
            var bw = Bnd4.Read(ReadDcx(pw, out dw)); var bk = Bnd4.Read(ReadDcx(pk, out dx)); var bb = Bnd4.Read(ReadDcx(pb, out dx));
            foreach (var wf in bw.Files)
            {
                var kf = bk.Files.First(f => f.Name == wf.Name); var bf = bb.Files.First(f => f.Name == wf.Name);
                if (Program.Eq(kf.Bytes, bf.Bytes) || Program.Eq(kf.Bytes, wf.Bytes)) continue;
                if (Program.Eq(wf.Bytes, bf.Bytes)) { wf.Bytes = kf.Bytes; continue; }
                if (!Program.Short(wf.Name).Equals("t800006000.esd", StringComparison.OrdinalIgnoreCase)) { Warn("esd conflict " + wf.Name); continue; }
                wf.Bytes = MergeKale(wf.Bytes, kf.Bytes);
            }
            return dw.Compress(bw.Write());
        }

        static byte[] I32Expr(int v) { var b = new byte[6]; b[0] = 0x82; BitConverter.GetBytes(v).CopyTo(b, 1); b[5] = 0xA1; return b; }
        static int ExprI32(byte[] e) { if (e.Length != 6 || e[0] != 0x82 || e[5] != 0xA1) return int.MinValue; return BitConverter.ToInt32(e, 1); }

        static byte[] MergeKale(byte[] wb, byte[] kb)
        {
            var ew = Esd.Read(wb); var ek = Esd.Read(kb);
            // locate in knight: the added list option, its result condition and the shop state
            Func<Esd, int, Tuple<EsdGroup, EsdState, EsdCmd>> findOpt = (e, optIdx) =>
            {
                foreach (var g in e.Groups) foreach (var s in g.States) foreach (var c in s.Entry)
                            if (c.Bank == 1 && c.Id == 19 && c.Args.Count == 3 && ExprI32(c.Args[0]) == optIdx && ExprI32(c.Args[1]) >= 28009000) return Tuple.Create(g, s, c);
                return null;
            };
            var wOpt = findOpt(ew, 10); var kOpt = findOpt(ek, 10);
            if (wOpt == null || kOpt == null) throw new Exception("Kale menu option 10 not found");
            if (findOpt(ew, 11) != null) throw new Exception("option 11 already used");
            byte[] condW = new byte[] { 0x57, 0x84, 0x82, 10, 0, 0, 0, 0x95, 0xA1 };
            Func<EsdGroup, Tuple<EsdState, EsdCond>> findCond = g =>
            {
                foreach (var s in g.States) foreach (var c in s.Conds) if (Program.Eq(c.Eval, condW)) return Tuple.Create(s, c);
                return null;
            };
            var gK = ek.Groups.First(g => g.Id == kOpt.Item1.Id);
            var gW = ew.Groups.First(g => g.Id == wOpt.Item1.Id);
            var kc = findCond(gK); var wc = findCond(gW);
            if (kc == null || wc == null) throw new Exception("Kale option-10 condition not found");
            var kShop = kc.Item2.Next;
            // 1) menu entry
            var opt = kOpt.Item3.Clone();
            opt.Args[0] = I32Expr(11);
            var wMenuState = wOpt.Item2;
            wMenuState.Entry.Insert(wMenuState.Entry.IndexOf(wOpt.Item3) + 1, opt);
            // 2) new state = copy of knight's shop state
            long newId = gW.States.Max(s => s.Id) + 1;
            var ns = new EsdState { Id = newId };
            ns.Entry = kShop.Entry.Select(c => c.Clone()).ToList();
            ns.Exit = kShop.Exit.Select(c => c.Clone()).ToList();
            ns.While = kShop.While.Select(c => c.Clone()).ToList();
            // map next-state references by state id into witch's group
            foreach (var c in kShop.Conds)
            {
                if (c.Subs.Count > 0 || c.Pass.Count > 0) throw new Exception("unexpected complex condition in knight shop state");
                var target = c.Next == null ? null : gW.States.First(s => s.Id == c.Next.Id);
                ns.Conds.Add(new EsdCond { Eval = (byte[])c.Eval.Clone(), Next = target });
            }
            gW.States.Add(ns);
            // 3) result condition for option 11
            var cond = new EsdCond { Eval = new byte[] { 0x57, 0x84, 0x82, 11, 0, 0, 0, 0x95, 0xA1 }, Next = ns };
            var resState = wc.Item1;
            resState.Conds.Insert(resState.Conds.IndexOf(wc.Item2) + 1, cond);
            Log.WriteLine("    Kale: added option 11 (text " + ExprI32(opt.Args[1]) + ") -> state " + newId + " shop " + string.Join("..", ns.Entry.Where(c => c.Bank == 1 && c.Id == 22).SelectMany(c => c.Args).Select(ExprI32)));
            return ew.Write();
        }
    }
}
