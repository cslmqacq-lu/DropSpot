using System.Drawing.Drawing2D;

namespace DropSpot;

/// <summary>设置界面的配色（与悬浮舱一致）。</summary>
internal static class SettingsPalette
{
    public static readonly Color Nav = Color.FromArgb(11, 17, 26);
    public static readonly Color Content = Color.FromArgb(16, 23, 34);
    public static readonly Color Card = Color.FromArgb(21, 30, 44);
    public static readonly Color CardBorder = Color.FromArgb(36, 50, 70);
    public static readonly Color Hover = Color.FromArgb(28, 40, 56);
    public static readonly Color Muted = Color.FromArgb(138, 155, 176);
    public static readonly Color Faint = Color.FromArgb(111, 129, 153);
    public static readonly Color Green = Color.FromArgb(61, 220, 132);
    public static readonly Color GreenDark = Color.FromArgb(30, 110, 72);
    public static readonly Color Danger = Color.FromArgb(248, 113, 113);

    public static GraphicsPath Rounded(RectangleF rect, float radius)
    {
        var path = new GraphicsPath();
        var d = Math.Min(radius * 2, Math.Min(rect.Width, rect.Height));
        if (d <= 0.5F)
        {
            path.AddRectangle(rect);
            return path;
        }

        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}

/// <summary>开关样式的复选框：左侧药丸开关，右侧文字。仍是 CheckBox，Checked 语义不变。</summary>
internal sealed class ToggleSwitch : CheckBox
{
    public ToggleSwitch()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        AutoSize = false;
        Height = 30;
        Cursor = Cursors.Hand;
        ForeColor = Theme.Text;
        BackColor = SettingsPalette.Content;
        Font = new Font("Microsoft YaHei UI", 9.5F);
    }

    protected override void OnCheckedChanged(EventArgs e)
    {
        base.OnCheckedChanged(e);
        Invalidate();
    }

