using CommunityToolkit.Mvvm.ComponentModel;

namespace NebulaReader.ViewModels;

public partial class PageNavigationItem : ObservableObject
{
    public int PageNumber { get; init; }
    public ImageSource? ThumbnailSource { get; init; }

    [ObservableProperty]
    public partial bool IsCurrent { get; set; }

    [ObservableProperty]
    public partial bool IsBookmarked { get; set; }
}
