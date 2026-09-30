namespace DropSpot;

internal sealed class AddFavoriteForm : Form
{
    private readonly TextBox _pathBox = new();
    private readonly Label _errorLabel = new();

    public AddFavoriteForm()
    {
        Text = "添加收藏文件夹";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(460, 174);
        BackColor = Theme.Window;
        ForeColor = Theme.Text;
        Font = new Font("Microsoft YaHei UI", 9F);
        AutoScaleMode = AutoScaleMode.Dpi;

        BuildUi();
    }

    public string SelectedPath { get; private set; } = string.Empty;

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        WindowChrome.ApplyDarkTitleBar(this);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _pathBox.Focus();
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(16, 14, 16, 12),
            BackColor = Theme.Window
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        Controls.Add(root);

        root.Controls.Add(new Label
        {
            Text = "文件夹路径",
            Dock = DockStyle.Fill,
            ForeColor = Theme.Text,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);

        var inputRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty,
            BackColor = Theme.Window
        };
        inputRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        inputRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 76F));
        root.Controls.Add(inputRow, 0, 1);

        _pathBox.Dock = DockStyle.Fill;
        _pathBox.Margin = new Padding(0, 3, 8, 3);
        _pathBox.BackColor = Theme.Panel;
        _pathBox.ForeColor = Theme.Text;
        _pathBox.BorderStyle = BorderStyle.FixedSingle;
        _pathBox.PlaceholderText = @"例如 D:\项目\素材";
        _pathBox.TextChanged += (_, _) => ClearError();
        inputRow.Controls.Add(_pathBox, 0, 0);

        var browseButton = CreateButton("浏览...", primary: false);
        browseButton.Dock = DockStyle.Fill;
        browseButton.Margin = new Padding(0, 3, 0, 3);
        browseButton.Click += (_, _) => BrowseFolder();
        inputRow.Controls.Add(browseButton, 1, 0);

        _errorLabel.Dock = DockStyle.Fill;
        _errorLabel.ForeColor = Color.FromArgb(248, 113, 113);
        _errorLabel.TextAlign = ContentAlignment.MiddleLeft;
        _errorLabel.AutoEllipsis = true;
        root.Controls.Add(_errorLabel, 0, 2);

        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Margin = Padding.Empty,
            BackColor = Theme.Window
        };
        root.Controls.Add(footer, 0, 3);

        var addButton = CreateButton("添加", primary: true);
        addButton.Click += (_, _) => ValidateAndAccept();
        footer.Controls.Add(addButton);

        var cancelButton = CreateButton("取消", primary: false);
        cancelButton.DialogResult = DialogResult.Cancel;
        footer.Controls.Add(cancelButton);

        AcceptButton = addButton;
        CancelButton = cancelButton;
    }

    private static Button CreateButton(string text, bool primary)
    {
        var button = new Button
        {
            Text = text,
            Width = 76,
            Height = 30,
            Margin = new Padding(8, 4, 0, 0),
            FlatStyle = FlatStyle.Flat,
            BackColor = primary ? Theme.AccentDark : Theme.Panel,
            ForeColor = primary ? Color.White : Theme.Text,
            TabStop = false
        };
        button.FlatAppearance.BorderColor = primary ? Theme.Accent : Theme.BorderStrong;
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(27, 148, 88) : Theme.Card;
        button.FlatAppearance.MouseDownBackColor = primary ? Theme.AccentDark : Theme.CardAlt;
        return button;
    }

    private void BrowseFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "选择要收藏的文件夹",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false,
            SelectedPath = Directory.Exists(_pathBox.Text.Trim()) ? _pathBox.Text.Trim() : string.Empty
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _pathBox.Text = dialog.SelectedPath;
            _pathBox.SelectionStart = _pathBox.TextLength;
        }
    }

    private void ValidateAndAccept()
    {
        var input = _pathBox.Text.Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(input))
        {
            ShowError("请输入文件夹路径");
            return;
        }

        try
        {
            var fullPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(input));
            if (!Directory.Exists(fullPath))
            {
                ShowError("文件夹不存在，请检查路径");
                return;
            }

            SelectedPath = fullPath;
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException or System.Security.SecurityException)
        {
            ShowError("路径格式无效，请重新输入");
        }
    }

    private void ShowError(string message)
    {
        _errorLabel.Text = message;
        _pathBox.Focus();
        _pathBox.SelectAll();
    }

    private void ClearError()
    {
        if (!string.IsNullOrEmpty(_errorLabel.Text))
        {
            _errorLabel.Text = string.Empty;
        }
    }
}
