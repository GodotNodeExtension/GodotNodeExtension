using System.Collections.Generic;
using System.Linq;
using Godot;
using GodotNodeExtension.Component.GodotMapsui;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Projections;
using Mapsui.Styles;
using MColor = Mapsui.Styles.Color;

namespace GodotNodeExtension.Example.GodotMapsui;

/// <summary>
/// Demo scene demonstrating the MapsuiControl component.
/// Shows an interactive OpenStreetMap with navigation controls and feature interaction.
/// </summary>
public partial class MapsuiDemo : Control
{
    [Export] public MapsuiControl? MapControl { get; set; }
    [Export] public Label? CoordinateLabel { get; set; }
    [Export] public Button? ZoomInButton { get; set; }
    [Export] public Button? ZoomOutButton { get; set; }
    [Export] public Button? ResetButton { get; set; }

    // Some notable locations
    private static readonly (string Name, double Lat, double Lon, int Zoom)[] Locations =
    [
        ("World View", 0, 0, 2),
        ("Tokyo", 35.6762, 139.6503, 12),
        ("New York", 40.7128, -74.0060, 12),
        ("London", 51.5074, -0.1278, 12),
        ("Sydney", -33.8688, 151.2093, 12),
        ("Beijing", 39.9042, 116.4074, 12),
    ];

    private int _currentLocationIndex;

    public override void _Ready()
    {
        ConnectSignals();
        UpdateCoordinateLabel();
    }

    private void ConnectSignals()
    {
        if (ZoomInButton != null)
            ZoomInButton.Pressed += OnZoomInPressed;

        if (ZoomOutButton != null)
            ZoomOutButton.Pressed += OnZoomOutPressed;

        if (ResetButton != null)
            ResetButton.Pressed += OnResetPressed;

        if (MapControl != null)
        {
            MapControl.MapTapped += OnMapTapped;
            MapControl.MapFeatureTapped += OnMapFeatureTapped;
            MapControl.MapBackgroundTapped += OnMapBackgroundTapped;
            MapControl.ViewportChanged += OnViewportChanged;
            MapControl.MapReady += OnMapReady;
        }
    }

    private void OnMapReady()
    {
        GD.Print("[MapsuiDemo] Map is ready!");
        AddMarkerLayer();
    }

    private readonly List<PointFeature> _markerFeatures = [];

    /// <summary>
    /// Adds a MemoryLayer with marker points at notable locations.
    /// Each marker has a hidden CalloutStyle that is shown on tap.
    /// </summary>
    private void AddMarkerLayer()
    {
        if (MapControl?.MapInstance is null) return;

        for (var i = 1; i < Locations.Length; i++) // Skip "World View"
        {
            var loc = Locations[i];
            var (x, y) = SphericalMercator.FromLonLat(loc.Lon, loc.Lat);
            var feature = new PointFeature(x, y);
            feature["Name"] = loc.Name;
            feature.Styles.Add(new SymbolStyle
            {
                SymbolScale = 0.5,
                Fill = new Brush(MColor.FromArgb(255, 220, 50, 50)),
                Outline = new Pen(MColor.White, 2),
            });
            // Add a callout style (disabled by default), shown on tap
            feature.Styles.Add(new CalloutStyle
            {
                Enabled = false,
                Type = CalloutType.Detail,
                Title = loc.Name,
                Subtitle = $"({loc.Lat:F4}, {loc.Lon:F4})",
                TitleFontColor = MColor.Black,
                SubtitleFontColor = MColor.Gray,
                MaxWidth = 200,
                Spacing = 2,
                BalloonDefinition = new CalloutBalloonDefinition
                {
                    Color = MColor.Gray,
                    BackgroundColor = MColor.White,
                    StrokeWidth = 1,
                    RectRadius = 6,
                    ShadowWidth = 2,
                    TailAlignment = TailAlignment.Bottom,
                    TailWidth = 12,
                    TailHeight = 10,
                    Padding = new MRect(8, 4, 8, 4),
                },
            });
            _markerFeatures.Add(feature);
        }

        var markerLayer = new MemoryLayer("Markers")
        {
            Features = _markerFeatures,
        };

        MapControl.AddLayer(markerLayer);
        GD.Print($"[MapsuiDemo] Added {_markerFeatures.Count} markers");
    }

    private void OnZoomInPressed()
    {
        // The control's own API keeps the zoom level in the 0..20 range the component documents.
        MapControl?.ZoomBy(1, null, 250);
    }

    private void OnZoomOutPressed()
    {
        MapControl?.ZoomBy(-1, null, 250);
    }

    private void OnResetPressed()
    {
        // Cycle through notable locations
        _currentLocationIndex = (_currentLocationIndex + 1) % Locations.Length;
        var loc = Locations[_currentLocationIndex];
        MapControl?.NavigateTo(loc.Lat, loc.Lon, loc.Zoom);
        GD.Print($"[MapsuiDemo] Navigating to: {loc.Name}");
    }

    private void OnMapTapped(double latitude, double longitude)
    {
        GD.Print($"[MapsuiDemo] Map tapped at: ({latitude:F4}, {longitude:F4})");
    }

    private void OnMapFeatureTapped(string layerName, long featureId, double latitude, double longitude)
    {
        GD.Print($"[MapsuiDemo] Feature tapped: layer={layerName}, id={featureId}, ({latitude:F4}, {longitude:F4})");

        // Toggle callout: show on tapped feature, hide on all others
        foreach (var feature in _markerFeatures)
        {
            var callout = feature.Styles.OfType<CalloutStyle>().FirstOrDefault();
            callout?.Enabled = feature.Id == featureId;
        }

        MapControl?.RefreshMap();

        if (CoordinateLabel != null)
        {
            // One lookup, one pass: the marker is searched once and "Unknown" covers both the id that
            // matched no marker and a marker without a "Name" field.
            var name = _markerFeatures
                .FirstOrDefault(f => f.Id == featureId)?["Name"]?.ToString() ?? "Unknown";
            CoordinateLabel.Text = $"{name} ({latitude:F4}, {longitude:F4})";
        }
    }

    private void OnMapBackgroundTapped(double latitude, double longitude)
    {
        // Hide all callouts when tapping the background
        foreach (var feature in _markerFeatures)
        {
            var callout = feature.Styles.OfType<CalloutStyle>().FirstOrDefault();
            callout?.Enabled = false;
        }

        MapControl?.RefreshMap();
        UpdateCoordinateLabel(latitude, longitude);
    }

    private void OnViewportChanged()
    {
        UpdateCoordinateLabel();
    }

    private void UpdateCoordinateLabel(double? lat = null, double? lon = null)
    {
        if (CoordinateLabel == null || MapControl is not { IsReady: true }) return;

        if (lat.HasValue && lon.HasValue)
        {
            CoordinateLabel.Text = $"Lat: {lat.Value:F4}  Lon: {lon.Value:F4}";
        }
        else
        {
            // The control reports the view and the tile traffic itself, so the demo never reaches into
            // the Mapsui internals (and it keeps working when those change).
            var (centerLat, centerLon) = MapControl.Center;
            string busy = MapControl.IsBusy ? "   loading tiles..." : "";
            CoordinateLabel.Text =
                $"Center: ({centerLat:F4}, {centerLon:F4})   Z{MapControl.ZoomLevel:F1}{busy}";
        }
    }
}
