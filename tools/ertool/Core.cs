using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace ErTool
{
    static class Oodle
    {
        [DllImport("oo2core_6_win64.dll", CallingConvention = CallingConvention.StdCall)]
        static extern long OodleLZ_Decompress(byte[] compBuf, long compBufSize, byte[] rawBuf, long rawLen,
            int fuzzSafe, int checkCRC, int verbosity, IntPtr decBufBase, long decBufSize, IntPtr fpCallback,
            IntPtr callbackUserData, IntPtr decoderMemory, long decoderMemorySize, int threadPhase);

        [DllImport("oo2core_6_win64.dll", CallingConvention = CallingConvention.StdCall)]
        static extern long OodleLZ_Compress(int compressor, byte[] rawBuf, long rawLen, byte[] compBuf, int level,
            IntPtr pOptions, IntPtr dictionaryBase, IntPtr lrm, IntPtr scratchMem, long scratchSize);

        public static byte[] Decompress(byte[] comp, long rawLen)
        {
            byte[] raw = new byte[rawLen];
            long r = OodleLZ_Decompress(comp, comp.Length, raw, rawLen, 1, 0, 0, IntPtr.Zero, 0, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, 3);
            if (r != rawLen) throw new Exception("Oodle decompress failed: " + r);
            return raw;
        }

        [DllImport("oo2core_6_win64.dll", CallingConvention = CallingConvention.StdCall)]
        static extern IntPtr OodleLZ_CompressOptions_GetDefault(int compressor, int level);

        public static byte[] Compress(byte[] raw, int level)
        {
            byte[] comp = new byte[raw.Length + 274 * ((raw.Length + 0x3FFFF) / 0x40000) + 0x10000];
            // The game decodes Kraken data in independent 256 KB seek chunks: the encoder must reset at each chunk
            // (seekChunkReset = true, seekChunkLen = 0x40000), otherwise the game crashes while loading the file.
            IntPtr def = OodleLZ_CompressOptions_GetDefault(8, level);
            IntPtr opts = Marshal.AllocHGlobal(0x100);
            long n;
            try
            {
                byte[] tmp = new byte[0x40];
                Marshal.Copy(def, tmp, 0, 0x40);
                BitConverter.GetBytes(1).CopyTo(tmp, 8);        // seekChunkReset
                BitConverter.GetBytes(0x40000).CopyTo(tmp, 12); // seekChunkLen
                Marshal.Copy(tmp, 0, opts, 0x40);
                n = OodleLZ_Compress(8, raw, raw.Length, comp, level, opts, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0);
            }
            finally { Marshal.FreeHGlobal(opts); }
            if (n <= 0) throw new Exception("Oodle compress failed");
            byte[] o = new byte[n];
            Buffer.BlockCopy(comp, 0, o, 0, (int)n);
            return o;
        }
    }

    static class BE
    {
        public static int I32(byte[] b, int o) { return (b[o] << 24) | (b[o + 1] << 16) | (b[o + 2] << 8) | b[o + 3]; }
        public static void W32(byte[] b, int o, int v) { b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16); b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v; }
    }

    class Dcx
    {
        public string Format; // DFLT / KRAK / NONE
        public byte[] Header; // first 0x4C bytes, kept as template

        public static bool IsDcx(byte[] b) { return b.Length > 4 && b[0] == 'D' && b[1] == 'C' && b[2] == 'X' && b[3] == 0; }

        public static byte[] Decompress(byte[] b, out Dcx info)
        {
            info = new Dcx();
            if (!IsDcx(b)) { info.Format = "NONE"; return b; }
            info.Format = Encoding.ASCII.GetString(b, 0x28, 4);
            info.Header = new byte[0x4C];
            Buffer.BlockCopy(b, 0, info.Header, 0, 0x4C);
            int unc = BE.I32(b, 0x1C);
            int comp = BE.I32(b, 0x20);
            byte[] data = new byte[comp];
            Buffer.BlockCopy(b, 0x4C, data, 0, Math.Min(comp, b.Length - 0x4C));
            if (info.Format == "DFLT")
            {
                using (var ms = new MemoryStream(data, 2, data.Length - 2))
                using (var ds = new DeflateStream(ms, CompressionMode.Decompress))
                using (var outp = new MemoryStream(unc))
                {
                    ds.CopyTo(outp);
                    return outp.ToArray();
                }
            }
            if (info.Format == "KRAK") return Oodle.Decompress(data, unc);
            if (info.Format == "ZSTD") return Zstd.Decompress(data, unc);
            throw new Exception("Unsupported DCX format " + info.Format);
        }

        public byte[] Compress(byte[] raw)
        {
            if (Format == "NONE") return raw;
            byte[] comp;
            if (Format == "DFLT")
            {
                using (var ms = new MemoryStream())
                {
                    ms.WriteByte(0x78); ms.WriteByte(0xDA);
                    using (var ds = new DeflateStream(ms, CompressionLevel.Optimal, true)) ds.Write(raw, 0, raw.Length);
                    uint a = Adler32(raw);
                    ms.WriteByte((byte)(a >> 24)); ms.WriteByte((byte)(a >> 16)); ms.WriteByte((byte)(a >> 8)); ms.WriteByte((byte)a);
                    comp = ms.ToArray();
                }
            }
            else if (Format == "KRAK") comp = Oodle.Compress(raw, Header[0x30]);
            else if (Format == "ZSTD") comp = Zstd.Compress(raw, Header[0x30]);
            else throw new Exception("Unsupported DCX format " + Format);
            byte[] o = new byte[(0x4C + comp.Length + 15) / 16 * 16]; // zero-padded to 16 like the game's own files
            Buffer.BlockCopy(Header, 0, o, 0, 0x4C);
            BE.W32(o, 0x1C, raw.Length);
            BE.W32(o, 0x20, comp.Length);
            Buffer.BlockCopy(comp, 0, o, 0x4C, comp.Length);
            return o;
        }

        static uint Adler32(byte[] d)
        {
            uint a = 1, b = 0;
            int i = 0;
            while (i < d.Length)
            {
                int n = Math.Min(3800, d.Length - i);
                for (int k = 0; k < n; k++) { a += d[i++]; b += a; }
                a %= 65521; b %= 65521;
            }
            return (b << 16) | a;
        }
    }

    class BndFile
    {
        public byte Flags;
        public int ID;
        public string Name;
        public byte[] Bytes;
        public BndFile Clone() { return new BndFile { Flags = Flags, ID = ID, Name = Name, Bytes = Bytes }; }
    }

    class Bnd4
    {
        public byte[] Head; // 0x40 raw header template
        public bool Unicode;
        public byte RawFormat;
        public byte Fmt; // logical format bits
        public byte Extended;
        public List<BndFile> Files = new List<BndFile>();

        const byte F_BE = 1, F_IDS = 2, F_N1 = 4, F_N2 = 8, F_LONG = 0x10, F_COMP = 0x20;

        static byte Rev(byte b) { byte r = 0; for (int i = 0; i < 8; i++) if ((b & (1 << i)) != 0) r |= (byte)(0x80 >> i); return r; }

        static byte ReadFormat(byte raw, bool bitBigEndian)
        {
            bool reverse = bitBigEndian || ((raw & 1) != 0 && (raw & 0x80) == 0);
            return reverse ? raw : Rev(raw);
        }

        int FileHeaderSize()
        {
            int s = 0x10 + ((Fmt & F_LONG) != 0 ? 8 : 4);
            if ((Fmt & F_COMP) != 0) s += 8;
            if ((Fmt & F_IDS) != 0) s += 4;
            if ((Fmt & (F_N1 | F_N2)) != 0) s += 4;
            if (Fmt == F_N1) s += 8;
            return s;
        }

        public static Bnd4 Read(byte[] b)
        {
            if (Encoding.ASCII.GetString(b, 0, 4) != "BND4") throw new Exception("Not BND4");
            var bnd = new Bnd4();
            bnd.Head = new byte[0x40];
            Buffer.BlockCopy(b, 0, bnd.Head, 0, 0x40);
            if (b[9] != 0) throw new Exception("Big endian BND4 not supported");
            bool bitBigEndian = b[0xA] == 0;
            int count = BitConverter.ToInt32(b, 0xC);
            long fhs = BitConverter.ToInt64(b, 0x20);
            bnd.Unicode = b[0x30] != 0;
            bnd.RawFormat = b[0x31];
            bnd.Fmt = ReadFormat(b[0x31], bitBigEndian);
            bnd.Extended = b[0x32];
            if (bnd.FileHeaderSize() != fhs) throw new Exception("File header size mismatch " + fhs + " vs " + bnd.FileHeaderSize() + " fmt " + bnd.Fmt.ToString("X2"));
            int p = 0x40;
            for (int i = 0; i < count; i++, p += (int)fhs)
            {
                var f = new BndFile();
                f.Flags = b[p];
                int q = p + 8;
                long csize = BitConverter.ToInt64(b, q); q += 8;
                if ((bnd.Fmt & F_COMP) != 0) q += 8;
                long off;
                if ((bnd.Fmt & F_LONG) != 0) { off = BitConverter.ToInt64(b, q); q += 8; } else { off = BitConverter.ToUInt32(b, q); q += 4; }
                if ((bnd.Fmt & F_IDS) != 0) { f.ID = BitConverter.ToInt32(b, q); q += 4; }
                if ((bnd.Fmt & (F_N1 | F_N2)) != 0)
                {
                    int no = BitConverter.ToInt32(b, q); q += 4;
                    f.Name = ReadStr(b, no, bnd.Unicode);
                }
                f.Bytes = new byte[csize];
                Buffer.BlockCopy(b, (int)off, f.Bytes, 0, (int)csize);
                bnd.Files.Add(f);
            }
            return bnd;
        }

        static string ReadStr(byte[] b, int o, bool uni)
        {
            int e = o;
            if (uni) { while (b[e] != 0 || b[e + 1] != 0) e += 2; return Encoding.Unicode.GetString(b, o, e - o); }
            while (b[e] != 0) e++;
            return Encoding.GetEncoding(932).GetString(b, o, e - o);
        }

        public static uint PathHash(string s)
        {
            string h = s.Trim().Replace('\\', '/').ToLowerInvariant();
            if (!h.StartsWith("/")) h = "/" + h;
            uint r = 0;
            foreach (char c in h) r = r * 37u + c;
            return r;
        }

        static bool IsPrime(uint c)
        {
            if (c < 2) return false;
            if (c == 2) return true;
            if (c % 2 == 0) return false;
            for (uint i = 3; i * i <= c; i += 2) if (c % i == 0) return false;
            return true;
        }

        public byte[] Write()
        {
            var ms = new MemoryStream();
            var bw = new BinaryWriter(ms);
            byte[] head = (byte[])Head.Clone();
            BitConverter.GetBytes(Files.Count).CopyTo(head, 0xC);
            bw.Write(head);
            int fhs = FileHeaderSize();
            long[] hdrPos = new long[Files.Count];
            for (int i = 0; i < Files.Count; i++)
            {
                hdrPos[i] = ms.Position;
                bw.Write(new byte[fhs]);
            }
            int[] nameOff = new int[Files.Count];
            for (int i = 0; i < Files.Count; i++)
            {
                nameOff[i] = (int)ms.Position;
                if ((Fmt & (F_N1 | F_N2)) != 0)
                {
                    if (Unicode) { bw.Write(Encoding.Unicode.GetBytes(Files[i].Name)); bw.Write((short)0); }
                    else { bw.Write(Encoding.GetEncoding(932).GetBytes(Files[i].Name)); bw.Write((byte)0); }
                }
            }
            if (Extended == 4)
            {
                while (ms.Position % 8 != 0) bw.Write((byte)0);
                long hto = ms.Position;
                ms.Position = 0x38; bw.Write(hto); ms.Position = hto;
                uint groupCount = 0;
                for (uint pr = (uint)Files.Count / 7; pr <= 100000; pr++) if (IsPrime(pr)) { groupCount = pr; break; }
                var lists = new List<KeyValuePair<uint, int>>[groupCount];
                for (int i = 0; i < groupCount; i++) lists[i] = new List<KeyValuePair<uint, int>>();
                for (int i = 0; i < Files.Count; i++)
                {
                    uint h = PathHash(Files[i].Name);
                    lists[h % groupCount].Add(new KeyValuePair<uint, int>(h, i));
                }
                foreach (var l in lists) l.Sort((x, y) => x.Key.CompareTo(y.Key));
                long phoPos = ms.Position;
                bw.Write(0L);
                bw.Write(groupCount);
                bw.Write(0x00080810);
                int idx = 0;
                foreach (var l in lists) { bw.Write(l.Count); bw.Write(idx); idx += l.Count; }
                long pho = ms.Position;
                ms.Position = phoPos; bw.Write(pho); ms.Position = pho;
                foreach (var l in lists) foreach (var kv in l) { bw.Write(kv.Key); bw.Write(kv.Value); }
            }
            long headersEnd = ms.Position;
            ms.Position = 0x28; bw.Write(headersEnd); ms.Position = headersEnd;
            long[] dataOff = new long[Files.Count];
            for (int i = 0; i < Files.Count; i++)
            {
                if (Files[i].Bytes.Length > 0) while (ms.Position % 0x10 != 0) bw.Write((byte)0);
                dataOff[i] = ms.Position;
                bw.Write(Files[i].Bytes);
            }
            for (int i = 0; i < Files.Count; i++)
            {
                ms.Position = hdrPos[i];
                var f = Files[i];
                bw.Write(f.Flags); bw.Write((byte)0); bw.Write((short)0); bw.Write(-1);
                bw.Write((long)f.Bytes.Length);
                if ((Fmt & F_COMP) != 0) bw.Write((long)f.Bytes.Length);
                if ((Fmt & F_LONG) != 0) bw.Write(dataOff[i]); else bw.Write((uint)dataOff[i]);
                if ((Fmt & F_IDS) != 0) bw.Write(f.ID);
                if ((Fmt & (F_N1 | F_N2)) != 0) bw.Write(nameOff[i]);
            }
            return ms.ToArray();
        }
    }

    static class Regulation
    {
        static readonly byte[] Key = Hex("99 BF FC 36 6A 6B C8 C6 F5 82 7D 09 36 02 D6 76 C4 28 92 A0 1C 20 7F B0 24 D3 AF 4E 49 3F EF 99");

        public static byte[] Hex(string s)
        {
            var parts = s.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            return parts.Select(x => Convert.ToByte(x, 16)).ToArray();
        }

        public static byte[] Decrypt(byte[] b)
        {
            byte[] iv = new byte[16];
            Buffer.BlockCopy(b, 0, iv, 0, 16);
            using (var aes = Aes.Create())
            {
                aes.Mode = CipherMode.CBC; aes.Padding = PaddingMode.None; aes.Key = Key; aes.IV = iv;
                using (var d = aes.CreateDecryptor()) return d.TransformFinalBlock(b, 16, b.Length - 16);
            }
        }

        public static byte[] Encrypt(byte[] plain, byte[] iv)
        {
            // zero-pad the DCX to 16, then PKCS7 (adds a full 0x10 block) — same as the shipped regulation.bin
            int len = (plain.Length + 15) / 16 * 16;
            byte[] p = new byte[len];
            Buffer.BlockCopy(plain, 0, p, 0, plain.Length);
            using (var aes = Aes.Create())
            {
                aes.Mode = CipherMode.CBC; aes.Padding = PaddingMode.PKCS7; aes.Key = Key; aes.IV = iv;
                using (var e = aes.CreateEncryptor())
                {
                    byte[] c = e.TransformFinalBlock(p, 0, p.Length);
                    byte[] o = new byte[16 + c.Length];
                    Buffer.BlockCopy(iv, 0, o, 0, 16);
                    Buffer.BlockCopy(c, 0, o, 16, c.Length);
                    return o;
                }
            }
        }
    }
}
