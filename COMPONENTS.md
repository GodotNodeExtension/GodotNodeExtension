# Components Registry

This file contains all available components in the GodotNodeExtension project.

## Available Components

| Component | Version | Author | Description | Status |
|-----------|---------|--------|-------------|---------|
| [DynamicNumberLabel](Component/DynamicNumberLabel/README.md) | 1.0.0 | shitake2333 | A custom Godot Label node for animated number display with customizable formatting and transitions | ✅ Complete |
| [GodotChart](Component/GodotChart/README.md) | 0.1.0 | shitake2333 | A declarative chart library for Godot with SkiaSharp backend. Supports bar charts, line charts, area charts, and scatter/bubble plots with auto-scaling, color mapping, and gradient fills. | 📋 Planned |

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
