using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using DiskCleaner.Core;

namespace DiskCleaner.ViewModels;

public sealed class DriveListViewModel : ObservableObject
{
    private bool _hasNoDrives;

    public DriveListViewModel(Action<DriveSummary> openDrive)
    {
        OpenCommand = new RelayCommand(p =>
        {
            if (p is DriveItemViewModel item)
            {
                openDrive(item.Drive);
            }
        });
        RefreshCommand = new RelayCommand(_ => Refresh());
        Refresh();
    }

    public ObservableCollection<DriveItemViewModel> Drives { get; } = [];

    public bool HasNoDrives
    {
        get => _hasNoDrives;
        private set => SetProperty(ref _hasNoDrives, value);
    }

    public ICommand OpenCommand { get; }

    public ICommand RefreshCommand { get; }

    private void Refresh()
    {
        Drives.Clear();
        foreach (var drive in DriveService.GetDrives())
        {
            Drives.Add(new DriveItemViewModel(drive));
        }

        HasNoDrives = Drives.Count == 0;
    }
}

public sealed class DriveItemViewModel(DriveSummary drive)
{
    public DriveSummary Drive => drive;

    public string Title => drive.DisplayName;

    public string? KindTag => drive.Kind switch
    {
        DriveKind.ExternalDisk => "移动硬盘",
        DriveKind.RemovableMedia => "U 盘 / 存储卡",
        _ => null,
    };

    public bool HasKindTag => KindTag is not null;

    public bool HasNoRecycleBin => !drive.HasRecycleBin;

    public string UsageText =>
        $"已用 {SizeFormatter.Format(drive.UsedBytes)}，共 {SizeFormatter.Format(drive.TotalBytes)}";

    public string FreeText => $"剩余 {SizeFormatter.Format(drive.FreeBytes)}";

    public GridLength UsedLength => new(UsedFraction, GridUnitType.Star);

    public GridLength FreeLength => new(1 - UsedFraction, GridUnitType.Star);

    private double UsedFraction => drive.TotalBytes > 0 ? (double)drive.UsedBytes / drive.TotalBytes : 0;

    public string ExactText =>
        $"已用 {SizeFormatter.FormatExact(drive.UsedBytes)}\n共 {SizeFormatter.FormatExact(drive.TotalBytes)}";
}
