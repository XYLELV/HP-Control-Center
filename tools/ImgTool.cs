// Asset prep for the control panel: decode (incl. WebP via WIC), remove a flat background, crop, build .ico.
//   ImgTool info   <in>
//   ImgTool cutout <in> <out.png> [tolerance]        flood-fill the border background to transparent, trim
//   ImgTool crop   <in> <out.png> <x> <y> <w> <h> [maxSide]
//   ImgTool ico    <in> <out.ico>                     16..256 px PNG-compressed icon
using System;
using System.IO;
using System.Collections.Generic;
using System.Windows.Media;
using System.Windows.Media.Imaging;

static class ImgTool
{
    static int W, H;

    static byte[] Load(string path)
    {
        BitmapSource f;
        using (var fs = File.OpenRead(path))
            f = BitmapDecoder.Create(fs, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
        var c = new FormatConvertedBitmap(f, PixelFormats.Bgra32, null, 0);
        W = c.PixelWidth; H = c.PixelHeight;
        var px = new byte[W * H * 4];
        c.CopyPixels(px, W * 4, 0);
        return px;
    }

    static BitmapSource ToBitmap(byte[] px, int w, int h)
    {
        var b = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, px, w * 4);
        b.Freeze();
        return b;
    }

    static void SavePng(BitmapSource b, string path)
    {
        var e = new PngBitmapEncoder();
        e.Frames.Add(BitmapFrame.Create(b));
        using (var f = File.Create(path)) e.Save(f);
    }

    static BitmapSource Resize(BitmapSource src, int w, int h)
    {
        // High-quality downscale through a DrawingVisual with Fant scaling.
        var dv = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(dv, BitmapScalingMode.Fant);
        using (var dc = dv.RenderOpen()) dc.DrawImage(src, new System.Windows.Rect(0, 0, w, h));
        var r = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        r.Render(dv);
        var c = new FormatConvertedBitmap(r, PixelFormats.Bgra32, null, 0);
        c.Freeze();
        return c;
    }

    static string Px(byte[] px, int x, int y)
    {
        int i = (y * W + x) * 4;
        return string.Format("({0},{1})={2},{3},{4},a{5}", x, y, px[i + 2], px[i + 1], px[i], px[i + 3]);
    }

