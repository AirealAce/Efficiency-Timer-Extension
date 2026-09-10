using ReflectionTimer.Core;

namespace ReflectionTimer.Accessible;

internal record PreviewPalette(Color Background, Color Raised, Color Border, Color Text, Color Muted, Color Accent, Color Selection, Color SelectionText);

internal static class PreviewTheme
{
    internal static PreviewPalette Palette(AppColorTheme theme, bool contrast = false)
    {
        if(contrast)return new(SystemColors.Control,SystemColors.Control,SystemColors.WindowText,SystemColors.ControlText,SystemColors.GrayText,SystemColors.Highlight,SystemColors.Highlight,SystemColors.HighlightText);
        static Color C(int value)=>Color.FromArgb(value>>16&255,value>>8&255,value&255);
        var colors=theme switch {
            AppColorTheme.Light=>new[]{0xF3F6FA,0xE5EBF2,0x6F7C8D,0x182537,0x44546A,0x12603E,0xC8DFD4,0x143F2D},
            AppColorTheme.HighContrast=>new[]{0,0,0xFFFFFF,0xFFFFFF,0xFFFFFF,0xFFFF00,0xFFFF00,0},
            AppColorTheme.Glamour=>new[]{0xFFF1F7,0xF9D6E5,0x965172,0x4A1934,0x6E3B55,0xA51D5C,0xE9D5F4,0x4A1934},
            _=>new[]{0x161A22,0x2C3543,0x4E5B70,0xEFF4FA,0xB5C1D2,0x69DFB0,0x30594D,0xEFF4FA}
        };
        return new(C(colors[0]),C(colors[1]),C(colors[2]),C(colors[3]),C(colors[4]),C(colors[5]),C(colors[6]),C(colors[7]));
    }
    internal static void ApplyMenu(ContextMenuStrip menu, PreviewPalette palette)
    {
        menu.BackColor=palette.Raised;menu.ForeColor=palette.Text;
        menu.Renderer=new MenuRenderer(palette);
        foreach(ToolStripItem item in menu.Items){item.BackColor=palette.Raised;item.ForeColor=palette.Text;}
    }
    private sealed class MenuColors(PreviewPalette p) : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground=>p.Raised;
        public override Color ImageMarginGradientBegin=>p.Raised;
        public override Color ImageMarginGradientMiddle=>p.Raised;
        public override Color ImageMarginGradientEnd=>p.Raised;
        public override Color MenuBorder=>p.Border;
        public override Color MenuItemBorder=>p.Accent;
        public override Color MenuItemSelected=>p.Selection;
        public override Color MenuItemSelectedGradientBegin=>p.Selection;
        public override Color MenuItemSelectedGradientEnd=>p.Selection;
        public override Color SeparatorDark=>p.Border;
        public override Color SeparatorLight=>p.Raised;
    }
    private sealed class MenuRenderer(PreviewPalette p) : ToolStripProfessionalRenderer(new MenuColors(p))
    {
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor=!e.Item.Enabled?p.Muted:e.Item.Selected?p.SelectionText:p.Text;base.OnRenderItemText(e);
        }
        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor=e.Item?.Selected==true?p.SelectionText:p.Text;base.OnRenderArrow(e);
        }
    }
}
