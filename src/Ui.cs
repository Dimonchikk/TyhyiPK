// Тихий ПК — кольори, шрифти й допоміжне малювання в стилі Apple.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace TyhyiPK
{
    static class Ui
    {
        public const TextFormatFlags Left = TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;
        public const TextFormatFlags Center = Left | TextFormatFlags.HorizontalCenter;
        public const TextFormatFlags Right = Left | TextFormatFlags.Right;

        public static float Scale;
        public static bool Dark;
        public static Color Bg, Card, Ink, Ink2, Ink3, Separator, Fill, FillHover, Blue, Green, Red, Orange, Purple,
            SegText, SegSep, SegDisabled, Thumb, ToggleOff, RowSel, RowHover, Side, CardLine;
        public static int ShadowAlpha;
        public static readonly List<Action> Rethemers;
        public static readonly Font Title, Body, BodySemi, Small, Caps, Stat, H1, Brand, Section;
        public static readonly Func<Color> CInk, CInk2, CInk3, CBg, CCard, CSide;

        static Ui()
        {
            Scale = 1f;
            Rethemers = new List<Action>();
            Title = Make(17f, true);
            Body = Make(9.75f, false);
            BodySemi = Make(9.75f, true);
            Small = Make(8.75f, false);
            Caps = Make(8f, false);
            Stat = Make(17f, true);
            H1 = Make(20f, true);
            Brand = Make(14f, true);
            Section = Make(11.5f, true);
            CInk = () => Ink;
            CInk2 = () => Ink2;
            CInk3 = () => Ink3;
            CBg = () => Bg;
            CCard = () => Card;
            CSide = () => Side;
            ApplyTheme(false);
        }

        public static int S(float v) { return (int)Math.Round(v * Scale); }
        public static Rectangle R(float x, float y, float w, float h) { return new Rectangle(S(x), S(y), S(w), S(h)); }

        static Font Make(float size, bool semi) { return new Font(semi ? "Segoe UI Semibold" : "Segoe UI", size); }
        static Color C(int r, int g, int b) { return Color.FromArgb(r, g, b); }

        public static void ApplyTheme(bool dark)
        {
            Dark = dark;
            if (!dark)
            {
                Bg = C(242, 242, 247); Card = Color.White;
                Ink = C(28, 28, 30); Ink2 = C(99, 99, 102); Ink3 = C(142, 142, 147);
                Separator = C(229, 229, 234); Fill = C(232, 232, 237); FillHover = C(220, 220, 226);
                Blue = C(0, 122, 255); Green = C(52, 199, 89); Red = C(255, 59, 48); Orange = C(255, 149, 0); Purple = C(175, 82, 222);
                SegText = C(60, 60, 67); SegSep = C(206, 206, 211); SegDisabled = C(240, 240, 243);
                Thumb = Color.White; ToggleOff = C(229, 229, 234); RowSel = C(225, 238, 255); RowHover = C(246, 246, 248);
                ShadowAlpha = 30;
                Bg = C(247, 248, 250); Side = C(236, 238, 242); CardLine = C(230, 231, 236);
            }
            else
            {
                Bg = C(22, 22, 24); Card = C(38, 38, 41);
                Ink = C(245, 245, 247); Ink2 = C(174, 174, 178); Ink3 = C(118, 118, 124);
                Separator = C(56, 56, 60); Fill = C(54, 54, 58); FillHover = C(66, 66, 70);
                Blue = C(10, 132, 255); Green = C(48, 209, 88); Red = C(255, 69, 58); Orange = C(255, 159, 10); Purple = C(191, 90, 242);
                SegText = C(220, 220, 225); SegSep = C(84, 84, 88); SegDisabled = C(44, 44, 47);
                Thumb = C(99, 99, 104); ToggleOff = C(72, 72, 76); RowSel = C(28, 56, 96); RowHover = C(48, 48, 52);
                ShadowAlpha = 60;
                Side = C(30, 30, 33); CardLine = C(52, 52, 56);
            }
        }

        public static bool SystemDark()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize"))
                {
                    object v = k != null ? k.GetValue("AppsUseLightTheme") : null;
                    return v is int && (int)v == 0;
                }
            }
            catch { return false; }
        }

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        // Темний заголовок вікна (Windows 10/11) і колір рамки в тон фону.
        public static void StyleCaption(Form f)
        {
            try
            {
                int dark = Dark ? 1 : 0;
                DwmSetWindowAttribute(f.Handle, 20, ref dark, 4);
                int caption = Bg.R | (Bg.G << 8) | (Bg.B << 16);
                DwmSetWindowAttribute(f.Handle, 35, ref caption, 4);
            }
            catch { }
        }

        public static GraphicsPath Round(RectangleF r, float rad)
        {
            var p = new GraphicsPath();
            float d = Math.Min(rad * 2, Math.Min(r.Width, r.Height));
            if (!(d > 1)) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void FillRound(Graphics g, RectangleF r, float rad, Color c)
        {
            using (var p = Round(r, rad))
            using (var b = new SolidBrush(c))
                g.FillPath(b, p);
        }

        public static Color Mix(Color a, Color b, float t)
        {
            t = Math.Max(0f, Math.Min(1f, t));
            return Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }

        public static void Text(Graphics g, string s, Font f, Rectangle r, Color c, TextFormatFlags flags)
        {
            TextRenderer.DrawText(g, s, f, r, c, flags | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
        }

        public static Label MakeLabel(Control parent, string text, Rectangle r, Font f, Func<Color> fore, Func<Color> back)
        {
            var l = new Label();
            l.Text = text;
            l.Bounds = r;
            l.Font = f;
            l.UseMnemonic = false;
            Action apply = () => { l.ForeColor = fore(); l.BackColor = back(); };
            apply();
            Rethemers.Add(apply);
            parent.Controls.Add(l);
            return l;
        }

        public static Color TempColor(double t)
        {
            if (t >= 85) return Red;
            if (t >= 72) return Orange;
            return Ink;
        }
    }
}
