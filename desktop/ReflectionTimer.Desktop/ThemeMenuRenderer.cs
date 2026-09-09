namespace ReflectionTimer.Desktop;

internal sealed class ThemeMenuRenderer : ToolStripProfessionalRenderer
{
    private sealed class Colors : ProfessionalColorTable
    {
        public Colors() { UseSystemColors = false; }
        public override Color ToolStripDropDownBackground => AppTheme.Raised;
        public override Color ImageMarginGradientBegin => AppTheme.Raised;
        public override Color ImageMarginGradientMiddle => AppTheme.Raised;
        public override Color ImageMarginGradientEnd => AppTheme.Raised;
        public override Color MenuBorder => AppTheme.Border;
        public override Color MenuItemBorder => AppTheme.Accent;
        public override Color MenuItemSelected => AppTheme.Selection;
        public override Color MenuItemSelectedGradientBegin => AppTheme.Selection;
        public override Color MenuItemSelectedGradientEnd => AppTheme.Selection;
        public override Color MenuItemPressedGradientBegin => AppTheme.Selection;
        public override Color MenuItemPressedGradientMiddle => AppTheme.Selection;
        public override Color MenuItemPressedGradientEnd => AppTheme.Selection;
        public override Color SeparatorDark => AppTheme.Border;
        public override Color SeparatorLight => AppTheme.Raised;
    }
    public ThemeMenuRenderer() : base(new Colors()) { RoundedEdges = false; }
    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = !e.Item.Enabled ? AppTheme.Muted : e.Item.Selected ? AppTheme.SelectionText : AppTheme.Text;
        base.OnRenderItemText(e);
    }
    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor = e.Item?.Selected == true ? AppTheme.SelectionText : AppTheme.Text;
        base.OnRenderArrow(e);
    }
}
