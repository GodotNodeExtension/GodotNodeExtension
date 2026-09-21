**English** | [中文](README.cn.md)

# GodotMapsui

An interactive map control for Godot, built on [Mapsui](https://github.com/Mapsui/Mapsui) and SkiaSharp and drawn on the GPU through [GodotSkia](../GodotSkia/README.md)'s Vulkan pipeline. Drop it in as an ordinary `Control` node to get pan, zoom, rotation, tile layers and coordinate conversion.

## Features

- Interactive tile maps: drag/inertia pan, wheel and pinch zoom, trackpad two-finger pan (rotation is available through the API)
- Real GPU rendering: the map is drawn into a Vulkan-backed Skia surface and shown as a texture
- Built-in sources (OpenStreetMap, Esri topo/dark gray) plus any custom XYZ template
- Attribution for the active tile source, exposed as text and as an optional themed label
- `PixelDensity` render scale for crisp labels on HiDPI displays or upscaled controls
- Camera and coordinate conversion keep working with no rendering device (headless CI)
- Layer management on top of Mapsui's, plus signals for taps, features and viewport changes

## Quick Start

### Scene Setup

1. Add a `MapsuiControl` node (it is listed under "Mapsui"; it is a `Control`, so place it in a container).
2. Give it a positive size — see [Size Requirement](#size-requirement).
3. Optionally set `InitialLatitude` / `InitialLongitude` / `InitialZoomLevel` in the Inspector.

### Code Setup

```csharp
var map = GetNode<MapsuiControl>("VBoxContainer/MapControl");

map.SetCenter(35.6762, 139.6503);           // jump to Tokyo, no animation
map.SetZoomLevel(12);                       // 0 = whole world, 20 = buildings
map.NavigateTo(48.8566, 2.3522, 12, 500);   // animated
map.FitToBounds(10, 20, 30, 40);            // frame a box, corners in any order
map.ApplyInitialView();                     // back to the exported initial view
```

### Connect Signals

```csharp
map.MapReady += () => GD.Print("map is ready");
map.ViewportChanged += () => GD.Print($"center {map.Center} z{map.ZoomLevel:F1}");
map.MapTapped += (lat, lon) => GD.Print($"tap {lat}, {lon}");
map.MapFeatureTapped += (layer, id, lat, lon) => GD.Print($"feature {layer}#{id}");
map.MapBackgroundTapped += (lat, lon) => GD.Print("background tapped");
```

## Size Requirement

The map only comes up on a **positive** control size: the camera needs a viewport before it can do anything, so initialization is deferred until `Size` has a width and a height greater than zero. The check runs every frame, so a size that arrives late is picked up automatically (no `await`).

This bites in containers. A `MapsuiControl` inside a `ScrollContainer` (or any container that gives it no height) reports a height of `0`, and the map stays blank. Give it a minimum size:

`custom_minimum_size = Vector2(0, 480)` (the demo scene uses exactly this)

`IsReady` stays `false` until the size is valid; once it flips, `MapReady` fires exactly once.

## API Reference

### Export Properties

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `InitialLatitude` | double | 0.0 | Latitude applied when the control first comes up (WGS84 degrees). |
| `InitialLongitude` | double | 0.0 | Longitude applied when the control first comes up (WGS84 degrees). |
| `InitialZoomLevel` | int | 2 | Initial zoom, 0 (world) to 20 (buildings). Inspector slider range 0-20. |
| `EnableInteraction` | bool | true | Enable pan/zoom input. When off the control ignores GUI input. |
| `EnableFling` | bool | true | Keep the map moving with inertia after a drag is released. |
| `PixelDensity` | float | 1.0 | Render scale of the map texture; clamped to 0.5-4.0. See [PixelDensity and Snapshots](#pixeldensity-and-snapshots). |
| `TileSource` | MapTileSourceType | OpenStreetMap | Base tile layer: `None`, `OpenStreetMap`, `EsriWorldTopo`, `EsriWorldDarkGray` or `CustomXyz`. |
| `CustomTileUrl` | string | "" | XYZ URL template for `TileSource = CustomXyz`. Placeholders: `{z}/{x}/{y}/{s}/{k}`. |
| `CustomTileSubdomains` | string | "" | Comma-separated subdomains substituted into `{s}` (for example `a,b,c`). |
| `TileApiKey` | string | "" | Key substituted into `{k}` for providers that require authentication. |
| `TileMinZoom` | int | 0 | Lowest zoom requested from the provider (0-20). |
| `TileMaxZoom` | int | 20 | Highest zoom requested from the provider (0-20). |
| `TileUserAgent` | string | "GodotNodeExtension GodotMapsui/1.0" | User agent sent with every tile request; keep it meaningful (see [Tiles and Compliance](#tiles-and-compliance)). |
| `ShowAttribution` | bool | false | Draw `AttributionText` in the bottom-left with Godot's font. Off by default - Mapsui already prints the credit into the texture. |
| `CustomAttribution` | string | "" | Attribution line used when `TileSource = CustomXyz`. |
| `DebugLogging` | bool | false | Print lifecycle/frame diagnostics to the Godot console. Real failures are always reported. |

### Read-only State

| Property | Type | Description |
|----------|------|-------------|
| `IsReady` | bool | True once the map exists **and** a valid viewport is set up; see [Size Requirement](#size-requirement). |
| `HasTexture` | bool | True when a Skia surface exists, i.e. the map can actually be drawn. Pair it with `IsReady` to detect a headless run. |
| `IsBusy` | bool | True while any layer is still fetching data (for example tiles) - drive a spinner with it. |
| `ZoomLevel` | double | Current fractional zoom level (0 = world, 20 = buildings). |
| `Center` | (double, double) | Current centre as `(Latitude, Longitude)` in WGS84 degrees. |
| `Rotation` | double | Current rotation in degrees clockwise from true north. |
| `TextureSize` | Vector2I | Rendered texture size in pixels = control size x `PixelDensity`; `(0, 0)` without a surface. |
| `AttributionText` | string | Attribution line required by the active tile source; empty when none is needed. |
| `Layers` | IReadOnlyList<ILayer> | Snapshot of the current layers, bottom-most first. |
| `MapInstance` | Map? | The underlying `Mapsui.Map` for advanced configuration. |

### Methods

| Method | Returns | Description |
|--------|---------|-------------|
| `AddLayer` | void | `AddLayer(ILayer layer)` - append a layer on top of the stack. |
| `InsertLayer` | void | `InsertLayer(int index, ILayer layer)` - insert at `index` (0 = bottom-most; the index is clamped to the layer count). |
| `RemoveLayer` | void | `RemoveLayer(ILayer layer)` - remove a layer. |
| `ClearLayers` | void | `ClearLayers()` - remove every layer, including the one created from `TileSource`. |
| `SetCenter` | void | `SetCenter(double latitude, double longitude, long duration = -1)` - move the centre; `duration` is a millisecond animation, `-1` jumps immediately. |
| `SetZoomLevel` | void | `SetZoomLevel(int level, long duration = -1)` - zoom to a whole level (clamped to 0-20). |
| `ZoomBy` | void | `ZoomBy(double levels, Vector2? anchor = null, long duration = -1)` - zoom by whole or fractional levels. `anchor` (control pixels) stays fixed, for example under the cursor; `null` zooms around the centre. |
| `SetRotation` | void | `SetRotation(double degrees, long duration = -1)` - rotate clockwise from north; `0` is north-up. |
| `NavigateTo` | void | `NavigateTo(double latitude, double longitude, int zoomLevel, long duration = 500)` - animated centre plus zoom in one call. |
| `FitToBounds` | void | `FitToBounds(lat1, lon1, lat2, lon2, double paddingRatio = 0.05, long duration = -1)` - fit a box; corners in any order; `paddingRatio` adds a margin. |
| `ApplyInitialView` | void | `ApplyInitialView(long duration = -1)` - re-apply the exported initial lat/lon/zoom (after changing them at runtime, or to reset). |
| `ScreenToLatLon` | (double, double) | `ScreenToLatLon(Vector2 position)` - control pixels (local coordinates) to WGS84 `(Latitude, Longitude)`. |
| `LatLonToScreen` | Vector2 | `LatLonToScreen(double latitude, double longitude)` - WGS84 to control pixels; the inverse of `ScreenToLatLon`. |
| `RefreshMap` | void | `RefreshMap()` - force a full data and render refresh. |
| `GetSnapshot` | Image? | `GetSnapshot()` - capture the current view as a Godot `Image` in texture pixels; `null` when no surface exists. |

### Signals

| Signal | Description |
|--------|-------------|
| `MapReady` | Emitted once when the map is initialized (first valid viewport). |
| `ViewportChanged` | Emitted whenever the viewport (centre, zoom or rotation) changes - including wheel zoom, animations/fling and code-driven navigation, not just drags. |
| `MapTapped` | `(latitude, longitude)` - a tap/click was registered. Always emitted. |
| `MapFeatureTapped` | `(layerName, featureId, latitude, longitude)` - the tap hit a feature; the feature also receives Mapsui's own tap event (callouts and so on). |
| `MapBackgroundTapped` | `(latitude, longitude)` - the tap missed every feature. |

## Input

- **Drag** with the left mouse button (or one finger) pans the map.
- **Mouse wheel** zooms in/out around the pointer; **`InputEventMagnifyGesture`** (touchpad pinch) zooms continuously.
- **Two-finger touch** is a combined pinch + pan: the distance between the fingers drives the scale while the midpoint movement pans.
- **Trackpad two-finger pan** (`InputEventPanGesture`) pans the map.
- **Fling**: releasing a drag keeps the map moving with inertia while `EnableFling` is on.
- **Tap vs. drag**: a press/release is reported as a tap only when the pointer moved **at most 4 px** over the whole gesture and no second finger was involved. A drag - however slow - is a pan, not a tap, and a pinch never reports one.

All of this requires `EnableInteraction` (default `true`); when it is off the control does not consume the events.

## Tiles and Compliance

`TileSource` selects the base layer:

- `None` - no tile layer; add your own with `AddLayer`.
- `OpenStreetMap` (default), `EsriWorldTopo`, `EsriWorldDarkGray` - built-in sources, fetched with `TileMinZoom`/`TileMaxZoom`.
- `CustomXyz` - any XYZ template. `CustomTileUrl` supports `{z}` (zoom), `{x}` (column), `{y}` (row), `{s}` (subdomain) and `{k}` (API key); `CustomTileSubdomains` fills `{s}` and `TileApiKey` fills `{k}`. A blank URL creates no layer.

```csharp
map.TileSource = MapTileSourceType.CustomXyz;
map.CustomTileUrl = "https://{s}.tiles.example.com/{z}/{x}/{y}.png?key={k}";
map.CustomTileSubdomains = "a,b,c";
map.TileApiKey = "YOUR_API_KEY";
map.CustomAttribution = "© Example Maps";
```

**User agent.** `TileUserAgent` is stamped on every tile request. OpenStreetMap's tile usage policy expects an identifiable client, so keep a meaningful value (the default already qualifies) when you ship a build.

**Attribution.** Most providers require it. The Mapsui renderer already prints the tile source's credit into the map texture (bottom-right corner), so you usually do not have to do anything. `AttributionText` exposes the same line if you want to show it elsewhere, and `ShowAttribution` adds a bottom-left label drawn with the control's theme font - turn it on when you replaced the tile layer yourself or want a label that follows the theme.

## PixelDensity and Snapshots

`PixelDensity` is the render scale of the map texture: at `2.0` the surface is twice the control size and is downscaled into the control, which keeps labels crisp on HiDPI displays and when the control is scaled up. It is clamped to **0.5 - 4.0**; out-of-range values snap to the nearest bound instead of allocating a huge surface.

- `TextureSize` reports the texture in pixels: control size x `PixelDensity` (`(0, 0)` with no surface).
- Pointer input is mapped into that texture-pixel space before Mapsui sees it, so hit-testing stays aligned at any density.
- `GetSnapshot()` likewise returns an `Image` in texture pixels - `PixelDensity` times the control size.
- `ScreenToLatLon` / `LatLonToScreen` take and return **control** pixels (the control's local coordinates) and apply the density for you.

```csharp
map.PixelDensity = 2f;                       // 160x120 control -> 320x240 texture
var snapshot = map.GetSnapshot();            // 320x240 Image
snapshot?.SavePng("user://map.png");
```

## Headless / No Rendering Device

Creating the Skia surface needs a rendering device. When there is none (headless CI, a machine without a GPU) the component keeps the camera alive: `IsReady` is `true`, `HasTexture` is `false`, and one WARNING is printed instead of failing every frame. Navigation, layers and coordinate conversion all keep working, only the pixels are missing.

```csharp
if (!map.HasTexture)
    GD.Print($"no surface, but the camera works: center {map.Center} z{map.ZoomLevel:F1}");
```

## Troubleshooting

Map stays blank? Check in this order:

1. **Size** - is the control actually bigger than 0x0? Inside a `ScrollContainer` give it `custom_minimum_size` (see [Size Requirement](#size-requirement)).
2. **Network / tile source** - a wrong `CustomTileUrl` or no connectivity leaves an empty map; the built-in sources need internet access.
3. **No rendering device** - `HasTexture` is `false` (see [Headless / No Rendering Device](#headless--no-rendering-device)).
4. **Still stuck?** - set `DebugLogging = true` and watch the `[MapsuiControl]` lines in the console; transient states stay quiet, real failures are always reported.

## MapsuiHelper

Static helpers you can call without a control instance.

- `MapTileSourceType` - `None`, `OpenStreetMap`, `EsriWorldTopo`, `EsriWorldDarkGray`, `CustomXyz`.
- `CreateTileLayer(sourceType, customUrl, subdomains, apiKey, minZoom, maxZoom, userAgent)` - build the tile layer for a configuration; returns `null` for `None` (and for a blank `CustomXyz` URL).
- `CreateOpenStreetMapLayer()` - an OSM tile layer.
- `CreateDefaultMap()` - a `Map` preloaded with an OSM layer.
- `ToSphericalMercator(latitude, longitude)` - WGS84 to EPSG:3857 `(X, Y)`.
- `ToLatLon(x, y)` - EPSG:3857 to `(Latitude, Longitude)`.
- `ZoomLevelToResolution(level)` / `ResolutionToZoomLevel(resolution)` - inverses of each other; the latter is clamped to 0-24 and returns `0` for a resolution that cannot be mapped.
- `LatLonToMercatorBox(lat1, lon1, lat2, lon2, paddingRatio = 0.05)` - WGS84 bounds to `MRect`; corners in any order, `paddingRatio` grows the box.
- `AttributionFor(sourceType, customAttribution = "")` - the credit line a source requires (empty when none).

```csharp
var (x, y) = MapsuiHelper.ToSphericalMercator(35.6762, 139.6503);
var (lat, lon) = MapsuiHelper.ToLatLon(x, y);
double resolution = MapsuiHelper.ZoomLevelToResolution(12);
string credit = MapsuiHelper.AttributionFor(MapTileSourceType.OpenStreetMap);
```

## Dependencies

- `Mapsui` >= 5.0.2
- `Mapsui.Rendering.Skia` >= 5.0.2
- `Mapsui.Tiling` >= 5.0.2
- `SkiaSharp` >= 3.116.0
- `GodotSkia` component (internal)
- Godot 4.0+ with .NET support, .NET 10.0+
