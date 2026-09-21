// Copyright (c) GodotNodeExtension contributors.
// Licensed under the MIT license.

using Godot;
using Mapsui.Extensions;
using SkiaSharp;

namespace GodotNodeExtension.Component.GodotMapsui;

/// <summary>
/// Partial class handling the Mapsui rendering pipeline.
/// Renders the Mapsui map to the SkiaCanvasTexture2D via SKCanvas.
/// </summary>
public partial class MapsuiControl
{
    private int _renderCount;

    /// <summary>
    /// Renders the current map state to the SkiaCanvasTexture2D.
    /// </summary>
    private void RenderMap()
    {
        if (_skiaTexture == null || _map == null || _mapRenderer == null)
        {
            LogDebug($"Render skipped: texture={_skiaTexture != null}, map={_map != null}, renderer={_mapRenderer != null}");
            return;
        }

        var canvas = _skiaTexture.Canvas;
        if (canvas == null)
        {
            LogDebug("Render skipped: the Skia canvas is not available yet");
            return;
        }

        var viewport = _map.Navigator.Viewport;
        if (!viewport.HasSize())
        {
            LogDebug($"Render skipped: viewport has no size ({viewport.Width}x{viewport.Height})");
            return;
        }

        try
        {
            // Clear the canvas with the map background color. Mapsui's Color type is a third-party
            // struct, so this stays a direct construction (the converter only handles Godot colors).
            var bgColor = new SKColor((byte)_map.BackColor.R, (byte)_map.BackColor.G, (byte)_map.BackColor.B, (byte)_map.BackColor.A);
            canvas.Clear(bgColor);

            // Render the map layers and widgets
            _mapRenderer.Render(
                canvas,
                viewport,
                _map.Layers,
                _map.GetWidgetsOfMapAndLayers(),
                _map.RenderService,
                _map.BackColor);

            // Flush the GPU surface and update Godot texture
            _skiaTexture.UpdateTexture();

            // Trigger Godot redraw
            QueueRedraw();

            _renderCount++;
            _renderErrorReported = false;
            if (DebugLogging && (_renderCount <= 3 || _renderCount % 60 == 0))
            {
                GD.Print($"[MapsuiControl] Rendered frame #{_renderCount}, layers={_map.Layers.Count}, viewport=({viewport.CenterX:F0},{viewport.CenterY:F0}) res={viewport.Resolution:F1}");
            }
        }
        catch (System.Exception ex)
        {
            // Report the failure once instead of flooding every frame; a successful frame re-arms it.
            if (!_renderErrorReported)
            {
                _renderErrorReported = true;
                GD.PushError($"[MapsuiControl] Render failed: {ex.Message}");
            }
            LogDebug(ex.StackTrace ?? "");
        }
    }
}
