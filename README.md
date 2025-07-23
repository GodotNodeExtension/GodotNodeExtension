# GodotNodeExtension

A collection of custom nodes and extensions for Godot 4.x with C# support, providing enhanced UI components and utilities for game development.

## 🚀 Features

- **Custom Node Components**: Ready-to-use nodes that extend Godot's functionality
- **Atomic Design**: Modular, self-contained components that can be installed independently
- **On-Demand Loading**: Install only the components you need with their specific dependencies, avoiding bloated packages
- **Cross-Platform Support**: Works on Windows, Linux, and macOS
- **Easy Installation**: Automated installation scripts for seamless setup
- **Full Documentation**: Comprehensive guides and examples for each component

## 📦 Available Components

> For a complete list of components, see [COMPONENTS.md](COMPONENTS.md)

## 🛠️ Quick Start

### Prerequisites

- **Godot 4.0+** with .NET support enabled
- **.NET SDK 9.0+**
- **Git** (for installation scripts)

### Installation

#### Option 1: Automated Installation (Recommended)

**Windows (PowerShell):**
```powershell
# Get Install Script
Invoke-WebRequest -Uri "https://raw.githubusercontent.com/GodotNodeExtension/GodotNodeExtensionInstaller/main/GodotNodeExtensionInstaller.ps1" -OutFile "GodotNodeExtensionInstaller.ps1"

# Install a specific component
.\GodotNodeExtensionInstaller.ps1 -ComponentName "DynamicNumberLabel"

# List all available components
.\GodotNodeExtensionInstaller.ps1 -ListComponents

# Install from latest release
.\GodotNodeExtensionInstaller.ps1 -ComponentName "DynamicNumberLabel" -FromRelease
```

**Linux/macOS (Bash):**
```bash
# Get Install Script
curl -o GodotNodeExtensionInstaller.sh https://raw.githubusercontent.com/GodotNodeExtension/GodotNodeExtensionInstaller/main/GodotNodeExtensionInstaller.sh

# Make script executable
chmod +x GodotNodeExtensionInstaller.sh

# Install a specific component
./GodotNodeExtensionInstaller.sh -c DynamicNumberLabel

# List all available components
./GodotNodeExtensionInstaller.sh --list

# Install from latest release
./GodotNodeExtensionInstaller.sh -c DynamicNumberLabel --from-release
```

#### Option 2: Manual Installation

1. Clone this repository:
   ```bash
   git clone https://github.com/GodotNodeExtension/GodotNodeExtension.git
   ```

2. Copy the desired component from `Component/[ComponentName]/` to your project's `addons/GodotNodeExtension/[ComponentName]/` directory

3. Install NuGet dependencies (if any) specified in the component's `component_info.json`

4. Build your project:
   ```bash
   dotnet build
   ```

## 🏗️ Project Structure

```
GodotNodeExtension/
├── Component/                    # Component source code
│   ├── Xxx/                      # component
│   └── ...
├── Example/                     # Usage examples and demos
│   ├── Xxx/                     # component demo scene
│   └── ...
├── COMPONENTS.md               # Component registry and details
└── README.md                   # This file
```

## 🔧 Development

### Building from Source

1. Clone the repository:
   ```bash
   git clone https://github.com/shitake2333/GodotNodeExtension.git
   cd GodotNodeExtension
   ```

2. Open in Godot 4.x with .NET support enabled

3. Build the project:
   ```bash
   dotnet build
   ```

### Adding New Components

1. Create a new directory under `Component/[YourComponentName]/`
2. Add your C# source files with `[Tool]` and `[GlobalClass]` attributes
3. Create a `component_info.json` file with dependency information
4. Add a `README.md` file with usage documentation
5. Create examples under `Example/[YourComponentName]/`
6. Update the main `COMPONENTS.md` file

### Contributing

We welcome contributions! Please follow these steps:

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/amazing-component`)
3. Commit your changes (`git commit -m 'Add amazing component'`)
4. Push to the branch (`git push origin feature/amazing-component`)
5. Open a Pull Request

## 📋 Requirements

- **Godot**: 4.0 or higher
- **.NET**: 9.0 or higher
- **Platform**: Windows, Linux, macOS

## 📄 License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## 🤝 Support

- **Issues**: Report bugs and request features on [GitHub Issues](https://github.com/shitake2333/GodotNodeExtension/issues)
- **Discussions**: Join the conversation in [GitHub Discussions](https://github.com/shitake2333/GodotNodeExtension/discussions)
- **Documentation**: Check individual component README files for detailed usage instructions

---

Made with ❤️ for the Godot community
