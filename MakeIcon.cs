using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

// 图标生成器 v2：小尺寸 BMP 格式（.NET Framework 兼容），大尺寸 PNG 压缩
public class MakeIcon
{
    static void Main()
    {
        string icoPath = @"D:\114514\2026构建\测试\GaokaoCountdown\app.ico";
        int[] sizes = new int[] { 16, 24, 32, 48, 64, 128, 256 };
        List<byte[]> entries = new List<byte[]>();
        List<bool> isPng = new List<bool>();
        foreach (int s in sizes)
        {
            using (Bitmap bmp = Draw(s))
            {
                if (s >= 64)
                {
                    using (MemoryStream ms = new MemoryStream())
                    {
                        bmp.Save(ms, ImageFormat.Png);
                        entries.Add(ms.ToArray());
                        isPng.Add(true);
                    }
                }
                else
                {
                    entries.Add(ToBmpEntry(bmp));
                    isPng.Add(false);
                }
            }
        }
        WriteIco(icoPath, sizes, entries);
        // 预览图
        using (Bitmap prev = Draw(256))
            prev.Save(@"D:\114514\2026构建\测试\GaokaoCountdown\icon_preview.png", ImageFormat.Png);
        Console.WriteLine("written ok");
    }

    static Bitmap Draw(int size)
    {
        Bitmap bmp = new Bitmap(size, size);
        using (Graphics g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            float u = size / 256f;

            RectangleF bg = new RectangleF(4 * u, 4 * u, 248 * u, 248 * u);
            using (GraphicsPath p = RoundRect(bg, 56 * u))
            using (LinearGradientBrush lb = new LinearGradientBrush(bg,
                Color.FromArgb(45, 48, 78), Color.FromArgb(24, 25, 42), 90f))
            {
                g.FillPath(lb, p);
            }

            RectangleF cal = new RectangleF(52 * u, 62 * u, 152 * u, 150 * u);
            using (GraphicsPath p = RoundRect(cal, 20 * u))
            using (SolidBrush b = new SolidBrush(Color.FromArgb(245, 246, 250)))
            {
                g.FillPath(b, p);
            }
            RectangleF top = new RectangleF(52 * u, 62 * u, 152 * u, 36 * u);
            using (GraphicsPath clip = RoundRect(cal, 20 * u))
            using (SolidBrush b = new SolidBrush(Color.FromArgb(235, 90, 90)))
            {
                g.SetClip(clip);
                g.FillRectangle(b, top);
                g.ResetClip();
            }
            using (SolidBrush b = new SolidBrush(Color.FromArgb(60, 62, 88)))
            {
                g.FillRectangle(b, 82 * u, 46 * u, 18 * u, 34 * u);
                g.FillRectangle(b, 156 * u, 46 * u, 18 * u, 34 * u);
            }
            using (Font f = new Font("Segoe UI", 92 * u / 1.33f, FontStyle.Bold, GraphicsUnit.Pixel))
            using (SolidBrush b = new SolidBrush(Color.FromArgb(255, 190, 60)))
            using (StringFormat sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                g.DrawString("7", f, b, new RectangleF(52 * u, 100 * u, 152 * u, 108 * u), sf);
            }
            if (size >= 48)
            {
                using (Font f = new Font("Segoe UI", 16 * u / 1.33f, FontStyle.Bold, GraphicsUnit.Pixel))
                using (SolidBrush b = new SolidBrush(Color.FromArgb(160, 164, 190)))
                using (StringFormat sf = new StringFormat { Alignment = StringAlignment.Center })
                {
                    g.DrawString("2028·GAOKAO", f, b, new RectangleF(0, 220 * u, 256 * u, 24 * u), sf);
                }
            }
        }
        return bmp;
    }

    // 32bpp BGRA DIB（BITMAPINFOHEADER，高度×2，含全0 AND mask）
    static byte[] ToBmpEntry(Bitmap bmp)
    {
        int w = bmp.Width, h = bmp.Height;
        using (MemoryStream ms = new MemoryStream())
        using (BinaryWriter bw = new BinaryWriter(ms))
        {
            bw.Write(40);              // biSize
            bw.Write(w);               // biWidth
            bw.Write(h * 2);           // biHeight（XOR+AND 两倍）
            bw.Write((short)1);        // biPlanes
            bw.Write((short)32);       // biBitCount
            bw.Write(0);               // biCompression = BI_RGB
            bw.Write(w * h * 4);       // biSizeImage
            bw.Write(0); bw.Write(0);  // PelsPerMeter
            bw.Write(0); bw.Write(0);  // ClrUsed/Important

            // XOR 数据：自下而上，BGRA
            for (int y = h - 1; y >= 0; y--)
            {
                for (int x = 0; x < w; x++)
                {
                    Color c = bmp.GetPixel(x, y);
                    bw.Write(c.B);
                    bw.Write(c.G);
                    bw.Write(c.R);
                    bw.Write(c.A);
                }
            }
            // AND mask：1bpp，每行补齐到 4 字节，全 0 = 完全不透明（alpha 走 XOR 通道）
            int maskRowBytes = ((w + 31) / 32) * 4;
            for (int y = 0; y < h; y++)
                bw.Write(new byte[maskRowBytes]);

            return ms.ToArray();
        }
    }

    static GraphicsPath RoundRect(RectangleF r, float radius)
    {
        GraphicsPath p = new GraphicsPath();
        float d = radius * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    static void WriteIco(string path, int[] sizes, List<byte[]> entries)
    {
        using (FileStream fs = new FileStream(path, FileMode.Create))
        using (BinaryWriter w = new BinaryWriter(fs))
        {
            w.Write((short)0);
            w.Write((short)1);
            w.Write((short)sizes.Length);
            long dataOffset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++)
            {
                int s = sizes[i];
                w.Write((byte)(s >= 256 ? 0 : s));
                w.Write((byte)(s >= 256 ? 0 : s));
                w.Write((byte)0);
                w.Write((byte)0);
                w.Write((short)1);
                w.Write((short)32);
                w.Write((int)entries[i].Length);
                w.Write((int)dataOffset);
                dataOffset += entries[i].Length;
            }
            foreach (byte[] e in entries) w.Write(e);
        }
    }
}
