using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace ErTool
{
    class EvInstr
    {
        public int Bank, Id;
        public byte[] Args;
        public long Layer = -1; // raw layer offset or -1
        public byte[] LayerData; // layer struct bytes if any
    }

    class EvParam
    {
        public long InstrIndex, TargetStart, SourceStart; public int ByteCount; public int Unk;
    }

    class EvEvent
    {
        public long Id; public int Rest; public int Unk;
        public List<EvInstr> Instrs = new List<EvInstr>();
        public List<EvParam> Params = new List<EvParam>();
    }

    // ER EMEVD (64-bit, version 0xCD) reader/writer
    class Emevd
    {
        public byte[] Head;
        public List<EvEvent> Events = new List<EvEvent>();
        public byte[] LinkedFiles; public long LinkedCount;
        public byte[] Strings;

        static long L(byte[] b, long o) { return BitConverter.ToInt64(b, (int)o); }

        public static Emevd Read(byte[] b)
        {
            var e = new Emevd();
            if (Encoding.ASCII.GetString(b, 0, 3) != "EVD") throw new Exception("not EVD");
            e.Head = new byte[0x90]; Buffer.BlockCopy(b, 0, e.Head, 0, 0x90);
            long evCount = L(b, 0x10), evOff = L(b, 0x18), insOff = L(b, 0x28), layerCount = L(b, 0x40), layerOff = L(b, 0x48);
            long parOff = L(b, 0x58), linkCount = L(b, 0x60), linkOff = L(b, 0x68), argsLen = L(b, 0x70), argsOff = L(b, 0x78), strLen = L(b, 0x80), strOff = L(b, 0x88);
            for (long i = 0; i < evCount; i++)
            {
                long p = evOff + i * 0x30;
                var ev = new EvEvent();
                ev.Id = L(b, p);
                long ic = L(b, p + 8), io = L(b, p + 0x10), pc = L(b, p + 0x18), po = L(b, p + 0x20);
                ev.Rest = BitConverter.ToInt32(b, (int)p + 0x28); ev.Unk = BitConverter.ToInt32(b, (int)p + 0x2C);
                for (long k = 0; k < ic; k++)
                {
                    long q = insOff + io + k * 0x20;
                    var ins = new EvInstr();
                    ins.Bank = BitConverter.ToInt32(b, (int)q); ins.Id = BitConverter.ToInt32(b, (int)q + 4);
                    long al = L(b, q + 8), ao = L(b, q + 0x10); ins.Layer = L(b, q + 0x18);
                    ins.Args = new byte[al]; Buffer.BlockCopy(b, (int)(argsOff + ao), ins.Args, 0, (int)al);
                    if (ins.Layer != -1) { ins.LayerData = new byte[0x20]; Buffer.BlockCopy(b, (int)(layerOff + ins.Layer), ins.LayerData, 0, 0x20); }
                    ev.Instrs.Add(ins);
                }
                if (po != -1)
                    for (long k = 0; k < pc; k++)
                    {
                        long q = parOff + po + k * 0x20;
                        ev.Params.Add(new EvParam { InstrIndex = L(b, q), TargetStart = L(b, q + 8), SourceStart = L(b, q + 0x10), ByteCount = BitConverter.ToInt32(b, (int)q + 0x18), Unk = BitConverter.ToInt32(b, (int)q + 0x1C) });
                    }
                e.Events.Add(ev);
            }
            e.LinkedCount = linkCount;
            e.LinkedFiles = new byte[linkCount * 8]; Buffer.BlockCopy(b, (int)linkOff, e.LinkedFiles, 0, e.LinkedFiles.Length);
            e.Strings = new byte[strLen]; Buffer.BlockCopy(b, (int)strOff, e.Strings, 0, (int)strLen);
            return e;
        }

        public byte[] Write()
        {
            int insCount = Events.Sum(e => e.Instrs.Count);
            int parCount = Events.Sum(e => e.Params.Count);
            if (Events.Any(e => e.Instrs.Any(i => i.Layer != -1))) throw new Exception("layers not supported");
            var ms = new MemoryStream(); var bw = new BinaryWriter(ms);
            byte[] head = (byte[])Head.Clone();
            bw.Write(head);
            long evOff = 0x90, insOff = evOff + 0x30L * Events.Count, layerOff = insOff + 0x20L * insCount;
            // args
            var args = new MemoryStream();
            var argOffs = new List<long>();
            foreach (var ev in Events) foreach (var ins in ev.Instrs)
            {
                argOffs.Add(ins.Args.Length > 0 ? args.Length : -1L);
                args.Write(ins.Args, 0, ins.Args.Length);
                while (args.Length % 4 != 0) args.WriteByte(0);
            }
            while (args.Length % 0x10 != 0) args.WriteByte(0);
            long argsOff = layerOff, parOff = argsOff + args.Length, linkOff = parOff + 0x20L * parCount, strOff = linkOff + LinkedFiles.Length;
            int ii = 0, pi = 0;
            foreach (var ev in Events)
            {
                bw.Write(ev.Id); bw.Write((long)ev.Instrs.Count);
                bw.Write(ev.Instrs.Count > 0 ? (long)ii * 0x20 : -1L);
                bw.Write((long)ev.Params.Count);
                bw.Write(ev.Params.Count > 0 ? (long)pi * 0x20 : -1L);
                bw.Write(ev.Rest); bw.Write(ev.Unk);
                ii += ev.Instrs.Count; pi += ev.Params.Count;
            }
            int ai = 0;
            foreach (var ev in Events) foreach (var ins in ev.Instrs)
            {
                bw.Write(ins.Bank); bw.Write(ins.Id); bw.Write((long)ins.Args.Length); bw.Write(argOffs[ai++]); bw.Write(-1L);
            }
            args.WriteTo(ms);
            foreach (var ev in Events) foreach (var p in ev.Params)
            {
                bw.Write(p.InstrIndex); bw.Write(p.TargetStart); bw.Write(p.SourceStart); bw.Write(p.ByteCount); bw.Write(p.Unk);
            }
            bw.Write(LinkedFiles);
            bw.Write(Strings);
            long end = ms.Position;
            ms.Position = 0xC; bw.Write((int)end);
            ms.Position = 0x10; bw.Write((long)Events.Count); bw.Write(evOff); bw.Write((long)insCount); bw.Write(insOff);
            bw.Write(0L); bw.Write(layerOff); bw.Write(0L); bw.Write(layerOff);
            bw.Write((long)parCount); bw.Write(parOff); bw.Write(LinkedCount); bw.Write(linkOff);
            bw.Write(args.Length); bw.Write(argsOff); bw.Write((long)Strings.Length); bw.Write(strOff);
            return ms.ToArray();
        }

        public string Dump()
        {
            var sb = new StringBuilder();
            foreach (var ev in Events)
            {
                sb.AppendLine("EVENT " + ev.Id + " rest=" + ev.Rest + " params=" + ev.Params.Count);
                for (int i = 0; i < ev.Instrs.Count; i++)
                {
                    var ins = ev.Instrs[i];
                    var ints = new List<string>();
                    for (int k = 0; k + 4 <= ins.Args.Length; k += 4) ints.Add(BitConverter.ToInt32(ins.Args, k).ToString());
                    sb.AppendLine("  " + i + ": " + ins.Bank + "[" + ins.Id + "] (" + string.Join(", ", ints) + ")" + (ins.Args.Length % 4 != 0 ? " +" + ins.Args.Length % 4 + "b" : "") + (ins.Layer != -1 ? " layer" : ""));
                }
                foreach (var p in ev.Params) sb.AppendLine("  P instr=" + p.InstrIndex + " tgt=" + p.TargetStart + " src=" + p.SourceStart + " n=" + p.ByteCount);
            }
            return sb.ToString();
        }
    }
}
