using System;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using SkiaSharp;
using GodotGuiExtension.GodotSkia;

namespace GodotNodeExtension.Example;

public partial class GodotSkiaDemo : Control
{
    // Export references to UI controls from tscn file
    [Export] public HSlider HueSlider { get; set; } = null!;
    [Export] public HSlider SatSlider { get; set; } = null!;
    [Export] public HSlider LightSlider { get; set; } = null!;
    [Export] public SpinBox LineWidthSpinBox { get; set; } = null!;
    [Export] public OptionButton DrawModeOption { get; set; } = null!;
    [Export] public CheckBox AnimationCheckBox { get; set; } = null!;
    [Export] public Button ClearButton { get; set; } = null!;
    [Export] public Button SaveButton { get; set; } = null!;
    [Export] public TextureRect TextureRect { get; set; } = null!;

    // Skia Canvas
    SkiaCanvasTexture2D _skiaCanvasTex = null!;

    // Animation variables
    private float _time = 0.0f;
    private bool _isAnimating = false;

    // Drawing state
    private SKColor _currentColor = SKColors.Red;
    private float _lineWidth = 2.0f;
    private int _drawMode = 0; // 0: Shapes, 1: Lines, 2: Text, 3: Animated

    // Paint objects
    private SKPaint _strokePaint = null!;
    private SKPaint _fillPaint = null!;
    private SKPaint _textPaint = null!;

    public async override void _Ready()
    {
        SetupSkiaCanvas();
        SetupPaints();
        SetupDrawModes();
        ConnectSignals();
        UpdateColor();
        await Task.Delay(new TimeSpan(0, 0, 0, 0, 10));
        RedrawCanvas();
        GD.Print("GodotSkia Demo Ready!");
    }

    private void SetupSkiaCanvas()
    {
        // Create Skia canvas texture
        _skiaCanvasTex = new SkiaCanvasTexture2D(512, 512);
        TextureRect.Texture = _skiaCanvasTex;
    }

    private void SetupPaints()
    {
        _strokePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            StrokeWidth = _lineWidth,
            IsAntialias = true
        };

