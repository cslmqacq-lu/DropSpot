namespace DropSpot;

/// <summary>深色右键菜单外观，与悬浮舱保持一致。</summary>
internal sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
{
    public DarkMenuRenderer()
        : base(new DarkMenuColors())
    {
        RoundedEdges = false;
    }

    public static ContextMenuStrip CreateMenu()
    {
        return new ContextMenuStrip
        {
            Renderer = new DarkMenuRenderer(),
            ShowImageMargin = false,
            BackColor = Color.FromArgb(23, 33, 49),
            ForeColor = Theme.Text,
            Font = new Font("Microsoft YaHei UI", 9F)
        };
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Enabled
            ? (e.Item.Tag as string == "danger" ? Color.FromArgb(245, 158, 139) : Theme.Text)
            : Theme.Dim;
        base.OnRenderItemText(e);
    }

    private sealed class DarkMenuColors : ProfessionalColorTable
    {
        private static readonly Color Background = Color.FromArgb(23, 33, 49);
        private static readonly Color Hover = Color.FromArgb(34, 48, 68);
        private static readonly Color Border = Color.FromArgb(42, 54, 72);

        public override Color ToolStripDropDownBackground => Background;
        public override Color MenuBorder => Border;
        public override Color MenuItemBorder => Hover;
        public override Color MenuItemSelected => Hover;
        public override Color MenuItemSelectedGradientBegin => Hover;
        public override Color MenuItemSelectedGradientEnd => Hover;
        public override Color MenuItemPressedGradientBegin => Hover;
        public override Color MenuItemPressedGradientEnd => Hover;
        public override Color ImageMarginGradientBegin => Background;
        public override Color ImageMarginGradientMiddle => Background;
        public override Color ImageMarginGradientEnd => Background;
        public override Color SeparatorDark => Border;
        public override Color SeparatorLight => Background;
    }
}
