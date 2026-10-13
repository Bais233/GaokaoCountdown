using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Text;

// 图标生成器：画日历风格图标（深底 + 金色 7），输出多尺�?.ico
public class MakeIcon
{
    static void Main()
    {
        string outPath = @"D:\114514\2026����\����\GaokaoCountdown\app.ico"; using (var b = Draw(256)) b.Save(@"D:\114514\2026����\����\GaokaoCountdown\icon_preview.png", ImageFormat.Png);
        int[] sizes = new int[] { 16, 24, 32, 48, 64, 128, 256 };
        List<byte[]> pngs = new List<byte[]>();
        foreach (int s in sizes)
        {
            using (Bitmap bmp = Draw(s))
            using (MemoryStream ms = new MemoryStream())
            {
                bmp.Save(ms, ImageFormat.Png);
                pngs.Add(ms.ToArray());
            }
        }
        WriteIco(outPath, sizes, pngs);
        Console.WriteLine("written: " + outPath);
    }

    static Bitmap Draw(int size)
    {
        Bitmap bmp = new Bitmap(size, size);
        using (Graphics g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            float u = size / 256f; // 缩放单位

            // 圆角方形深色�?            RectangleF bg = new RectangleF(4 * u, 4 * u, 248 * u, 248 * u);
            using (GraphicsPath p = RoundRect(bg, 56 * u))
            using (LinearGradientBrush lb = new LinearGradientBrush(bg,
                Color.FromArgb(45, 48, 78), Color.FromArgb(24, 25, 42), 90f))
            {
                g.FillPath(lb, p);
            }

            // 日历主体（白底圆角）
            RectangleF cal = new RectangleF(52 * u, 62 * u, 152 * u, 150 * u);
            using (GraphicsPath p = RoundRect(cal, 20 * u))
            using (SolidBrush b = new SolidBrush(Color.FromArgb(245, 246, 250)))
            {
                g.FillPath(b, p);
            }
            // 日历顶部红条
            RectangleF top = new RectangleF(52 * u, 62 * u, 152 * u, 36 * u);
            using (GraphicsPath clip = RoundRect(cal, 20 * u))
            using (SolidBrush b = new SolidBrush(Color.FromArgb(235, 90, 90)))
            {
                g.SetClip(clip);
                g.FillRectangle(b, top);
                g.ResetClip();
            }
            // 日历挂环
            using (SolidBrush b = new SolidBrush(Color.FromArgb(60, 62, 88)))
            {
                g.FillRectangle(b, 82 * u, 46 * u, 18 * u, 34 * u);
                g.FillRectangle(b, 156 * u, 46 * u, 18 * u, 34 * u);
            }
            // 中间金色 7
            using (Font f = new Font("Segoe UI", 92 * u / 1.33f, FontStyle.Bold, GraphicsUnit.Pixel))
            using (SolidBrush b = new SolidBrush(Color.FromArgb(255, 190, 60)))
            using (StringFormat sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                g.DrawString("7", f, b,
                    new RectangleF(52 * u, 100 * u, 152 * u, 108 * u), sf);
            }
            // 底部小字 GAO KAO
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

    static void WriteIco(string path, int[] sizes, List<byte[]> pngs)
    {
        using (FileStream fs = new FileStream(path, FileMode.Create))
        using (BinaryWriter w = new BinaryWriter(fs))
        {
            w.Write((short)0);          // reserved
            w.Write((short)1);          // type: icon
            w.Write((short)sizes.Length);
            long dataOffset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++)
            {
                int s = sizes[i];
                w.Write((byte)(s >= 256 ? 0 : s)); // width
                w.Write((byte)(s >= 256 ? 0 : s)); // height
                w.Write((byte)0);        // colors
                w.Write((byte)0);        // reserved
                w.Write((short)1);       // planes
                w.Write((short)32);      // bpp
                w.Write((int)pngs[i].Length);
                w.Write((int)dataOffset);
                dataOffset += pngs[i].Length;
            }
            foreach (byte[] png in pngs) w.Write(png);
        }
    }
}

