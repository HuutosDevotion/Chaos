using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Chaos.Client.ViewModels;

public class EmojiUploaderViewModel : SubmenuViewModel
{
    public const int ExportSize = 128;
    public const int CropSize = 256;

    private readonly byte[] _sourceBytes;
    private readonly bool _isGif;
    private readonly Func<byte[], string, string, Task<string?>> _uploadCallback;

    private BitmapSource? _sourceImage;
    private double _offsetX;
    private double _offsetY;
    private double _zoom = 1.0;
    private double _canvasWidth = 500;
    private double _canvasHeight = 500;
    private string _emojiName = string.Empty;
    private BitmapSource? _previewImage;
    private bool _isUploading;
    private string _errorMessage = string.Empty;

    /// <summary>
    /// Minimum zoom: the larger DIP dimension fills the crop box.
    /// Wide images zoom out until full width fits; tall images until full height fits.
    /// Uses Width/Height (DIPs) not PixelWidth/PixelHeight, because WPF Stretch="None"
    /// renders at DIP dimensions which differ from pixels when image DPI != 96.
    /// </summary>
    private double MinZoom => _sourceImage is not null
        ? CropSize / Math.Max(_sourceImage.Width, _sourceImage.Height)
        : 0.1;

    public double CanvasWidth
    {
        get => _canvasWidth;
        set
        {
            _canvasWidth = value;
            _offsetX = ClampX(_offsetX);
            OnPropertyChanged(nameof(OffsetX));
            UpdatePreview();
        }
    }

    public double CanvasHeight
    {
        get => _canvasHeight;
        set
        {
            _canvasHeight = value;
            _offsetY = ClampY(_offsetY);
            OnPropertyChanged(nameof(OffsetY));
            UpdatePreview();
        }
    }

    public BitmapSource? SourceImage
    {
        get => _sourceImage;
        private set { _sourceImage = value; OnPropertyChanged(); }
    }

    public double OffsetX
    {
        get => _offsetX;
        set { _offsetX = ClampX(value); OnPropertyChanged(); UpdatePreview(); }
    }

    public double OffsetY
    {
        get => _offsetY;
        set { _offsetY = ClampY(value); OnPropertyChanged(); UpdatePreview(); }
    }

    public double Zoom
    {
        get => _zoom;
        set
        {
            _zoom = Math.Clamp(value, MinZoom, 10.0);
            _offsetX = ClampX(_offsetX);
            _offsetY = ClampY(_offsetY);
            OnPropertyChanged();
            OnPropertyChanged(nameof(OffsetX));
            OnPropertyChanged(nameof(OffsetY));
            UpdatePreview();
        }
    }

    /// <summary>
    /// Atomically updates zoom + offsets so the zoom pivots around the crop center
    /// without triggering three separate preview rebuilds.
    /// </summary>
    public void SetZoomCentered(double newZoom, double centerX, double centerY)
    {
        double oldZoom = _zoom;
        newZoom = Math.Clamp(newZoom, MinZoom, 10.0);
        _offsetX = centerX - (centerX - _offsetX) * (newZoom / oldZoom);
        _offsetY = centerY - (centerY - _offsetY) * (newZoom / oldZoom);
        _zoom = newZoom;
        _offsetX = ClampX(_offsetX);
        _offsetY = ClampY(_offsetY);
        OnPropertyChanged(nameof(OffsetX));
        OnPropertyChanged(nameof(OffsetY));
        OnPropertyChanged(nameof(Zoom));
        UpdatePreview();
    }

    /// <summary>
    /// Clamp X offset to keep the image inside the crop box.
    /// When image is smaller than crop box: image stays within the box.
    /// When image is larger: image must cover the full box width.
    /// </summary>
    private double ClampX(double x)
    {
        if (_sourceImage is null) return x;
        double cropLeft = (_canvasWidth - CropSize) / 2.0;
        double imgW = _sourceImage.Width * _zoom;
        double a = cropLeft;                    // image left = crop left
        double b = cropLeft + CropSize - imgW;  // image right = crop right
        return Math.Clamp(x, Math.Min(a, b), Math.Max(a, b));
    }

    /// <summary>
    /// Clamp Y offset to keep the image inside the crop box.
    /// </summary>
    private double ClampY(double y)
    {
        if (_sourceImage is null) return y;
        double cropTop = (_canvasHeight - CropSize) / 2.0;
        double imgH = _sourceImage.Height * _zoom;
        double a = cropTop;                     // image top = crop top
        double b = cropTop + CropSize - imgH;   // image bottom = crop bottom
        return Math.Clamp(y, Math.Min(a, b), Math.Max(a, b));
    }

    public string EmojiName
    {
        get => _emojiName;
        set { _emojiName = value; OnPropertyChanged(); }
    }

    public BitmapSource? PreviewImage
    {
        get => _previewImage;
        private set { _previewImage = value; OnPropertyChanged(); }
    }

    public bool IsGif => _isGif;
    public byte[] SourceBytes => _sourceBytes;

