using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AchievementTracker
{
    /// <summary>Colours, fonts and drawing helpers shared by every window.</summary>
    public static class Theme
    {
        // Deep slate surfaces with one trophy-gold accent.
        public static readonly Color Base = ColorTranslator.FromHtml("#12151C");
        public static readonly Color Surface = ColorTranslator.FromHtml("#1A1F29");
        public static readonly Color Raised = ColorTranslator.FromHtml("#232A37");
        public static readonly Color Hover = ColorTranslator.FromHtml("#2A3242");
        public static readonly Color Border = ColorTranslator.FromHtml("#2F3849");
        public static readonly Color Text = ColorTranslator.FromHtml("#E8EBF1");
        public static readonly Color TextDim = ColorTranslator.FromHtml("#B4BBCB");
        public static readonly Color Muted = ColorTranslator.FromHtml("#8E97AB");
        public static readonly Color Accent = ColorTranslator.FromHtml("#F2B544");
        public static readonly Color AccentHover = ColorTranslator.FromHtml("#F7C56A");
        public static readonly Color OnAccent = ColorTranslator.FromHtml("#241A05");
        public static readonly Color Good = ColorTranslator.FromHtml("#5DCB8B");
        public static readonly Color Danger = ColorTranslator.FromHtml("#E5685F");

        private static readonly string BodyFamily = Pick("Segoe UI Variable Text", "Segoe UI");
        private static readonly string HeadFamily = Pick("Segoe UI Semibold", BodyFamily);

        public static readonly Font Title = new Font(HeadFamily, 18f);
        public static readonly Font Heading = new Font(HeadFamily, 11f);
        public static readonly Font Body = new Font(BodyFamily, 10f);
        public static readonly Font BodyStrong = new Font(HeadFamily, 10f);
        public static readonly Font Small = new Font(BodyFamily, 9f);

        private static string Pick(params string[] names)
        {
            var installed = new HashSet<string>(FontFamily.Families.Select(f => f.Name), StringComparer.OrdinalIgnoreCase);
            foreach (string n in names)
            {
                if (installed.Contains(n)) return n;
            }
            return "Segoe UI";
        }

        public static Font HeadFont(float size) => new Font(HeadFamily, size);

        public static Color Blend(Color a, Color b, float t)
        {
            t = Math.Max(0f, Math.Min(1f, t));
            return Color.FromArgb(
                (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t),
                (int)(a.B + (b.B - a.B) * t));
        }

        public static GraphicsPath RoundedRect(RectangleF r, float radius)
        {
            GraphicsPath path = new GraphicsPath();
            float d = Math.Min(radius * 2f, Math.Min(r.Width, r.Height));
            if (d <= 0f)
            {
                path.AddRectangle(r);
                return path;
            }
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static void Smooth(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        }

        public static void DrawText(Graphics g, string text, Font font, Color color, Rectangle bounds, TextFormatFlags extra = TextFormatFlags.Left)
        {
            TextRenderer.DrawText(g, text, font, bounds, color,
                extra | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
        }

        // A coloured rounded square with the game's first letter. The colour is derived from the seed, so it is stable.
        private static readonly Color[] AvatarColors =
        {
            ColorTranslator.FromHtml("#5B7DB1"), ColorTranslator.FromHtml("#7A6FB0"), ColorTranslator.FromHtml("#4E9A8A"),
            ColorTranslator.FromHtml("#B0805B"), ColorTranslator.FromHtml("#8A9A4E"), ColorTranslator.FromHtml("#B0607A")
        };

        public static void DrawAvatar(Graphics g, Rectangle r, string name, string seed, float radius = -1f)
        {
            int sum = 0;
            foreach (char c in seed ?? "") sum += c;
            Color baseColor = AvatarColors[sum % AvatarColors.Length];

            using (GraphicsPath path = RoundedRect(r, radius >= 0f ? radius : r.Width * 0.25f))
            using (LinearGradientBrush fill = new LinearGradientBrush(r, Blend(baseColor, Color.White, 0.12f), Blend(baseColor, Color.Black, 0.25f), 90f))
            {
                g.FillPath(fill, path);
            }

            string letter = string.IsNullOrWhiteSpace(name) ? "?" : name.Trim().Substring(0, 1).ToUpperInvariant();
            using (Font f = new Font(HeadFamily, Math.Min(r.Width, r.Height) * 0.42f, FontStyle.Regular, GraphicsUnit.Pixel))
            {
                TextRenderer.DrawText(g, letter, f, r, Color.White,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
        }

        // Cover art cropped to fill a rounded rectangle (like CSS "object-fit: cover"); letter tile if there is no image.
        public static void DrawCover(Graphics g, Rectangle r, Image? cover, string name, string seed, float radius)
        {
            bool drawn = false;
            if (cover != null)
            {
                GraphicsState state = g.Save();
                try
                {
                    using (GraphicsPath path = RoundedRect(r, radius))
                    {
                        g.SetClip(path);
                        float targetAspect = (float)r.Width / r.Height;
                        float sourceAspect = (float)cover.Width / cover.Height;
                        RectangleF src;
                        if (sourceAspect > targetAspect)
                        {
                            float w = cover.Height * targetAspect;
                            src = new RectangleF((cover.Width - w) / 2f, 0, w, cover.Height);
                        }
                        else
                        {
                            float h = cover.Width / targetAspect;
                            src = new RectangleF(0, (cover.Height - h) / 2f, cover.Width, h);
                        }
                        g.DrawImage(cover, r, src.X, src.Y, src.Width, src.Height, GraphicsUnit.Pixel);
                        drawn = true;
                    }
                }
                catch { }
                finally { g.Restore(state); }
            }
            if (!drawn) DrawAvatar(g, r, name, seed, radius);
        }

        // ---- Window chrome (dark title bar that matches the app) ----
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hwnd, string? appName, string? idList);

        public static void StyleWindow(Form form)
        {
            void Apply()
            {
                try
                {
                    int dark = 1;
                    DwmSetWindowAttribute(form.Handle, 20, ref dark, 4);       // dark title bar
                    int caption = ColorTranslator.ToWin32(Base);
                    DwmSetWindowAttribute(form.Handle, 35, ref caption, 4);    // caption colour (Windows 11)
                    int captionText = ColorTranslator.ToWin32(Text);
                    DwmSetWindowAttribute(form.Handle, 36, ref captionText, 4);
                }
                catch { }
            }

            if (form.IsHandleCreated) Apply();
            else form.HandleCreated += (s, e) => Apply();
        }

        public static void DarkScrollbars(Control control)
        {
            void Apply()
            {
                try { SetWindowTheme(control.Handle, "DarkMode_Explorer", null); }
                catch { }
            }

            if (control.IsHandleCreated) Apply();
            else control.HandleCreated += (s, e) => Apply();
        }
    }

    /// <summary>Rounded panel used for every card in the UI.</summary>
    public class CardPanel : Panel
    {
        public int Radius { get; set; } = 14;
        public Color Fill { get; set; } = Theme.Surface;

        public CardPanel()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Base;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            Theme.Smooth(g);
            using (GraphicsPath path = Theme.RoundedRect(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), Radius))
            using (SolidBrush fill = new SolidBrush(Fill))
            using (Pen pen = new Pen(Theme.Border))
            {
                g.FillPath(fill, path);
                g.DrawPath(pen, path);
            }
        }
    }

    public enum ButtonKind { Primary, Secondary, Ghost, DangerGhost }

    /// <summary>Flat rounded button with hover, pressed and keyboard-focus states.</summary>
    public class FlatButton : Control
    {
        private bool _hover;
        private bool _down;
        private bool _active;

        public ButtonKind Kind { get; set; } = ButtonKind.Secondary;
        public Color Backdrop { get; set; } = Theme.Surface;

        // For tab-style ghost buttons: draws the gold underline.
        public bool Active
        {
            get => _active;
            set { _active = value; Invalidate(); }
        }

        public FlatButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            Size = new Size(120, 38);
            Cursor = Cursors.Hand;
            Font = Theme.BodyStrong;
            TabStop = true;
        }

        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; _down = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) { _down = true; Invalidate(); }
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            _down = false;
            Invalidate();
            base.OnMouseUp(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) { _down = true; Invalidate(); }
            base.OnKeyDown(e);
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            if ((e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) && _down)
            {
                _down = false;
                Invalidate();
                OnClick(EventArgs.Empty);
            }
            base.OnKeyUp(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Backdrop);
            Theme.Smooth(g);

            Color bg = Backdrop;
            Color fg = Theme.Text;
            bool border = false;

            switch (Kind)
            {
                case ButtonKind.Primary:
                    bg = _down ? Theme.Blend(Theme.Accent, Color.Black, 0.15f) : _hover ? Theme.AccentHover : Theme.Accent;
                    fg = Theme.OnAccent;
                    break;
                case ButtonKind.Secondary:
                    bg = _down ? Theme.Hover : _hover ? Theme.Hover : Theme.Raised;
                    fg = Theme.Text;
                    border = true;
                    break;
                case ButtonKind.Ghost:
                    bg = (_hover || _down) ? Theme.Raised : Backdrop;
                    fg = (_hover || _active) ? Theme.Text : Theme.Muted;
                    break;
                case ButtonKind.DangerGhost:
                    bg = (_hover || _down) ? Theme.Blend(Backdrop, Theme.Danger, 0.16f) : Backdrop;
                    fg = Theme.Danger;
                    break;
            }

            if (!Enabled) { fg = Theme.Muted; bg = Theme.Blend(bg, Backdrop, 0.6f); }

            RectangleF rect = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            using (GraphicsPath path = Theme.RoundedRect(rect, 10f))
            using (SolidBrush fill = new SolidBrush(bg))
            {
                g.FillPath(fill, path);
                if (border)
                {
                    using (Pen pen = new Pen(Theme.Border)) g.DrawPath(pen, path);
                }
                if (Focused && ShowFocusCues)
                {
                    using (Pen ring = new Pen(Theme.Accent, 2f)) g.DrawPath(ring, path);
                }
            }

            TextRenderer.DrawText(g, Text, Font, new Rectangle(8, 0, Width - 16, Height), fg,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);

            if (_active && Kind == ButtonKind.Ghost)
            {
                int textWidth = TextRenderer.MeasureText(Text, Font).Width;
                using (SolidBrush underline = new SolidBrush(Theme.Accent))
                {
                    g.FillRectangle(underline, (Width - textWidth) / 2, Height - 4, textWidth, 2);
                }
            }
        }
    }

    /// <summary>Pill-shaped on/off switch with a short slide animation.</summary>
    public class ToggleSwitch : Control
    {
        private bool _checked;
        private float _t;
        private readonly System.Windows.Forms.Timer _timer = new System.Windows.Forms.Timer { Interval = 15 };

        public event EventHandler? CheckedChanged;
        public Color Backdrop { get; set; } = Theme.Surface;

        public bool Checked
        {
            get => _checked;
            set
            {
                if (_checked == value) return;
                _checked = value;
                if (IsHandleCreated) _timer.Start();
                else _t = value ? 1f : 0f;
                Invalidate();
                CheckedChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public ToggleSwitch()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            Size = new Size(44, 24);
            Cursor = Cursors.Hand;
            TabStop = true;
            _timer.Tick += (s, e) =>
            {
                float target = _checked ? 1f : 0f;
                _t += (target - _t) * 0.35f;
                if (Math.Abs(target - _t) < 0.02f) { _t = target; _timer.Stop(); }
                Invalidate();
            };
        }

        protected override void OnClick(EventArgs e)
        {
            Checked = !Checked;
            base.OnClick(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space) Checked = !Checked;
            base.OnKeyDown(e);
        }

        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _timer.Dispose();
            base.Dispose(disposing);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Backdrop);
            Theme.Smooth(g);

            RectangleF track = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            using (GraphicsPath path = Theme.RoundedRect(track, track.Height / 2f))
            using (SolidBrush fill = new SolidBrush(Theme.Blend(Theme.Hover, Theme.Accent, _t)))
            {
                g.FillPath(fill, path);
                if (Focused && ShowFocusCues)
                {
                    using (Pen ring = new Pen(Theme.Accent, 2f)) g.DrawPath(ring, path);
                }
            }

            float d = Height - 8f;
            float x = 4f + (Width - 8f - d) * _t;
            using (SolidBrush thumb = new SolidBrush(Theme.Blend(Theme.Muted, Color.White, _t)))
            {
                g.FillEllipse(thumb, x, 4f, d, d);
            }
        }
    }

    /// <summary>0-100 slider with a gold fill.</summary>
    public class ModernSlider : Control
    {
        private int _value = 50;
        private bool _drag;

        public event EventHandler? ValueChanged;
        public event EventHandler? ValueCommitted;   // raised when the user lets go
        public Color Backdrop { get; set; } = Theme.Surface;

        public int Value
        {
            get => _value;
            set
            {
                value = Math.Max(0, Math.Min(100, value));
                if (_value == value) return;
                _value = value;
                Invalidate();
                ValueChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public ModernSlider()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            Height = 28;
            Cursor = Cursors.Hand;
            TabStop = true;
        }

        private void SetFromX(int x)
        {
            double fraction = (x - 10) / (double)Math.Max(1, Width - 20);
            Value = (int)Math.Round(fraction * 100);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) { _drag = true; SetFromX(e.X); }
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_drag) SetFromX(e.X);
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (_drag) { _drag = false; Invalidate(); ValueCommitted?.Invoke(this, EventArgs.Empty); }
            base.OnMouseUp(e);
        }

        protected override bool IsInputKey(Keys keyData)
        {
            return keyData == Keys.Left || keyData == Keys.Right || keyData == Keys.Up || keyData == Keys.Down || base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            int step = 0;
            if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Down) step = -5;
            else if (e.KeyCode == Keys.Right || e.KeyCode == Keys.Up) step = 5;
            if (step != 0)
            {
                Value += step;
                ValueCommitted?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
            }
            base.OnKeyDown(e);
        }

        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Backdrop);
            Theme.Smooth(g);

            float cy = Height / 2f;
            float left = 10f;
            float right = Width - 10f;
            float thumbX = left + (right - left) * _value / 100f;

            using (GraphicsPath track = Theme.RoundedRect(new RectangleF(left, cy - 3f, right - left, 6f), 3f))
            using (SolidBrush trackBrush = new SolidBrush(Theme.Hover))
            {
                g.FillPath(trackBrush, track);
            }

            if (thumbX > left)
            {
                using (GraphicsPath filled = Theme.RoundedRect(new RectangleF(left, cy - 3f, thumbX - left, 6f), 3f))
                using (SolidBrush accent = new SolidBrush(Theme.Accent))
                {
                    g.FillPath(accent, filled);
                }
            }

            using (SolidBrush thumb = new SolidBrush(Color.White))
            {
                g.FillEllipse(thumb, thumbX - 8f, cy - 8f, 16f, 16f);
            }
            if ((Focused && ShowFocusCues) || _drag)
            {
                using (Pen ring = new Pen(Theme.Accent, 2f)) g.DrawEllipse(ring, thumbX - 9f, cy - 9f, 18f, 18f);
            }
        }
    }

    /// <summary>Dark single-line text input with a gold focus outline.</summary>
    public class ModernTextBox : Control
    {
        public TextBox Inner { get; }
        public Color Backdrop { get; set; } = Theme.Surface;
        public event EventHandler? Submitted;   // Enter pressed

        public string Value
        {
            get => Inner.Text;
            set => Inner.Text = value ?? "";
        }

        public string PlaceholderText
        {
            get => Inner.PlaceholderText;
            set => Inner.PlaceholderText = value;
        }

        public bool UseSystemPasswordChar
        {
            get => Inner.UseSystemPasswordChar;
            set => Inner.UseSystemPasswordChar = value;
        }

        public ModernTextBox()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Inner = new TextBox
            {
                BorderStyle = BorderStyle.None,
                BackColor = Theme.Raised,
                ForeColor = Theme.Text,
                Font = Theme.Body
            };
            Controls.Add(Inner);
            Height = 38;
            Inner.GotFocus += (s, e) => Invalidate();
            Inner.LostFocus += (s, e) => Invalidate();
            Inner.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.SuppressKeyPress = true;
                    Submitted?.Invoke(this, EventArgs.Empty);
                }
            };
            PositionInner();
        }

        private void PositionInner()
        {
            if ((object?)Inner == null) return;   // a resize can fire before Inner exists
            Inner.Left = 12;
            Inner.Width = Math.Max(10, Width - 24);
            Inner.Top = Math.Max(0, (Height - Inner.Height) / 2);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            PositionInner();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Backdrop);
            Theme.Smooth(g);

            using (GraphicsPath path = Theme.RoundedRect(new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f), 10f))
            using (SolidBrush fill = new SolidBrush(Theme.Raised))
            using (Pen pen = new Pen(Inner.Focused ? Theme.Accent : Theme.Border, Inner.Focused ? 1.5f : 1f))
            {
                g.FillPath(fill, path);
                g.DrawPath(pen, path);
            }
        }
    }

    /// <summary>A row of mutually exclusive options; the selected one is filled gold.</summary>
    public class SegmentedControl : Control
    {
        private string[] _items = new string[0];
        private int _selected;
        private int _hover = -1;

        public event EventHandler? SelectedChanged;
        public Color Backdrop { get; set; } = Theme.Surface;

        public string[] Items
        {
            get => _items;
            set { _items = value ?? new string[0]; Invalidate(); }
        }

        public int SelectedIndex
        {
            get => _selected;
            set
            {
                if (_items.Length > 0) value = Math.Max(0, Math.Min(_items.Length - 1, value));
                if (_selected == value) return;
                _selected = value;
                Invalidate();
                SelectedChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public SegmentedControl()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            Height = 38;
            Cursor = Cursors.Hand;
            TabStop = true;
        }

        private int IndexAt(int x)
        {
            if (_items.Length == 0) return -1;
            int index = (int)(x * _items.Length / (double)Math.Max(1, Width));
            return Math.Max(0, Math.Min(_items.Length - 1, index));
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int index = IndexAt(e.X);
            if (index != _hover) { _hover = index; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = -1;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && _items.Length > 0) SelectedIndex = IndexAt(e.X);
            base.OnMouseDown(e);
        }

        protected override bool IsInputKey(Keys keyData)
        {
            return keyData == Keys.Left || keyData == Keys.Right || base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Left) { SelectedIndex = _selected - 1; e.Handled = true; }
            else if (e.KeyCode == Keys.Right) { SelectedIndex = _selected + 1; e.Handled = true; }
            base.OnKeyDown(e);
        }

        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Backdrop);
            Theme.Smooth(g);

            using (GraphicsPath outer = Theme.RoundedRect(new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f), 10f))
            using (SolidBrush fill = new SolidBrush(Theme.Raised))
            using (Pen pen = new Pen(Focused && ShowFocusCues ? Theme.Accent : Theme.Border, Focused && ShowFocusCues ? 1.5f : 1f))
            {
                g.FillPath(fill, outer);
                g.DrawPath(pen, outer);
            }

            if (_items.Length == 0) return;
            float segmentWidth = (Width - 6f) / _items.Length;
            for (int i = 0; i < _items.Length; i++)
            {
                RectangleF seg = new RectangleF(3f + segmentWidth * i, 3f, segmentWidth, Height - 6f);
                bool selected = i == _selected;

                if (selected || i == _hover)
                {
                    using (GraphicsPath path = Theme.RoundedRect(seg, 8f))
                    using (SolidBrush brush = new SolidBrush(selected ? Theme.Accent : Theme.Hover))
                    {
                        g.FillPath(brush, path);
                    }
                }

                Rectangle textRect = new Rectangle((int)seg.X, (int)seg.Y, (int)seg.Width, (int)seg.Height);
                TextRenderer.DrawText(g, _items[i], Theme.BodyStrong, textRect, selected ? Theme.OnAccent : Theme.TextDim,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            }
        }
    }

    /// <summary>Small rounded status badge with a coloured dot.</summary>
    public class StatusPill : Control
    {
        private string _text = "";
        private Color _dot = Theme.Muted;

        public Color Backdrop { get; set; } = Theme.Base;

        public StatusPill()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Height = 32;
        }

        public void SetStatus(string text, Color dot)
        {
            _text = text;
            _dot = dot;
            Width = TextRenderer.MeasureText(text, Theme.Small).Width + 44;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Backdrop);
            Theme.Smooth(g);

            using (GraphicsPath path = Theme.RoundedRect(new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f), Height / 2f))
            using (SolidBrush fill = new SolidBrush(Theme.Surface))
            using (Pen pen = new Pen(Theme.Border))
            using (SolidBrush dot = new SolidBrush(_dot))
            {
                g.FillPath(fill, path);
                g.DrawPath(pen, path);
                g.FillEllipse(dot, 14, (Height - 8) / 2, 8, 8);
            }
            Theme.DrawText(g, _text, Theme.Small, Theme.TextDim, new Rectangle(30, 0, Width - 38, Height));
        }
    }

    /// <summary>Circular completion ring with a percentage in the middle.</summary>
    public class ProgressRing : Control
    {
        private float _value;
        private readonly Font _font = Theme.HeadFont(15f);

        public Color Backdrop { get; set; } = Theme.Surface;

        public float Value
        {
            get => _value;
            set { _value = Math.Max(0f, Math.Min(1f, value)); Invalidate(); }
        }

        public ProgressRing()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Size = new Size(84, 84);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _font.Dispose();
            base.Dispose(disposing);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Backdrop);
            Theme.Smooth(g);

            const float thickness = 8f;
            RectangleF ring = new RectangleF(thickness / 2f + 1f, thickness / 2f + 1f, Width - thickness - 2f, Height - thickness - 2f);

            using (Pen track = new Pen(Theme.Hover, thickness))
            {
                g.DrawEllipse(track, ring);
            }
            if (_value > 0f)
            {
                using (Pen arc = new Pen(Theme.Accent, thickness) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                {
                    g.DrawArc(arc, ring, -90f, Math.Min(359.9f, 360f * _value));
                }
            }

            TextRenderer.DrawText(g, ((int)Math.Round(_value * 100)) + "%", _font, ClientRectangle, Theme.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    /// <summary>Static avatar tile (first letter of a game on a coloured square).</summary>
    public class AvatarBox : Control
    {
        public string GameName { get; set; } = "";
        public string Seed { get; set; } = "";
        public Image? Cover { get; set; }
        public Color Backdrop { get; set; } = Theme.Surface;

        public AvatarBox()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Size = new Size(56, 56);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Backdrop);
            Theme.Smooth(e.Graphics);
            Theme.DrawCover(e.Graphics, new Rectangle(0, 0, Width, Height), Cover, GameName, Seed, 10f);
        }
    }

    /// <summary>
    /// Owner-drawn list box with real double buffering (the stock ListBox flickers) and hover tracking.
    /// Subscribe to DrawItem to paint each row.
    /// </summary>
    public class RowListBox : ListBox
    {
        public int HoverIndex { get; private set; } = -1;

        public RowListBox()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            DrawMode = DrawMode.OwnerDrawFixed;
            BorderStyle = BorderStyle.None;
            IntegralHeight = false;
            BackColor = Theme.Surface;
            ForeColor = Theme.Text;
            Theme.DarkScrollbars(this);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            Theme.Smooth(g);
            for (int i = TopIndex; i < Items.Count; i++)
            {
                Rectangle r = GetItemRectangle(i);
                if (r.Top >= Height) break;
                DrawItemState state = SelectedIndices.Contains(i) ? DrawItemState.Selected : DrawItemState.None;
                OnDrawItem(new DrawItemEventArgs(g, Font, r, i, state, ForeColor, BackColor));
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int idx = IndexFromPoint(e.Location);
            if (idx != HoverIndex) { HoverIndex = idx; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            if (HoverIndex != -1) { HoverIndex = -1; Invalidate(); }
            base.OnMouseLeave(e);
        }

        protected override void OnSelectedIndexChanged(EventArgs e)
        {
            Invalidate();
            base.OnSelectedIndexChanged(e);
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            const int WM_VSCROLL = 0x0115;
            const int WM_MOUSEWHEEL = 0x020A;
            if (m.Msg == WM_VSCROLL || m.Msg == WM_MOUSEWHEEL) Invalidate();
        }
    }
}
