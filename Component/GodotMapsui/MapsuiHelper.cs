// Copyright (c) GodotNodeExtension contributors.
// Licensed under the MIT license.

using System;
using System.Linq;
using System.Net.Http;
using BruTile.Predefined;
using BruTile.Web;
using Mapsui;
using Mapsui.Projections;
using Mapsui.Tiling;
using Mapsui.Tiling.Layers;

namespace GodotNodeExtension.Component.GodotMapsui;

/// <summary>
/// Tile source types supported by <see cref="MapsuiControl"/>.
/// </summary>
public enum MapTileSourceType
{
    /// <summary>No tile layer added automatically.</summary>
    None = 0,
    /// <summary>OpenStreetMap standard tile layer.</summary>
    OpenStreetMap = 1,
    /// <summary>Esri World Topographic map.</summary>
    EsriWorldTopo = 2,
    /// <summary>Esri World Dark Gray base map.</summary>
    EsriWorldDarkGray = 3,
    /// <summary>Custom XYZ tile URL template.</summary>
    CustomXyz = 100,
}

/// <summary>
/// Utility class providing common Mapsui map configurations and coordinate conversion helpers.
/// </summary>
public static class MapsuiHelper
{
    /// <summary>
    /// Creates an OpenStreetMap tile layer with default settings.
    /// </summary>
    /// <returns>A configured <see cref="TileLayer"/> showing OpenStreetMap tiles.</returns>
    public static TileLayer CreateOpenStreetMapLayer()
    {
        return OpenStreetMap.CreateTileLayer("GodotMapsui/1.0");
    }

    /// <summary>
    /// Creates a tile layer based on the specified tile source configuration.
    /// </summary>
    /// <param name="sourceType">The type of tile source.</param>
    /// <param name="customUrl">Custom XYZ URL template (for CustomXyz type).
    /// Supports placeholders: {z} (zoom), {x} (column), {y} (row), {s} (subdomain).</param>
    /// <param name="subdomains">Comma-separated subdomain list (for CustomXyz type).</param>
    /// <param name="apiKey">API key for authentication (optional, maps to {k} placeholder).</param>
    /// <param name="minZoom">Minimum zoom level (0-20).</param>
    /// <param name="maxZoom">Maximum zoom level (0-20).</param>
    /// <param name="userAgent">Client identification sent with tile requests (OSM's policy asks for one).</param>
    /// <returns>A configured <see cref="TileLayer"/>, or null if sourceType is None.</returns>
    public static TileLayer? CreateTileLayer(
        MapTileSourceType sourceType,
        string customUrl = "",
        string subdomains = "",
        string apiKey = "",
        int minZoom = 0,
        int maxZoom = 20,
        string userAgent = "GodotNodeExtension GodotMapsui/1.0")
    {
        // The user agent is not a factory parameter in Mapsui/BruTile: it goes through the HTTP request
        // hook, which every source type accepts.
        var configureRequest = UserAgentHook(userAgent);

        var tileSource = sourceType switch
        {
            MapTileSourceType.None => null,
            MapTileSourceType.OpenStreetMap => KnownTileSources.Create(
                configureHttpRequestMessage: configureRequest,
                minZoomLevel: minZoom, maxZoomLevel: maxZoom),
            MapTileSourceType.EsriWorldTopo => KnownTileSources.Create(
                KnownTileSource.EsriWorldTopo, configureHttpRequestMessage: configureRequest,
                minZoomLevel: minZoom, maxZoomLevel: maxZoom),
            MapTileSourceType.EsriWorldDarkGray => KnownTileSources.Create(
                KnownTileSource.EsriWorldDarkGrayBase, configureHttpRequestMessage: configureRequest,
                minZoomLevel: minZoom, maxZoomLevel: maxZoom),
            MapTileSourceType.CustomXyz => CreateCustomXyzSource(
                customUrl, subdomains, apiKey, minZoom, maxZoom, configureRequest),
            _ => null,
        };

        if (tileSource == null) return null;
        return new TileLayer(tileSource);
    }

