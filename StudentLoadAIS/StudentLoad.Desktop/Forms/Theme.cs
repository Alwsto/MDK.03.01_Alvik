namespace StudentLoad.Desktop.Forms;

internal static class Theme
{
    internal static readonly Color Navy = Color.FromArgb(24, 39, 62);
    internal static readonly Color NavyLight = Color.FromArgb(39, 59, 87);
    internal static readonly Color Ink = Color.FromArgb(30, 47, 67);
    internal static readonly Color Muted = Color.FromArgb(78, 98, 119);
    internal static readonly Color Canvas = Color.FromArgb(239, 244, 249);
    internal static readonly Color Border = Color.FromArgb(211, 221, 231);
    internal static readonly Color Accent = Color.FromArgb(19, 112, 118);
    internal static readonly Color AccentLight = Color.FromArgb(220, 245, 244);
    internal static readonly Color Error = Color.FromArgb(150, 58, 42);
    internal static readonly Color Caution = Color.FromArgb(127, 78, 17);

    internal static Font Font(float size = 10, FontStyle style = FontStyle.Regular) =>
        new("Segoe UI", size, style, GraphicsUnit.Point);

    internal static Label Label(string text, float size = 10, bool bold = false, Color? color = null) =>
        new()
        {
            Text = text, AutoSize = true, Font = Font(size, bold ? FontStyle.Bold : FontStyle.Regular),
            ForeColor = color ?? Ink, UseMnemonic = false
        };

    internal static Button Button(string text, int width = 125, bool primary = false)
    {
        var button = new Button
        {
            Text = text, Width = width, Height = 34, Font = Font(9.5f, FontStyle.Bold),
            FlatStyle = FlatStyle.Flat, UseVisualStyleBackColor = false,
            BackColor = primary ? Accent : Color.White, ForeColor = primary ? Color.White : Ink,
            Cursor = Cursors.Hand, Margin = new Padding(0, 0, 8, 0)
        };
        button.FlatAppearance.BorderColor = primary ? Accent : Border;
        button.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(11, 85, 90) : AccentLight;
        return button;
    }

    internal static ComboBox Combo(string name, int width = 170) => new()
    {
        Name = name, Width = width, Height = 31, Font = Font(), DropDownStyle = ComboBoxStyle.DropDownList,
        DisplayMember = nameof(Services.CatalogItem.Name), ValueMember = nameof(Services.CatalogItem.Id),
        IntegralHeight = false, DropDownHeight = 215, AccessibleName = name
    };

    internal static DataGridView CreateGrid(string accessibleName)
    {
        var grid = new DataGridView
        {
            Dock = DockStyle.Fill, BackgroundColor = Color.White, BorderStyle = BorderStyle.FixedSingle,
            GridColor = Border, RowHeadersVisible = false, EnableHeadersVisualStyles = false,
            AutoGenerateColumns = false, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false, ReadOnly = true, MultiSelect = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect, ColumnHeadersHeight = 38,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
            ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single,
            AccessibleName = accessibleName,
            Font = Font()
        };
        grid.RowTemplate.Height = 40;
        grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Color.FromArgb(226, 234, 243), ForeColor = Ink,
            Font = Font(9, FontStyle.Bold), Padding = new Padding(3, 0, 2, 0)
        };
        grid.DefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Color.White, ForeColor = Ink, SelectionBackColor = AccentLight,
            SelectionForeColor = Ink, WrapMode = DataGridViewTriState.False, Padding = new Padding(3, 0, 2, 0)
        };
        grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(246, 249, 252);
        return grid;
    }

    internal static void Column(DataGridView grid, string property, string text, float weight, int width = 50)
    {
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = property, HeaderText = text, DataPropertyName = property,
            FillWeight = weight, MinimumWidth = width, SortMode = DataGridViewColumnSortMode.NotSortable
        });
    }
}