        _fillPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            IsAntialias = true
        };

        _textPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            IsAntialias = true
        };
    }

    private void SetupDrawModes()
    {
        DrawModeOption.AddItem("Geometric Shapes");
        DrawModeOption.AddItem("Free Drawing");
        DrawModeOption.AddItem("Text Effects");
        DrawModeOption.AddItem("Animated Graphics");
    }

    private void ConnectSignals()
    {
        // Color sliders
        HueSlider.ValueChanged += OnColorChanged;
        SatSlider.ValueChanged += OnColorChanged;
        LightSlider.ValueChanged += OnColorChanged;

        // Other controls
        LineWidthSpinBox.ValueChanged += OnLineWidthChanged;
        DrawModeOption.ItemSelected += OnDrawModeChanged;
        AnimationCheckBox.Toggled += OnAnimationToggled;

        // Buttons
        ClearButton.Pressed += OnClearPressed;
        SaveButton.Pressed += OnSavePressed;

        // Mouse input for drawing
        TextureRect.GuiInput += OnTextureRectInput;
    }

    private void OnColorChanged(double value)
    {
        UpdateColor();
        RedrawCanvas();
    }

    private void UpdateColor()
    {
        var hue = (float)HueSlider.Value;
        var saturation = (float)SatSlider.Value / 100.0f;
        var lightness = (float)LightSlider.Value / 100.0f;

        _currentColor = SKColor.FromHsl(hue, saturation * 100, lightness * 100);

        _strokePaint.Color = _currentColor;
        _fillPaint.Color = _currentColor;
        _textPaint.Color = _currentColor;
    }

    private void OnLineWidthChanged(double value)
    {
        _lineWidth = (float)value;
        _strokePaint.StrokeWidth = _lineWidth;
        RedrawCanvas();
    }

    private void OnDrawModeChanged(long index)
    {
        _drawMode = (int)index;
        RedrawCanvas();
    }

    private void OnAnimationToggled(bool pressed)
    {
        _isAnimating = pressed;
        if (_isAnimating)
        {
            _time = 0.0f;
        }
    }

    private void OnClearPressed()
    {
        _skiaCanvasTex.Canvas?.Clear(SKColors.White);
        _skiaCanvasTex.UpdateTexture();
    }

    private void OnSavePressed()
    {
        var fileName = $"skia_canvas_{DateTime.Now:yyyyMMdd_HHmmss}.png";
        var saveDialog = new FileDialog();
        saveDialog.FileMode = FileDialog.FileModeEnum.SaveFile;
        saveDialog.CurrentFile = fileName;
        AddChild(saveDialog);
        saveDialog.PopupCentered();
        saveDialog.FileSelected += path =>
        {
            GD.Print($"Image saved to: {path}");
            _skiaCanvasTex.GetImage().SavePng(path);
        };
    }

    private void OnTextureRectInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mouseButton && mouseButton.Pressed)
        {
            if (_drawMode == 1) // Free drawing mode
            {
                var localPos = TextureRect.GetLocalMousePosition();
                DrawAtPosition(localPos);
            }
        }
        else if (@event is InputEventMouseMotion mouseMotion && Input.IsMouseButtonPressed(MouseButton.Left))
        {
            if (_drawMode == 1) // Free drawing mode
            {
                var localPos = TextureRect.GetLocalMousePosition();
                DrawAtPosition(localPos);
            }
        }
    }

    private void DrawAtPosition(Vector2 position)
    {
        var rect = TextureRect.GetRect();

        // Convert screen position to canvas coordinates
        var canvasX = position.X * (512.0f / rect.Size.X);
        var canvasY = position.Y * (512.0f / rect.Size.Y);

        // Draw a small circle at the position
        _skiaCanvasTex.Canvas?.DrawCircle(canvasX, canvasY, _lineWidth * 2, _fillPaint);
        // _skiaCanvasTex.UpdateTexture();
    }

    private void RedrawCanvas()
    {
        GD.Print(_skiaCanvasTex.Canvas);
        _skiaCanvasTex.Canvas?.Clear(SKColors.White);

        switch (_drawMode)
        {
            case 0: // Geometric Shapes
                DrawGeometricShapes(_skiaCanvasTex);
                break;
            case 1: // Free Drawing - don't clear existing content
                _skiaCanvasTex.Canvas?.Clear(SKColors.White);
                break;
            case 2: // Text Effects
                DrawTextEffects(_skiaCanvasTex);
                break;
            case 3: // Animated Graphics
                DrawAnimatedGraphics(_skiaCanvasTex);
                break;
        }
        _skiaCanvasTex.UpdateTexture();
    }

    private void DrawGeometricShapes(SkiaCanvasTexture2D canvas)
    {
        // Rectangle
        canvas.Canvas?.DrawRect(50, 50, 100, 80, _strokePaint);
        canvas.Canvas?.DrawRect(60, 60, 80, 60, _fillPaint);

        // Circle
        canvas.Canvas?.DrawCircle(250, 100, 50, _strokePaint);
        canvas.Canvas?.DrawCircle(250, 100, 30, _fillPaint);

        // Triangle
        var trianglePath = new SKPath();
        trianglePath.MoveTo(400, 150);
        trianglePath.LineTo(350, 50);
        trianglePath.LineTo(450, 50);
        trianglePath.Close();
        canvas.Canvas?.DrawPath(trianglePath, _strokePaint);

        // Rounded rectangle
        canvas.Canvas?.DrawRoundRect(new SKRoundRect(new SKRect(50, 200, 150, 100), 20, 20), _fillPaint);

        // Line
        canvas.Canvas?.DrawLine(250, 200, 400, 300, _strokePaint);

        // Oval
        canvas.Canvas?.DrawOval(new SKRect(300, 350, 450, 450), _strokePaint);
    }

    private void DrawTextEffects(SkiaCanvasTexture2D canvas)
    {
        var skFont = new SKFont(SKTypeface.Default, 24);
        // Simple text
        canvas.Canvas?.DrawText("Hello Skia!", 50, 100, SKTextAlign.Left, skFont, _textPaint);

        // Text with different sizes
        _textPaint.TextSize = 32;
        canvas.Canvas?.DrawText("Large Text", 50, 150, SKTextAlign.Left, skFont, _textPaint);

        _textPaint.TextSize = 16;
        canvas.Canvas?.DrawText("Small text here", 50, 180, SKTextAlign.Left, skFont, _textPaint);

        // Reset text size
        _textPaint.TextSize = 24;

        // Text on path
        var textPath = new SKPath();
        textPath.AddArc(new SKRect(200, 200, 400, 400), 0, 180);
        canvas.Canvas?.DrawTextOnPath("Text on curved path!", textPath, 0, -10, SKTextAlign.Left, skFont, _textPaint);

        // Outlined text
        var outlinePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2,
            Color = SKColors.Black,
            IsAntialias = true
        };

        canvas.Canvas?.DrawText("Outlined", 50, 350, SKTextAlign.Left, skFont, outlinePaint);

        var fillTextPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = _currentColor,
            IsAntialias = true
        };

        canvas.Canvas?.DrawText("Outlined", 50, 350, SKTextAlign.Left, skFont, fillTextPaint);
    }

    private void DrawAnimatedGraphics(SkiaCanvasTexture2D canvas)
    {
        var centerX = 256f;
        var centerY = 256f;
        var radius = 100f;

        // Rotating shapes
        for (int i = 0; i < 8; i++)
        {
            var angle = (_time + i * 45) * Math.PI / 180.0;
            var x = centerX + Math.Cos(angle) * radius;
            var y = centerY + Math.Sin(angle) * radius;

            canvas.Canvas?.DrawCircle((float)x, (float)y, 20, _fillPaint);
        }

        // Pulsating center circle
        var pulseRadius = 30 + Math.Sin(_time * 0.1) * 10;
        canvas.Canvas?.DrawCircle(centerX, centerY, (float)pulseRadius, _strokePaint);

        // Spiral
        var spiralPath = new SKPath();
        for (float t = 0; t < 360; t += 5)
        {
            var spiralAngle = (t + _time * 2) * Math.PI / 180.0;
            var spiralRadius = t * 0.3f;
            var spiralX = centerX + Math.Cos(spiralAngle) * spiralRadius;
            var spiralY = centerY + Math.Sin(spiralAngle) * spiralRadius;

            if (t == 0)
                spiralPath.MoveTo((float)spiralX, (float)spiralY);
            else
                spiralPath.LineTo((float)spiralX, (float)spiralY);
        }

        canvas.Canvas?.DrawPath(spiralPath, _strokePaint);
    }

    public override void _Process(double delta)
    {
        if (_isAnimating)
        {
            _time += (float)delta * 60.0f; // 60 degrees per second
            if (_drawMode == 3) // Only redraw if in animated mode
            {
                RedrawCanvas();
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _strokePaint?.Dispose();
            _fillPaint?.Dispose();
            _textPaint?.Dispose();
            _skiaCanvasTex?.Dispose();
        }
        base.Dispose(disposing);
    }
}