    /// <summary>
    /// HTTP request hook that stamps the configured user agent onto every tile request. Tile services
    /// (OpenStreetMap in particular) reject or throttle clients that do not identify themselves.
    /// </summary>
    private static Action<HttpRequestMessage>? UserAgentHook(string userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent)) return null;

        return message => message.Headers.TryAddWithoutValidation("User-Agent", userAgent);
    }

    private static HttpTileSource? CreateCustomXyzSource(
        string url, string subdomains, string apiKey, int minZoom, int maxZoom,
        Action<HttpRequestMessage>? configureRequest = null)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;

        var serverNodes = string.IsNullOrWhiteSpace(subdomains)
            ? null
            : subdomains.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();

        return new HttpTileSource(
            new GlobalSphericalMercator(Math.Max(0, minZoom), Math.Min(20, maxZoom)),
            url,
            serverNodes,
            apiKey: string.IsNullOrWhiteSpace(apiKey) ? null : apiKey,
            name: "CustomXyz",
            configureHttpRequestMessage: configureRequest);
    }

    /// <summary>
    /// Creates a default <see cref="Map"/> instance pre-configured with an OpenStreetMap layer.
    /// </summary>
    /// <returns>A new <see cref="Map"/> with OpenStreetMap tiles.</returns>
    public static Map CreateDefaultMap()
    {
        var map = new Map();
        map.Layers.Add(CreateOpenStreetMapLayer());
        return map;
    }

    /// <summary>
    /// Converts WGS84 latitude/longitude to Spherical Mercator (EPSG:3857) coordinates.
    /// </summary>
    /// <param name="latitude">Latitude in degrees (-90 to 90).</param>
    /// <param name="longitude">Longitude in degrees (-180 to 180).</param>
    /// <returns>A tuple of (X, Y) in Spherical Mercator meters.</returns>
    public static (double X, double Y) ToSphericalMercator(double latitude, double longitude)
    {
        var (x, y) = SphericalMercator.FromLonLat(longitude, latitude);
        return (x, y);
    }

    /// <summary>
    /// Converts Spherical Mercator (EPSG:3857) coordinates to WGS84 latitude/longitude.
    /// </summary>
    /// <param name="x">X coordinate in Spherical Mercator meters.</param>
    /// <param name="y">Y coordinate in Spherical Mercator meters.</param>
    /// <returns>A tuple of (Latitude, Longitude) in WGS84 degrees.</returns>
    public static (double Latitude, double Longitude) ToLatLon(double x, double y)
    {
        var (lon, lat) = SphericalMercator.ToLonLat(x, y);
        return (lat, lon);
    }

    /// <summary>
    /// Calculates the Mapsui resolution for a given zoom level.
    /// Zoom level 0 = whole world, zoom level 20 = building detail.
    /// </summary>
    /// <param name="zoomLevel">Zoom level (0-20).</param>
    /// <returns>The resolution in meters per pixel.</returns>
    public static double ZoomLevelToResolution(int zoomLevel)
    {
        // At zoom level 0, the entire world (circumference ~40075016m) fits in 256 pixels.
        // Each zoom level halves the resolution.
        const double worldCircumference = 40075016.685578488;
        const double tileSize = 256.0;
        return worldCircumference / (tileSize * Math.Pow(2, zoomLevel));
    }

    /// <summary>
    /// Calculates the (fractional) zoom level of a resolution - the inverse of
    /// <see cref="ZoomLevelToResolution"/>. Use it to report or clamp the current view.
    /// </summary>
    /// <param name="resolution">Resolution in meters per pixel.</param>
    /// <returns>The zoom level, clamped to 0..24 for finite input; 0 when the resolution is not usable.</returns>
    public static double ResolutionToZoomLevel(double resolution)
    {
        const double worldCircumference = 40075016.685578488;
        const double tileSize = 256.0;
        if (!double.IsFinite(resolution) || resolution <= 0) return 0;

        return Math.Clamp(Math.Log2(worldCircumference / (tileSize * resolution)), 0, 24);
    }

    /// <summary>
    /// Converts a WGS84 bounding box to a Spherical Mercator rectangle, tolerating swapped corners.
    /// </summary>
    /// <param name="latitude1">First corner latitude in degrees.</param>
    /// <param name="longitude1">First corner longitude in degrees.</param>
    /// <param name="latitude2">Opposite corner latitude in degrees.</param>
    /// <param name="longitude2">Opposite corner longitude in degrees.</param>
    /// <param name="paddingRatio">Fraction of the box size added on every side (0.05 = 5%).</param>
    /// <returns>The rectangle in Spherical Mercator meters.</returns>
    public static MRect LatLonToMercatorBox(
        double latitude1, double longitude1, double latitude2, double longitude2,
        double paddingRatio = 0.05)
    {
        var (x1, y1) = ToSphericalMercator(latitude1, longitude1);
        var (x2, y2) = ToSphericalMercator(latitude2, longitude2);

        double minX = Math.Min(x1, x2);
        double maxX = Math.Max(x1, x2);
        double minY = Math.Min(y1, y2);
        double maxY = Math.Max(y1, y2);

        double pad = Math.Max(0, paddingRatio) * Math.Max(maxX - minX, maxY - minY);
        return new MRect(minX - pad, minY - pad, maxX + pad, maxY + pad);
    }

    /// <summary>
    /// The attribution line a tile source requires. A host is responsible for showing it - see
    /// <c>MapsuiControl.ShowAttribution</c>, which draws it automatically.
    /// </summary>
    /// <param name="sourceType">The configured tile source.</param>
    /// <param name="customAttribution">Text to use for <see cref="MapTileSourceType.CustomXyz"/>.</param>
    /// <returns>The attribution text, or an empty string when the source needs none.</returns>
    public static string AttributionFor(MapTileSourceType sourceType, string? customAttribution = "")
        => sourceType switch
        {
            MapTileSourceType.OpenStreetMap => "© OpenStreetMap contributors",
            MapTileSourceType.EsriWorldTopo or MapTileSourceType.EsriWorldDarkGray => "Tiles © Esri",
            MapTileSourceType.CustomXyz => customAttribution ?? "",
            _ => "",
        };
}
