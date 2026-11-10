using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Media.Imaging;

namespace HideAnyWindowManager.Util;

internal static class IconHelper
{
    // BitmapImage instances are UI-thread objects. Keep them alive so an
    // external config reload (which rebuilds the rule rows) does not re-run
    // shell icon extraction for every row.
    private static readonly Dictionary<string, BitmapImage> Cache =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Loads the associated icon for a file (typically an exe) and returns it
    /// as a WinUI 3 BitmapImage. Returns null on any failure.</summary>
    public static async Task<BitmapImage?> LoadIconAsync(string filePath)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            return null;

        if (Cache.TryGetValue(filePath, out var cached))
            return cached;

        byte[]? png;
        try
        {
            // GDI+ extraction and PNG encoding are synchronous and can take
            // tens of ms per exe. Keep them off the UI thread.
            png = await Task.Run(() =>
            {
                using var icon = System.Drawing.Icon.ExtractAssociatedIcon(filePath);
                if (icon == null) return null;
                using var bitmap = icon.ToBitmap();
                using var ms = new MemoryStream();
                bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                return ms.ToArray();
            });
        }
        catch
        {
            return null;
        }

        if (png == null || png.Length == 0)
            return null;

        try
        {
            using var ms = new MemoryStream(png);
            var bmp = new BitmapImage();
            await bmp.SetSourceAsync(ms.AsRandomAccessStream());
            Cache[filePath] = bmp;
            return bmp;
        }
        catch
        {
            return null;
        }
    }
}
