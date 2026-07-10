namespace DiskWriteWatcher;

public sealed class DrivePickerForm : Form
{
    private readonly CheckedListBox _driveList = new();

    public DrivePickerForm()
    {
        Text = "选择硬盘";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(360, 360);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 3,
            Padding = new Padding(12)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        Controls.Add(root);

        root.Controls.Add(new Label
        {
            Text = "选择要加入监视范围的硬盘：",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);

        _driveList.Dock = DockStyle.Fill;
        _driveList.CheckOnClick = true;
        root.Controls.Add(_driveList, 0, 1);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft
        };
        root.Controls.Add(buttons, 0, 2);

        var okButton = new Button
        {
            Text = "确定",
            DialogResult = DialogResult.OK,
            Width = 84
        };
        buttons.Controls.Add(okButton);

        var cancelButton = new Button
        {
            Text = "取消",
            DialogResult = DialogResult.Cancel,
            Width = 84
        };
        buttons.Controls.Add(cancelButton);

        AcceptButton = okButton;
        CancelButton = cancelButton;

        LoadDrives();
    }

    public IReadOnlyList<string> SelectedDrivePaths
    {
        get
        {
            var paths = new List<string>();
            foreach (var item in _driveList.CheckedItems)
            {
                if (item is DriveItem drive)
                {
                    paths.Add(drive.Path);
                }
            }

            return paths;
        }
    }

    private void LoadDrives()
    {
        foreach (var drive in DriveInfo.GetDrives().Where(drive => drive.IsReady))
        {
            if (drive.DriveType is DriveType.Fixed or DriveType.Removable)
            {
                _driveList.Items.Add(new DriveItem(drive.RootDirectory.FullName, drive.VolumeLabel, drive.DriveType));
            }
        }
    }

    private sealed record DriveItem(string Path, string VolumeLabel, DriveType DriveType)
    {
        public override string ToString()
        {
            var label = string.IsNullOrWhiteSpace(VolumeLabel) ? "无卷标" : VolumeLabel;
            return $"{Path}  {label}  ({DriveType})";
        }
    }
}
