# GodotNodeExtension Components List

This file contains a list of all available components in the GodotNodeExtension project.

## Component Registry

| Component Name | Version | Author | Description | Status |
|---------------|---------|---------|-------------|---------|
| [DynamicNumberLabel](Component/DynamicNumberLabel/README.md) | 1.0.0 | shitake2333 | A custom Godot Label node for animated number display with customizable formatting and transitions | ✅ Complete |
| [GodotSkia](Component/GodotSkia/README.md) | - | shitake2333 | SkiaSharp integration for Godot with texture rendering support | 🚧 In Development |
| [MarkDownView](Component/MarkDownView/README.md) | - | shitake2333 | Markdown viewer component for Godot | 🚧 In Development |

## Installation

Use the provided installation scripts to install components:

### PowerShell (Windows)
```powershell
.\GodotNodeExtensionInstaller.ps1 -ComponentName "DynamicNumberLabel"
```

### Bash (Linux/macOS)
```bash
./install-component.sh -c DynamicNumberLabel
```

## Contributing

To add a new component to this list:
1. Create a new directory under `Component/[ComponentName]/`
2. Add a `component_info.json` file with component metadata
3. Add a `README.md` file with component documentation
4. Update this `COMPONENTS.md` file
5. Create examples under `Example/[ComponentName]/`

## Last Updated
- Date: 2025-01-21
- Total Components: 3
- Complete Components: 1
- In Development: 2
