# Components Registry

This file contains all available components in the GodotNodeExtension project.

## Available Components

| Component | Version | Author | Description | Status |
|-----------|---------|--------|-------------|---------|
| [DynamicNumberLabel](Doc/DynamicNumberLabel/README.md) | 1.0.1 | shitake2333 | A custom Godot Label node for animated number display with customizable formatting and transitions | ✅ Complete |
| [GodotChart](Doc/GodotChart/README.md) | 0.2.0 | shitake2333 | A declarative chart library for Godot with a SkiaSharp backend: 22 chart kinds (20 Mark classes, plus the SectionMark annotation mark) across Cartesian, polar, hierarchical and flow layouts with auto-inferred scales, theming, animation and hit testing. | ✅ Complete |
| [GodotSkia](Doc/GodotSkia/README.md) | 0.9.0 | shitake2333 | A 2D rendering bridge between SkiaSharp and Godot. Shares the engine's Vulkan surface with Skia's GRContext (with a CPU fallback for every other renderer, including the OpenGL compatibility renderer), and provides bidirectional Godot/Skia type converters, an image interop layer and diagnostics for the shared GPU context. | ✅ Complete |

## Contributing

To add a new component:

1. Create a new directory under `Component/[ComponentName]/`
2. Add your component files with `[Tool]` and `[GlobalClass]` attributes
3. Create a `component_info.json` file with the following structure:
   ```json
   {
     "name": "ComponentName",
     "version": "1.0.0",
     "author": "YourName",
     "description": "Brief description of your component",
     "license": "MIT",
     "requirements": {
       "godot": ">=4.0.0",
       "dotnet": ">=6.0"
     },
     "dependencies": {
       "nuget": [
         {
           "name": "PackageName",
           "version": ">=1.0.0",
           "required": true
         }
       ],
       "components": [
         "DependentComponentName"
       ]
     }
   }
   ```
4. Add a `README.md` file with usage documentation
5. Create examples under `Example/[ComponentName]/`
6. Submit a pull request

This file is automatically updated when component_info.json files are modified.

---

*Last updated: 2026-09-21 12:37:33 UTC*
