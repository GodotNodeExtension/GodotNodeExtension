using System;
using Godot;
using SkiaSharp;

namespace GodotGuiExtension.GodotSkia;

/// <summary>
/// Provides conversion utilities between Skia types and Godot types
/// </summary>
public static class SkiaGodotConverter
{
    #region Color Conversion

    /// <summary>
    /// Convert Godot Color to Skia SKColor
    /// </summary>
    public static SKColor ToSKColor(this Color godotColor)
    {
        return new SKColor(
            (byte)(godotColor.R * 255),
            (byte)(godotColor.G * 255),
            (byte)(godotColor.B * 255),
            (byte)(godotColor.A * 255)
        );
    }

    /// <summary>
    /// Convert Skia SKColor to Godot Color
    /// </summary>
    public static Color ToGodotColor(this SKColor skColor)
    {
        return new Color(
            skColor.Red / 255f,
            skColor.Green / 255f,
            skColor.Blue / 255f,
            skColor.Alpha / 255f
        );
    }

    #endregion

    #region Vector Conversion

    /// <summary>
    /// Convert Godot Vector2 to Skia SKPoint
    /// </summary>
    public static SKPoint ToSKPoint(this Vector2 vector)
    {
        return new SKPoint(vector.X, vector.Y);
    }

    /// <summary>
    /// Convert Skia SKPoint to Godot Vector2
    /// </summary>
    public static Vector2 ToVector2(this SKPoint point)
    {
        return new Vector2(point.X, point.Y);
    }

    /// <summary>
    /// Convert Godot Vector2 to Skia SKSize
    /// </summary>
    public static SKSize ToSKSize(this Vector2 vector)
    {
        return new SKSize(vector.X, vector.Y);
    }

    /// <summary>
    /// Convert Skia SKSize to Godot Vector2
    /// </summary>
    public static Vector2 ToVector2(this SKSize size)
    {
        return new Vector2(size.Width, size.Height);
    }

    #endregion

    #region Rect Conversion

    /// <summary>
    /// Convert Godot Rect2 to Skia SKRect
    /// </summary>
    public static SKRect ToSKRect(this Rect2 rect)
    {
        return new SKRect(
            rect.Position.X,
            rect.Position.Y,
            rect.Position.X + rect.Size.X,
            rect.Position.Y + rect.Size.Y
        );
    }

    /// <summary>
    /// Convert Skia SKRect to Godot Rect2
    /// </summary>
    public static Rect2 ToRect2(this SKRect rect)
    {
        return new Rect2(
            rect.Left,
            rect.Top,
            rect.Width,
            rect.Height
        );
    }

    #endregion

    #region Font Conversion

    /// <summary>
    /// Convert Godot Font to Skia SKFont
    /// </summary>
    public static SKFont ToSKFont(this Font godotFont, float size = 16f)
    {
        if (godotFont is FontFile fontFile)
        {
            var fontData = fontFile.Data;
            if (fontData is { Length: > 0 })
            {
                var skTypeface = SKTypeface.FromData(SKData.CreateCopy(fontData));
                return new SKFont(skTypeface, size);
            }
        }
        else if (godotFont is SystemFont systemFont)
        {
            foreach (var systemFontFontName in systemFont.FontNames)
            {
                
            }
        }
        
        return new SKFont(SKTypeface.Default, size);
    }

    /// <summary>
    /// Create SKPaint with specified parameters for text rendering
    /// </summary>
    public static SKPaint CreateTextPaint(Font godotFont, float fontSize, Color color, bool antiAlias = true)
    {
        var paint = new SKPaint
        {
            IsAntialias = antiAlias,
            Color = color.ToSKColor(),
            TextSize = fontSize,
            TextAlign = SKTextAlign.Left
        };
        
        if (godotFont is FontFile fontFile)
        {
            var fontData = fontFile.Data;
            if (fontData != null && fontData.Length > 0)
            {
                var skTypeface = SKTypeface.FromData(SKData.CreateCopy(fontData));
                paint.Typeface = skTypeface;
            }
        }

        return paint;
    }

