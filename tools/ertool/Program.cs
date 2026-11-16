using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace ErTool
{
    static partial class Program
    {
        public static string Md5(byte[] b)
        {
            using (var m = MD5.Create()) return BitConverter.ToString(m.ComputeHash(b)).Replace("-", "").Substring(0, 10).ToLower();
        }

        // Open a container file: returns decompressed bytes. Handles regulation encryption.
        public static byte[] Open(string path, out Dcx dcx, out bool encrypted)
        {
            byte[] b = File.ReadAllBytes(path);
            encrypted = false;
            if (!Dcx.IsDcx(b) && path.EndsWith("regulation.bin", StringComparison.OrdinalIgnoreCase))
            {
                b = Regulation.Decrypt(b);
                encrypted = true;
            }
            return Dcx.Decompress(b, out dcx);
        }

        static int Main(string[] args)
        {
            try
            {
                string cmd = args[0];
                if (cmd == "list")
                {
                    Dcx d; bool enc;
                    byte[] raw = Open(args[1], out d, out enc);
                    Console.WriteLine("dcx=" + d.Format + " enc=" + enc + " rawsize=" + raw.Length + " magic=" + Encoding.ASCII.GetString(raw, 0, 4));
                    if (d.Header != null) Console.WriteLine("dcxhdr " + BitConverter.ToString(d.Header, 0, 0x4C));
                    var bnd = Bnd4.Read(raw);
                    Console.WriteLine("files=" + bnd.Files.Count + " fmt=" + bnd.Fmt.ToString("X2") + " raw=" + bnd.RawFormat.ToString("X2") + " ext=" + bnd.Extended + " uni=" + bnd.Unicode);
                    foreach (var f in bnd.Files) Console.WriteLine(f.ID + "\t" + f.Flags.ToString("X2") + "\t" + f.Bytes.Length + "\t" + Md5(f.Bytes) + "\t" + f.Name);
                }
                else if (cmd == "roundtrip")
                {
                    Dcx d; bool enc;
                    byte[] raw = Open(args[1], out d, out enc);
                    var bnd = Bnd4.Read(raw);
                    byte[] w = bnd.Write();
                    Console.WriteLine("raw " + raw.Length + " " + Md5(raw) + "  rewritten " + w.Length + " " + Md5(w));
                    if (w.Length == raw.Length)
                    {
                        int diffs = 0;
                        for (int i = 0; i < w.Length; i++) if (w[i] != raw[i]) { if (diffs < 10) Console.WriteLine("diff at 0x" + i.ToString("X")); diffs++; }
                        Console.WriteLine("diffs=" + diffs);
                    }
                }
                else if (cmd == "dcxd")
                {
                    Dcx d; bool enc;
                    File.WriteAllBytes(args[2], Open(args[1], out d, out enc));
                    Console.WriteLine(d.Format);
                }
                else if (cmd == "extract")
                {
                    Dcx d; bool enc;
                    var bnd = Bnd4.Read(Open(args[1], out d, out enc));
                    foreach (var f in bnd.Files)
                    {
                        string n = f.Name;
                        int c = n.IndexOf(':'); if (c >= 0) n = n.Substring(c + 1);
                        n = n.TrimStart('\\', '/');
                        string p = Path.Combine(args[2], n);
                        Directory.CreateDirectory(Path.GetDirectoryName(p));
                        File.WriteAllBytes(p, f.Bytes);
                    }
                }
                else return Extra(args);
                return 0;
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(e);
                return 1;
            }
        }
    }
}
