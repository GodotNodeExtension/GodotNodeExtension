[English](README.md) | **中文**

# GodotMapsui

基于 [Mapsui](https://github.com/Mapsui/Mapsui) 与 SkiaSharp 的 Godot 交互式地图控件，通过 [GodotSkia](../GodotSkia/README.cn.md) 的 Vulkan 管线在 GPU 上绘制。把它当作普通 `Control` 节点放进场景，即可获得平移、缩放、旋转、瓦片图层与坐标换算能力。

## 功能特性

- 交互式瓦片地图：拖动（含惯性）平移、滚轮与捏合缩放、触控板双指平移（旋转可通过 API 调用）
- 真实 GPU 渲染：地图绘制到 Vulkan 支持的 Skia 表面，再作为纹理显示
- 内置数据源（OpenStreetMap、Esri 地形/暗灰）以及任意自定义 XYZ 模板
- 当前瓦片源的归属信息，既可作为文本读取，也可选择绘制主题化标签
- `PixelDensity` 渲染倍率，在 HiDPI 屏幕或控件被放大时保持文字清晰
- 无渲染设备时（无头 CI）相机与坐标换算依然可用
- 在 Mapsui 之上提供图层管理，并提供点击、要素与视口变化信号

## 快速开始

### 场景方式

1. 添加一个 `MapsuiControl` 节点（在 "Mapsui" 分类下；它是 `Control`，请放进容器）。
2. 给它一个正的尺寸 —— 见 [尺寸要求](#尺寸要求)。
3. 可选：在检查器中设置 `InitialLatitude` / `InitialLongitude` / `InitialZoomLevel`。

### 代码方式

```csharp
var map = GetNode<MapsuiControl>("VBoxContainer/MapControl");

map.SetCenter(35.6762, 139.6503);           // 跳到东京，无动画
map.SetZoomLevel(12);                       // 0 = 全球，20 = 建筑级
map.NavigateTo(48.8566, 2.3522, 12, 500);   // 带动画
map.FitToBounds(10, 20, 30, 40);            // 框住一个范围，两个角点顺序任意
map.ApplyInitialView();                     // 回到导出属性里的初始视图
```

### 连接信号

```csharp
map.MapReady += () => GD.Print("地图已就绪");
map.ViewportChanged += () => GD.Print($"中心 {map.Center} z{map.ZoomLevel:F1}");
map.MapTapped += (lat, lon) => GD.Print($"点击 {lat}, {lon}");
map.MapFeatureTapped += (layer, id, lat, lon) => GD.Print($"要素 {layer}#{id}");
map.MapBackgroundTapped += (lat, lon) => GD.Print("点击了背景");
```

## 尺寸要求

地图只在控件尺寸为**正**时才会起来：相机需要先拿到视口才能工作，因此初始化会被推迟到 `Size` 的宽和高都大于 0 之后。该检查每帧执行，迟到的尺寸会被自动补上（无需 `await`）。

这在容器里最容易踩坑。放进 `ScrollContainer`（或任何不给它高度的容器）时高度会是 `0`，地图就会空白。给它一个最小尺寸：

`custom_minimum_size = Vector2(0, 480)`（示例场景用的就是这个值）

在尺寸合法之前 `IsReady` 一直是 `false`；一旦变为 `true`，`MapReady` 只触发一次。

## API 参考

### 导出属性

| 属性 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `InitialLatitude` | double | 0.0 | 控件首次起来时应用的纬度（WGS84 度）。 |
| `InitialLongitude` | double | 0.0 | 控件首次起来时应用的经度（WGS84 度）。 |
| `InitialZoomLevel` | int | 2 | 初始缩放，0（全球）到 20（建筑级）。检查器滑块范围 0-20。 |
| `EnableInteraction` | bool | true | 是否启用平移/缩放输入。关闭后控件忽略 GUI 输入。 |
| `EnableFling` | bool | true | 拖动松开后是否带惯性继续滑动。 |
| `PixelDensity` | float | 1.0 | 地图纹理渲染倍率，钳制到 0.5-4.0。见 [PixelDensity 与快照](#pixeldensity-与快照)。 |
| `TileSource` | MapTileSourceType | OpenStreetMap | 底图瓦片源：`None`、`OpenStreetMap`、`EsriWorldTopo`、`EsriWorldDarkGray` 或 `CustomXyz`。 |
| `CustomTileUrl` | string | "" | `TileSource = CustomXyz` 时的 XYZ URL 模板。占位符：`{z}/{x}/{y}/{s}/{k}`。 |
| `CustomTileSubdomains` | string | "" | 逗号分隔的子域列表，替换模板中的 `{s}`（如 `a,b,c`）。 |
| `TileApiKey` | string | "" | 需要鉴权的服务使用的密钥，替换 `{k}`。 |
| `TileMinZoom` | int | 0 | 向服务端请求的最低缩放级别（0-20）。 |
| `TileMaxZoom` | int | 20 | 向服务端请求的最高缩放级别（0-20）。 |
| `TileUserAgent` | string | "GodotNodeExtension GodotMapsui/1.0" | 每个瓦片请求携带的 User-Agent，请保持可识别（见 [瓦片与合规](#瓦片与合规)）。 |
| `ShowAttribution` | bool | false | 用 Godot 字体在左下角绘制 `AttributionText`。默认关闭 —— Mapsui 已把署名画进纹理。 |
| `CustomAttribution` | string | "" | `TileSource = CustomXyz` 时使用的归属信息。 |
| `DebugLogging` | bool | false | 向 Godot 控制台输出生命周期/每帧诊断。真实错误始终会报告。 |

### 只读状态

| 属性 | 类型 | 说明 |
|------|------|------|
| `IsReady` | bool | 地图已创建**且**已建立有效视口时为 true；见 [尺寸要求](#尺寸要求)。 |
| `HasTexture` | bool | 存在 Skia 表面（也就是真的能画）时为 true。可与 `IsReady` 一起判断是否处于无头环境。 |
| `IsBusy` | bool | 仍有图层在取数据（如瓦片）时为 true，可用于转圈指示。 |
| `ZoomLevel` | double | 当前（可含小数的）缩放级别（0 = 全球，20 = 建筑级）。 |
| `Center` | (double, double) | 当前中心点，`(Latitude, Longitude)`，WGS84 度。 |
| `Rotation` | double | 当前旋转角，自正北顺时针，单位为度。 |
| `TextureSize` | Vector2I | 渲染纹理尺寸（像素）= 控件尺寸 x `PixelDensity`；无表面时为 `(0, 0)`。 |
| `AttributionText` | string | 当前瓦片源要求的归属行；无需归属时为空串。 |
| `Layers` | IReadOnlyList<ILayer> | 当前图层的快照，最底层在前。 |
| `MapInstance` | Map? | 底层 `Mapsui.Map`，用于高级配置。 |

### 方法

| 方法 | 返回 | 说明 |
|------|------|------|
| `AddLayer` | void | `AddLayer(ILayer layer)` —— 在栈顶追加一个图层。 |
| `InsertLayer` | void | `InsertLayer(int index, ILayer layer)` —— 在 `index` 处插入（0 = 最底层；index 会被钳制到图层数量范围内）。 |
| `RemoveLayer` | void | `RemoveLayer(ILayer layer)` —— 移除一个图层。 |
| `ClearLayers` | void | `ClearLayers()` —— 清空所有图层，包括由 `TileSource` 创建的底图。 |
| `SetCenter` | void | `SetCenter(double latitude, double longitude, long duration = -1)` —— 移动中心；`duration` 为毫秒动画，`-1` 表示立即跳转。 |
| `SetZoomLevel` | void | `SetZoomLevel(int level, long duration = -1)` —— 缩放到整数级（钳制到 0-20）。 |
| `ZoomBy` | void | `ZoomBy(double levels, Vector2? anchor = null, long duration = -1)` —— 按整数或小数级缩放。`anchor`（控件像素）保持不动（例如光标下）；`null` 时以中心缩放。 |
| `SetRotation` | void | `SetRotation(double degrees, long duration = -1)` —— 自正北顺时针旋转；`0` 为正北朝上。 |
| `NavigateTo` | void | `NavigateTo(double latitude, double longitude, int zoomLevel, long duration = 500)` —— 一次调用完成带动画的中心 + 缩放。 |
| `FitToBounds` | void | `FitToBounds(lat1, lon1, lat2, lon2, double paddingRatio = 0.05, long duration = -1)` —— 框住一个范围；两个角点顺序任意；`paddingRatio` 加边距。 |
| `ApplyInitialView` | void | `ApplyInitialView(long duration = -1)` —— 重新应用导出的初始纬度/经度/缩放（运行期改过之后，或想复位时使用）。 |
| `ScreenToLatLon` | (double, double) | `ScreenToLatLon(Vector2 position)` —— 控件像素（局部坐标）到 WGS84 `(Latitude, Longitude)`。 |
| `LatLonToScreen` | Vector2 | `LatLonToScreen(double latitude, double longitude)` —— WGS84 到控件像素；`ScreenToLatLon` 的逆运算。 |
| `RefreshMap` | void | `RefreshMap()` —— 强制完整刷新数据与渲染。 |
| `GetSnapshot` | Image? | `GetSnapshot()` —— 以纹理像素捕获当前视图为 Godot `Image`；无表面时返回 `null`。 |

### 信号

| 信号 | 说明 |
|------|------|
| `MapReady` | 地图初始化完成（首次建立有效视口）时触发一次。 |
| `ViewportChanged` | 视口（中心、缩放或旋转）变化时触发 —— 包括滚轮缩放、动画/惯性以及代码导航，而不只是拖动。 |
| `MapTapped` | `(latitude, longitude)` —— 注册了一次点击/轻触。始终触发。 |
| `MapFeatureTapped` | `(layerName, featureId, latitude, longitude)` —— 点击命中了要素；该要素同时也会收到 Mapsui 自己的点击事件（气泡等）。 |
| `MapBackgroundTapped` | `(latitude, longitude)` —— 点击未命中任何要素。 |

## 输入

- **拖动**：按住鼠标左键（或单指）平移地图。
- **滚轮**：以指针为中心缩放；**`InputEventMagnifyGesture`**（触控板捏合）连续缩放。
- **双指触摸**：捏合 + 平移合一，两指间距变化改变比例，中点移动即平移。
- **触控板双指平移**（`InputEventPanGesture`）：平移地图。
- **惯性（Fling）**：`EnableFling` 开启时，松开拖动后地图继续滑行。
- **点击 vs 拖动**：只有整个手势中指针移动**不超过 4 像素**、且没有出现第二根手指时，才判定为点击。拖动（无论多慢）都算平移而非点击，捏合手势也永远不会报告点击。

以上都依赖 `EnableInteraction`（默认 `true`）；关闭后控件不消费输入事件。

## 瓦片与合规

`TileSource` 选择底图：

- `None` —— 不加瓦片图层；用 `AddLayer` 自行添加。
- `OpenStreetMap`（默认）、`EsriWorldTopo`、`EsriWorldDarkGray` —— 内置源，按 `TileMinZoom`/`TileMaxZoom` 请求。
- `CustomXyz` —— 任意 XYZ 模板。`CustomTileUrl` 支持 `{z}`（缩放）、`{x}`（列）、`{y}`（行）、`{s}`（子域）与 `{k}`（API key）；`CustomTileSubdomains` 填充 `{s}`，`TileApiKey` 填充 `{k}`。URL 为空时不会创建图层。

```csharp
map.TileSource = MapTileSourceType.CustomXyz;
map.CustomTileUrl = "https://{s}.tiles.example.com/{z}/{x}/{y}.png?key={k}";
map.CustomTileSubdomains = "a,b,c";
map.TileApiKey = "YOUR_API_KEY";
map.CustomAttribution = "© Example Maps";
```

**User-Agent。** `TileUserAgent` 会附加到每个瓦片请求上。OpenStreetMap 的瓦片使用政策要求客户端可识别，因此发布产品时请保留一个有意义的取值（默认值已经可用）。

**归属信息。** 大多数服务商都要求显示归属。Mapsui 渲染器已经把瓦片源的署名画进地图纹理（右下角），所以通常无需额外处理。`AttributionText` 暴露同一行文本，便于你在别处展示；`ShowAttribution` 则用控件主题字体在左下角再加一个标签 —— 当你自己替换了瓦片图层，或想要跟随主题的标签时再打开。

## PixelDensity 与快照

`PixelDensity` 是地图纹理的渲染倍率：设为 `2.0` 时表面尺寸是控件的两倍，再缩放进控件显示，从而在 HiDPI 屏幕或控件被放大时保持文字清晰。取值钳制在 **0.5 - 4.0**；越界值会贴到最近的边界，而不是分配一张巨大的表面。

- `TextureSize` 以像素报告纹理：控件尺寸 x `PixelDensity`（无表面时为 `(0, 0)`）。
- 指针输入在交给 Mapsui 之前会被映射到该纹理像素空间，因此任何倍率下命中测试都对齐。
- `GetSnapshot()` 返回的同样是纹理像素的 `Image` —— 控件尺寸乘以 `PixelDensity`。
- `ScreenToLatLon` / `LatLonToScreen` 收发的是**控件**像素（控件局部坐标），倍率由组件内部处理。

```csharp
map.PixelDensity = 2f;                       // 160x120 的控件 -> 320x240 的纹理
var snapshot = map.GetSnapshot();            // 320x240 的 Image
snapshot?.SavePng("user://map.png");
```

## 无头 / 无渲染设备

创建 Skia 表面需要渲染设备。没有设备时（无头 CI、无 GPU），组件会让相机保持可用：`IsReady` 为 `true`、`HasTexture` 为 `false`，并且只打印一次 WARNING，而不是每帧报错。导航、图层与坐标换算都照常工作，只是没有画面。

```csharp
if (!map.HasTexture)
    GD.Print($"没有表面，但相机可用：中心 {map.Center} z{map.ZoomLevel:F1}");
```

## 故障排查

地图空白？按这个顺序检查：

1. **尺寸** —— 控件是否真的大于 0x0？放进 `ScrollContainer` 时请给它 `custom_minimum_size`（见 [尺寸要求](#尺寸要求)）。
2. **网络 / 瓦片源** —— `CustomTileUrl` 写错或没有网络都会得到空白地图；内置源需要联网。
3. **无渲染设备** —— `HasTexture` 为 `false`（见 [无头 / 无渲染设备](#无头--无渲染设备)）。
4. **仍未解决？** —— 把 `DebugLogging = true`，观察控制台里的 `[MapsuiControl]` 输出；瞬时状态保持安静，真实错误始终会报告。

## MapsuiHelper

无需控件实例即可调用的静态工具。

- `MapTileSourceType` —— `None`、`OpenStreetMap`、`EsriWorldTopo`、`EsriWorldDarkGray`、`CustomXyz`。
- `CreateTileLayer(sourceType, customUrl, subdomains, apiKey, minZoom, maxZoom, userAgent)` —— 按配置构造瓦片图层；`None` 返回 `null`（`CustomXyz` 的 URL 为空时同样返回 `null`）。
- `CreateOpenStreetMapLayer()` —— 一个 OSM 瓦片图层。
- `CreateDefaultMap()` —— 预置 OSM 图层的 `Map`。
- `ToSphericalMercator(latitude, longitude)` —— WGS84 到 EPSG:3857 `(X, Y)`。
- `ToLatLon(x, y)` —— EPSG:3857 到 `(Latitude, Longitude)`。
- `ZoomLevelToResolution(level)` / `ResolutionToZoomLevel(resolution)` —— 互为逆运算；后者钳制到 0-24，无法映射的分辨率返回 `0`。
- `LatLonToMercatorBox(lat1, lon1, lat2, lon2, paddingRatio = 0.05)` —— WGS84 范围到 `MRect`；角点顺序任意，`paddingRatio` 会扩大盒子。
- `AttributionFor(sourceType, customAttribution = "")` —— 源要求的署名行（无需署名时为空串）。

```csharp
var (x, y) = MapsuiHelper.ToSphericalMercator(35.6762, 139.6503);
var (lat, lon) = MapsuiHelper.ToLatLon(x, y);
double resolution = MapsuiHelper.ZoomLevelToResolution(12);
string credit = MapsuiHelper.AttributionFor(MapTileSourceType.OpenStreetMap);
```

## 依赖

- `Mapsui` >= 5.0.2
- `Mapsui.Rendering.Skia` >= 5.0.2
- `Mapsui.Tiling` >= 5.0.2
- `SkiaSharp` >= 3.116.0
- `GodotSkia` 组件（内部）
- Godot 4.0+（.NET 版）与 .NET 10.0+
