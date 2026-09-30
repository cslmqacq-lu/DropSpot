namespace DropSpot;

public sealed class ActivityHistoryForm : Form
{
    private readonly Func<IReadOnlyList<FolderActivity>> _itemsProvider;
    private readonly Action<string> _openFolder;
    private readonly ListView _list = new();

    public ActivityHistoryForm(
        Func<IReadOnlyList<FolderActivity>> itemsProvider,
        Action<string> openFolder)
    {
        _itemsProvider = itemsProvider;
        _openFolder = openFolder;
        Text = "DropSpot 活动历史";
        Icon = AppIcon.Create();
        Size = new Size(720, 500);
        MinimumSize = new Size(540, 360);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Theme.Window;
        ForeColor = Theme.Text;
        Font = new Font("Microsoft YaHei UI", 9F);
        BuildUi();
        RefreshItems();
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            Padding = new Padding(12),
            BackColor = Theme.Window
        };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        Controls.Add(root);

        _list.Dock = DockStyle.Fill;
        _list.View = View.Details;
        _list.FullRowSelect = true;
        _list.MultiSelect = false;
        _list.HideSelection = false;
        _list.BackColor = Theme.Panel;
        _list.ForeColor = Theme.Text;
        _list.BorderStyle = BorderStyle.FixedSingle;
        _list.Columns.Add("文件夹", 180);
        _list.Columns.Add("路径", 330);
        _list.Columns.Add("最后活动", 150);
        _list.DoubleClick += (_, _) => OpenSelected();
        var menu = new ContextMenuStrip { ShowImageMargin = false };
        menu.Items.Add("打开文件夹", null, (_, _) => OpenSelected());
        menu.Items.Add("复制路径", null, (_, _) => CopySelected());
        _list.ContextMenuStrip = menu;
        root.Controls.Add(_list, 0, 0);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            BackColor = Theme.Window
        };
        root.Controls.Add(buttons, 0, 1);
        buttons.Controls.Add(CreateButton("关闭", (_, _) => Close()));
        buttons.Controls.Add(CreateButton("刷新", (_, _) => RefreshItems()));
    }

    private static Button CreateButton(string text, EventHandler click)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = true,
            Height = 28,
            Margin = new Padding(6, 7, 0, 0),
            FlatStyle = FlatStyle.Flat,
            BackColor = Theme.Panel,
            ForeColor = Theme.Text
        };
        button.FlatAppearance.BorderColor = Theme.Border;
        button.Click += click;
        return button;
    }

    private void RefreshItems()
    {
        _list.BeginUpdate();
        try
        {
            _list.Items.Clear();
            foreach (var activity in _itemsProvider())
            {
                var item = new ListViewItem(activity.DisplayName) { Tag = activity.FolderPath };
                item.SubItems.Add(activity.FolderPath);
                item.SubItems.Add(activity.LastTime.ToString("yyyy-MM-dd HH:mm:ss"));
                _list.Items.Add(item);
            }
        }
        finally
        {
            _list.EndUpdate();
        }
    }

    private void OpenSelected()
    {
        if (_list.SelectedItems.Count == 1 && _list.SelectedItems[0].Tag is string path)
        {
            _openFolder(path);
        }
    }

    private void CopySelected()
    {
        if (_list.SelectedItems.Count == 1 && _list.SelectedItems[0].Tag is string path)
        {
            Clipboard.SetText(path);
        }
    }
}