    #endregion

    #region Paint Conversion

    /// <summary>
    /// Create basic SKPaint
    /// </summary>
    public static SKPaint CreatePaint(Color color, bool antiAlias = true, SKPaintStyle style = SKPaintStyle.Fill)
    {
        return new SKPaint
        {
            Color = color.ToSKColor(),
            IsAntialias = antiAlias,
            Style = style
        };
    }

    /// <summary>
    /// Create SKPaint with stroke
    /// </summary>
    public static SKPaint CreateStrokePaint(Color color, float strokeWidth, bool antiAlias = true)
    {
        return new SKPaint
        {
            Color = color.ToSKColor(),
            IsAntialias = antiAlias,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = strokeWidth
        };
    }

    #endregion

    #region Transform Conversion

    /// <summary>
    /// Convert Godot Transform2D to Skia SKMatrix
    /// </summary>
    public static SKMatrix ToSKMatrix(this Transform2D transform)
    {
        return new SKMatrix(
            transform.X.X, transform.Y.X, transform.Origin.X,
            transform.X.Y, transform.Y.Y, transform.Origin.Y,
            0, 0, 1
        );
    }

    /// <summary>
    /// Convert Skia SKMatrix to Godot Transform2D
    /// </summary>
    public static Transform2D ToTransform2D(this SKMatrix matrix)
    {
        return new Transform2D(
            new Vector2(matrix.ScaleX, matrix.SkewY),
            new Vector2(matrix.SkewX, matrix.ScaleY),
            new Vector2(matrix.TransX, matrix.TransY)
        );
    }

    #endregion

    #region Image Conversion

    /// <summary>
    /// Convert Godot Image to Skia SKBitmap
    /// </summary>
    public static SKBitmap ToSKBitmap(this Image image)
    {
        if (image.GetFormat() != Image.Format.Rgba8)
        {
            image.Convert(Image.Format.Rgba8);
        }

        var bitmap = new SKBitmap(image.GetWidth(), image.GetHeight(), SKColorType.Rgba8888, SKAlphaType.Unpremul);
        var pixelData = image.GetData();
        
        unsafe
        {
            fixed (byte* ptr = pixelData)
            {
                bitmap.SetPixels((IntPtr)ptr);
            }
        }

        return bitmap;
    }

    /// <summary>
    /// Convert Skia SKBitmap to Godot Image
    /// </summary>
    public static Image ToGodotImage(this SKBitmap bitmap)
    {
        IntPtr pixels = bitmap.GetPixels();
        int dataSize = bitmap.ByteCount;
        var pixelData = new byte[dataSize];
        System.Runtime.InteropServices.Marshal.Copy(pixels, pixelData, 0, dataSize);
        return Image.CreateFromData(bitmap.Width, bitmap.Height, false, Image.Format.Rgba8, pixelData);
    }

    /// <summary>
    /// Convert Godot Texture2D to Skia SKImage
    /// </summary>
    public static SKImage ToSKImage(this Texture2D texture)
    {
        var image = texture.GetImage();
        var bitmap = image.ToSKBitmap();
        return SKImage.FromBitmap(bitmap);
    }

    public static Image ToGodotImage(this SKImage skImage)
    {
        var skBitmap = SKBitmap.FromImage(skImage);
        return skBitmap.ToGodotImage();
    }

    #endregion

    #region Predefined Common Colors

    public static class Colors
    {
        public static readonly SKColor White = Godot.Colors.White.ToSKColor();
        public static readonly SKColor Black = Godot.Colors.Black.ToSKColor();
        public static readonly SKColor Red = Godot.Colors.Red.ToSKColor();
        public static readonly SKColor Green = Godot.Colors.Green.ToSKColor();
        public static readonly SKColor Blue = Godot.Colors.Blue.ToSKColor();
        public static readonly SKColor Yellow = Godot.Colors.Yellow.ToSKColor();
        public static readonly SKColor Transparent = Godot.Colors.Transparent.ToSKColor();
    }

    #endregion
}