    protected override void OnMouseEnter(EventArgs eventargs)
    {
        base.OnMouseEnter(eventargs);
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs eventargs)
    {
        base.OnMouseLeave(eventargs);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs pevent)
    {
        var g = pevent.Graphics;
        g.Clear(BackColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var scale = DeviceDpi / 96F;
        var trackW = 36 * scale;
        var trackH = 20 * scale;
        var track = new RectangleF(1, (Height - trackH) / 2F, trackW, trackH);
        var hovered = ClientRectangle.Contains(PointToClient(Cursor.Position));
        var trackColor = !Enabled
            ? Color.FromArgb(40, 50, 64)
            : Checked ? SettingsPalette.Green : hovered ? Color.FromArgb(58, 76, 102) : Color.FromArgb(48, 62, 84);
        using (var path = SettingsPalette.Rounded(track, trackH / 2))
        using (var brush = new SolidBrush(trackColor))
        {
            g.FillPath(brush, path);
        }

        var knob = trackH - 6 * scale;
        var knobX = Checked ? track.Right - knob - 3 * scale : track.X + 3 * scale;
        using (var knobBrush = new SolidBrush(Checked ? Color.FromArgb(11, 17, 26) : Color.FromArgb(214, 222, 232)))
        {
            g.FillEllipse(knobBrush, knobX, track.Y + 3 * scale, knob, knob);
        }

        var textRect = new Rectangle((int)(track.Right + 10 * scale), 0, Width - (int)(track.Right + 10 * scale), Height);
        TextRenderer.DrawText(g, Text, Font, textRect, Enabled ? ForeColor : Theme.Dim,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        if (Focused && ShowFocusCues)
        {
            ControlPaint.DrawFocusRectangle(g, textRect, ForeColor, BackColor);
        }
    }
}

/// <summary>左侧导航按钮：选中时左侧绿条 + 浅底。</summary>
internal sealed class NavButton : Button
{
    private bool _selected;

    public NavButton(string text, string glyph)
    {
        Text = text;
        Glyph = glyph;
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        FlatAppearance.MouseOverBackColor = SettingsPalette.Hover;
        FlatAppearance.MouseDownBackColor = SettingsPalette.Hover;
        BackColor = SettingsPalette.Nav;
        ForeColor = SettingsPalette.Muted;
        Font = new Font("Microsoft YaHei UI", 10F);
        TextAlign = ContentAlignment.MiddleLeft;
        Height = 40;
        Cursor = Cursors.Hand;
        TabStop = true;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }

    public string Glyph { get; }

    public bool Selected
    {
        get => _selected;
        set
        {
            _selected = value;
            Invalidate();
        }
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs pevent)
    {
        var g = pevent.Graphics;
        var hovered = ClientRectangle.Contains(PointToClient(Cursor.Position));
        g.Clear(SettingsPalette.Nav);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var scale = DeviceDpi / 96F;
        var box = new RectangleF(8 * scale, 2 * scale, Width - 16 * scale, Height - 4 * scale);
        if (_selected || hovered)
        {
            using var path = SettingsPalette.Rounded(box, 8 * scale);
            using var brush = new SolidBrush(_selected ? Color.FromArgb(26, 38, 54) : SettingsPalette.Hover);
            g.FillPath(brush, path);
        }

        if (_selected)
        {
            using var accent = new SolidBrush(SettingsPalette.Green);
            using var bar = SettingsPalette.Rounded(new RectangleF(box.X, box.Y + 10 * scale, 3 * scale, box.Height - 20 * scale), 1.5F * scale);
            g.FillPath(accent, bar);
        }

        var color = _selected ? Theme.Text : hovered ? Theme.Text : SettingsPalette.Muted;
        using var iconFont = new Font("Segoe MDL2 Assets", 11F);
        TextRenderer.DrawText(g, Glyph, iconFont, new Rectangle((int)(box.X + 14 * scale), 0, (int)(22 * scale), Height), color,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        TextRenderer.DrawText(g, Text, Font, new Rectangle((int)(box.X + 44 * scale), 0, Width - (int)(box.X + 48 * scale), Height), color,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }
}

/// <summary>简洁的数值滑块（悬浮舱背景不透明度）。</summary>
internal sealed class ValueSlider : Control
{
    private int _value = 90;
    private bool _dragging;

    public ValueSlider()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        Height = 28;
        Cursor = Cursors.Hand;
        BackColor = SettingsPalette.Content;
        TabStop = true;
    }

    public int Minimum { get; set; } = 50;

    public int Maximum { get; set; } = 100;

    public int Value
    {
        get => _value;
        set
        {
            var clamped = Math.Clamp(value, Minimum, Maximum);
            if (clamped == _value)
            {
                return;
            }

            _value = clamped;
            Invalidate();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? ValueChanged;

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        _dragging = true;
        SetFromX(e.X);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragging)
        {
            SetFromX(e.X);
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _dragging = false;
    }

    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Left or Keys.Right || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Left)
        {
            Value -= 5;
        }
        else if (e.KeyCode == Keys.Right)
        {
            Value += 5;
        }
    }

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        Invalidate();
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        Invalidate();
    }

    private void SetFromX(int x)
    {
        var scale = DeviceDpi / 96F;
        var left = 9 * scale;
        var right = Width - 9 * scale;
        var ratio = Math.Clamp((x - left) / Math.Max(1, right - left), 0, 1);
        Value = (int)Math.Round(Minimum + ratio * (Maximum - Minimum));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var scale = DeviceDpi / 96F;
        var left = 9 * scale;
        var right = Width - 9 * scale;
        var y = Height / 2F;
        var ratio = (float)(_value - Minimum) / Math.Max(1, Maximum - Minimum);
        var knobX = left + ratio * (right - left);
        using (var track = new Pen(Color.FromArgb(48, 62, 84), 4 * scale) { StartCap = LineCap.Round, EndCap = LineCap.Round })
        {
            g.DrawLine(track, left, y, right, y);
        }

        using (var fill = new Pen(SettingsPalette.Green, 4 * scale) { StartCap = LineCap.Round, EndCap = LineCap.Round })
        {
            g.DrawLine(fill, left, y, knobX, y);
        }

        var r = (Focused ? 9 : 8) * scale;
        using var knob = new SolidBrush(Color.FromArgb(230, 237, 245));
        g.FillEllipse(knob, knobX - r, y - r, r * 2, r * 2);
    }
}
