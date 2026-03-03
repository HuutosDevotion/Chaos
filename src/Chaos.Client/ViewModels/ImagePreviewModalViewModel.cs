namespace Chaos.Client.ViewModels;

public class ImagePreviewModalViewModel : SubmenuViewModel
{
    public string ImageUrl { get; }
    public ImagePreviewModalViewModel(string imageUrl) => ImageUrl = imageUrl;
}