    [STAThread]
    static int Main(string[] a)
    {
        switch (a[0])
        {
            case "info":
            {
                var px = Load(a[1]);
                Console.WriteLine(W + "x" + H);
                var pts = new[] { new[] { 0, 0 }, new[] { 3, 3 }, new[] { 20, 20 }, new[] { 60, 60 }, new[] { W / 2, 2 }, new[] { W / 2, 25 },
                                  new[] { 3, H / 2 }, new[] { 40, H / 2 }, new[] { W - 4, H / 2 }, new[] { W - 4, 3 }, new[] { 3, H - 4 }, new[] { W / 2, H - 4 }, new[] { W - 4, H - 4 } };
                foreach (var p in pts) Console.Write(Px(px, p[0], p[1]) + "  ");
                Console.WriteLine();
                return 0;
            }
            case "cutout":
            {
                var px = Load(a[1]);
                int tol = a.Length > 3 ? int.Parse(a[3]) : 24;
                // Background colour = average of the four corners.
                int[] cx = { 1, W - 2, 1, W - 2 }, cy = { 1, 1, H - 2, H - 2 };
                int br = 0, bg = 0, bb = 0;
                for (int k = 0; k < 4; k++) { int i = (cy[k] * W + cx[k]) * 4; bb += px[i]; bg += px[i + 1]; br += px[i + 2]; }
                br /= 4; bg /= 4; bb /= 4;
                Func<int, int> dist = i => Math.Max(Math.Abs(px[i + 2] - br), Math.Max(Math.Abs(px[i + 1] - bg), Math.Abs(px[i] - bb)));

                var isBg = new bool[W * H];
                var q = new Queue<int>();
                Action<int, int> seed = (x, y) => { int p = y * W + x; if (!isBg[p] && dist(p * 4) <= tol) { isBg[p] = true; q.Enqueue(p); } };
                for (int x = 0; x < W; x++) { seed(x, 0); seed(x, H - 1); }
                for (int y = 0; y < H; y++) { seed(0, y); seed(W - 1, y); }
                while (q.Count > 0)
                {
                    int p = q.Dequeue(), x = p % W, y = p / W;
                    if (x > 0) seed(x - 1, y);
                    if (x < W - 1) seed(x + 1, y);
                    if (y > 0) seed(x, y - 1);
                    if (y < H - 1) seed(x, y + 1);
                }

                // Distance (in px, up to 3) from the background, for a soft, de-fringed edge.
                var ring = new int[W * H];
                for (int p = 0; p < W * H; p++) ring[p] = isBg[p] ? 0 : 99;
                for (int pass = 1; pass <= 3; pass++)
                    for (int y = 0; y < H; y++)
                        for (int x = 0; x < W; x++)
                        {
                            int p = y * W + x;
                            if (ring[p] != 99) continue;
                            bool near = (x > 0 && ring[p - 1] == pass - 1) || (x < W - 1 && ring[p + 1] == pass - 1) ||
                                        (y > 0 && ring[p - W] == pass - 1) || (y < H - 1 && ring[p + W] == pass - 1);
                            if (near) ring[p] = pass;
                        }

                int minX = W, minY = H, maxX = 0, maxY = 0;
                for (int p = 0; p < W * H; p++)
                {
                    int i = p * 4;
                    if (isBg[p]) { px[i] = px[i + 1] = px[i + 2] = px[i + 3] = 0; continue; }
                    if (ring[p] <= 2)
                    {
                        // "Colour to alpha" against the background colour on the outer 2 px only.
                        double al = 0;
                        int[] bgc = { bb, bg, br };
                        for (int ch = 0; ch < 3; ch++)
                        {
                            double c = px[i + ch], b = bgc[ch];
                            double need = c > b ? (c - b) / (255 - b + 1e-9) : (b - c) / (b + 1e-9);
                            al = Math.Max(al, need);
                        }
                        al = Math.Min(1, Math.Max(al, ring[p] == 2 ? 0.6 : 0.15));
                        for (int ch = 0; ch < 3; ch++)
                        {
                            double v = (px[i + ch] - bgc[ch] * (1 - al)) / al;
                            px[i + ch] = (byte)Math.Max(0, Math.Min(255, Math.Round(v)));
                        }
                        px[i + 3] = (byte)Math.Round(al * 255);
                    }
                    int xx = p % W, yy = p / W;
                    if (xx < minX) minX = xx; if (xx > maxX) maxX = xx; if (yy < minY) minY = yy; if (yy > maxY) maxY = yy;
                }
                var full = ToBitmap(px, W, H);
                var cropped = new CroppedBitmap(full, new System.Windows.Int32Rect(minX, minY, maxX - minX + 1, maxY - minY + 1));
                SavePng(cropped, a[2]);
                int bgCount = 0; foreach (var v in isBg) if (v) bgCount++;
                Console.WriteLine("bg colour " + br + "," + bg + "," + bb + "  removed " + (100.0 * bgCount / (W * H)).ToString("F1") + "%  bbox " + minX + "," + minY + " " + (maxX - minX + 1) + "x" + (maxY - minY + 1));
                return 0;
            }
            case "crop":
            {
                var px = Load(a[1]);
                int x = int.Parse(a[3]), y = int.Parse(a[4]), w = Math.Min(int.Parse(a[5]), W - x), h = Math.Min(int.Parse(a[6]), H - y);
                BitmapSource b = new CroppedBitmap(ToBitmap(px, W, H), new System.Windows.Int32Rect(x, y, w, h));
                if (a.Length > 7)
                {
                    int max = int.Parse(a[7]);
                    if (Math.Max(w, h) > max) { double s = (double)max / Math.Max(w, h); b = Resize(b, (int)Math.Round(w * s), (int)Math.Round(h * s)); }
                }
                SavePng(b, a[2]);
                Console.WriteLine("cropped " + b.PixelWidth + "x" + b.PixelHeight);
                return 0;
            }
            case "ico":
            {
                var px = Load(a[1]);
                var src = ToBitmap(px, W, H);
                int[] sizes = { 16, 20, 24, 32, 40, 48, 64, 96, 128, 256 };
                var images = new List<byte[]>();
                foreach (int s in sizes)
                {
                    var e = new PngBitmapEncoder();
                    e.Frames.Add(BitmapFrame.Create(Resize(src, s, s)));
                    using (var ms = new MemoryStream()) { e.Save(ms); images.Add(ms.ToArray()); }
                }
                using (var f = new BinaryWriter(File.Create(a[2])))
                {
                    f.Write((short)0); f.Write((short)1); f.Write((short)sizes.Length);
                    int offset = 6 + 16 * sizes.Length;
                    for (int k = 0; k < sizes.Length; k++)
                    {
                        f.Write((byte)(sizes[k] >= 256 ? 0 : sizes[k])); f.Write((byte)(sizes[k] >= 256 ? 0 : sizes[k]));
                        f.Write((byte)0); f.Write((byte)0); f.Write((short)1); f.Write((short)32);
                        f.Write(images[k].Length); f.Write(offset);
                        offset += images[k].Length;
                    }
                    foreach (var img in images) f.Write(img);
                }
                Console.WriteLine("ico with " + sizes.Length + " sizes");
                return 0;
            }
        }
        return 1;
    }
}