    public bool IsUploading
    {
        get => _isUploading;
        private set { _isUploading = value; OnPropertyChanged(); }
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set { _errorMessage = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasError)); }
    }

    public bool HasError => !string.IsNullOrEmpty(_errorMessage);

    public ICommand Confirm { get; }
    public ICommand Cancel { get; }

    public EmojiUploaderViewModel(byte[] imageData, string filename, Func<byte[], string, string, Task<string?>> uploadCallback)
    {
        _sourceBytes = imageData;
        _uploadCallback = uploadCallback;

        var ext = Path.GetExtension(filename).ToLower();
        _isGif = ext == ".gif";

        // Default name from filename
        var baseName = Path.GetFileNameWithoutExtension(filename).ToLower();
        baseName = System.Text.RegularExpressions.Regex.Replace(baseName, @"[^a-z0-9_]", "_");
        if (baseName.Length > 32) baseName = baseName[..32];
        EmojiName = baseName;

        LoadSourceImage();

        if (_sourceImage is not null)
        {
            // Initial zoom: smaller dimension fills the crop box exactly
            _zoom = MinZoom;
            _zoom = Math.Clamp(_zoom, MinZoom, 10.0);

            // Center the image in the canvas
            _offsetX = (_canvasWidth - _sourceImage.Width * _zoom) / 2;
            _offsetY = (_canvasHeight - _sourceImage.Height * _zoom) / 2;
        }

        UpdatePreview();

        Confirm = new RelayCommand(async _ => await UploadAsync(), _ => !IsUploading && !string.IsNullOrWhiteSpace(EmojiName));
        Cancel = new RelayCommand(_ => Close());
    }

    private void LoadSourceImage()
    {
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.StreamSource = new MemoryStream(_sourceBytes);
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
            SourceImage = bmp;
        }
        catch
        {
            SourceImage = null;
        }
    }

    /// <summary>
    /// Renders the crop box contents onto a transparent ExportSize x ExportSize canvas.
    /// The image is drawn at its correct position — parts outside the canvas are clipped,
    /// parts of the canvas without image coverage stay transparent.
    /// </summary>
    private void UpdatePreview()
    {
        if (_sourceImage is null) return;

        try
        {
            double cropLeft = (_canvasWidth - CropSize) / 2.0;
            double cropTop = (_canvasHeight - CropSize) / 2.0;

            // Image position within the ExportSize output space
            double outputScale = (double)ExportSize / CropSize;
            double imgX = (_offsetX - cropLeft) * outputScale;
            double imgY = (_offsetY - cropTop) * outputScale;
            double imgW = _sourceImage.Width * _zoom * outputScale;
            double imgH = _sourceImage.Height * _zoom * outputScale;

            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawImage(_sourceImage, new Rect(imgX, imgY, imgW, imgH));
            }

            var rtb = new RenderTargetBitmap(ExportSize, ExportSize, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            rtb.Freeze();
            PreviewImage = rtb;
        }
        catch
        {
            // Preview update failed silently
        }

    }

    private async Task UploadAsync()
    {
        IsUploading = true;
        ErrorMessage = string.Empty;

        try
        {
            byte[] croppedBytes = CropWithImageSharp();

            if (croppedBytes.Length > 256 * 1024)
            {
                ErrorMessage = $"File size ({croppedBytes.Length / 1024}KB) exceeds 256KB limit. Try zooming in more.";
                return;
            }

            string ext = _isGif ? ".gif" : ".png";
            var error = await _uploadCallback(croppedBytes, ext, EmojiName.Trim());
            if (error is not null)
            {
                ErrorMessage = error;
                return;
            }

            Close();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Upload failed: {ex.Message}";
        }
        finally
        {
            IsUploading = false;
        }
    }

    /// <summary>
    /// Crops and resizes using ImageSharp. Works on all frames for animated GIFs.
    /// Strategy: resize source to output scale → pad with transparent pixels so the
    /// crop box region is guaranteed in-bounds → crop to ExportSize x ExportSize.
    /// </summary>
    private byte[] CropWithImageSharp()
    {
        double cropLeft = (_canvasWidth - CropSize) / 2.0;
        double cropTop = (_canvasHeight - CropSize) / 2.0;

        using var image = SixLabors.ImageSharp.Image.Load<Rgba32>(_sourceBytes);

        // Step 1: resize source so that display DIPs map 1:1 to export pixels
        // Use WPF DIP dimensions (not pixel dimensions) since zoom/offsets are in DIP space
        double dipW = _sourceImage!.Width;
        double dipH = _sourceImage!.Height;
        double outputScale = _zoom * ExportSize / CropSize;
        int newW = Math.Max(1, (int)Math.Round(dipW * outputScale));
        int newH = Math.Max(1, (int)Math.Round(dipH * outputScale));
        image.Mutate(ctx => ctx.Resize(newW, newH));

        // Step 2: where the crop box starts relative to the resized image
        int cropX = (int)Math.Round((cropLeft - _offsetX) * ExportSize / CropSize);
        int cropY = (int)Math.Round((cropTop - _offsetY) * ExportSize / CropSize);

        // Step 3: pad with transparent pixels so the crop region is always in-bounds
        // Pad centers the content, adding (pad, pad) offset to content origin
        int pad = ExportSize;
        int paddedW = newW + 2 * pad;
        int paddedH = newH + 2 * pad;
        image.Mutate(ctx => ctx.Pad(paddedW, paddedH, SixLabors.ImageSharp.Color.Transparent));

        // Step 4: crop to final output — adjust for the padding offset
        int adjCropX = Math.Clamp(cropX + pad, 0, paddedW - ExportSize);
        int adjCropY = Math.Clamp(cropY + pad, 0, paddedH - ExportSize);
        image.Mutate(ctx => ctx.Crop(new SixLabors.ImageSharp.Rectangle(adjCropX, adjCropY, ExportSize, ExportSize)));

        using var ms = new MemoryStream();
        if (_isGif)
            image.Save(ms, new GifEncoder());
        else
            image.Save(ms, new PngEncoder());

        return ms.ToArray();
    }
}
