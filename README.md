**English** | [中文](README.cn.md)

# GodotNodeExtension

A collection of custom nodes and extensions for Godot 4.7+ with C# support, providing enhanced UI components and utilities for game development.

## 🚀 Features

- **Custom Node Components**: Ready-to-use nodes that extend Godot's functionality
- **Atomic Design**: Modular, self-contained components that can be installed independently
- **On-Demand Loading**: Install only the components you need with their specific dependencies, avoiding bloated packages
- **Cross-Platform Support**: Works on Windows, Linux, and macOS
- **Easy Installation**: A .NET tool installs a component together with its dependencies in one command
- **Full Documentation**: Comprehensive guides and examples for each component

## 📦 Available Components

> The registry — every component with its version, dependencies and status — lives in [COMPONENTS.md](COMPONENTS.md);
> it is generated from each `Component/<Component>/component_info.json`, so it never drifts from the sources.

## 🛠️ Quick Start

### Prerequisites

- **Godot 4.7+** with .NET support enabled (built and tested with Godot 4.7.2)
- **.NET SDK 10.0+** (the sources target `net10.0`; `global.json` pins SDK 10.0.0 with `rollForward: latestMajor`)
- **Git** (only for the manual installation route)

### Installation

#### Option 1: The Installer CLI (Recommended)

The installer is published as a .NET tool, so one command installs it and one installs a component:

```bash
dotnet tool install -g GodotNodeExtensionInstaller               # once

godotNodeInstall list                                            # components with versions and dependencies
godotNodeInstall install GodotChart                              # into the Godot project in the current directory
godotNodeInstall install GodotChart "D:/MyGame"                  # ... or an explicit project path
godotNodeInstall install GodotChart "D:/MyGame" --example        # plus that component's example scenes
godotNodeInstall install GodotChart "D:/MyGame" --from-release   # from the latest release instead of the repo
godotNodeInstall install GodotChart "D:/MyGame" --force          # overwrite an existing copy

godotNodeInstall update --path "D:/MyGame"                       # refresh every installed component
godotNodeInstall check  --path "D:/MyGame"                       # verify the environment
```

The component lands in `addons/GodotNodeExtension/<Component>/` and its NuGet dependencies (and, unless
`--skip-dependencies` is passed, the components they need) are installed for you. The tool itself targets
`.NET 9`, so if your machine only has a newer runtime, either install the 9.0 runtime or start it with
`DOTNET_ROLL_FORWARD=Major godotNodeInstall …`.

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
├── Component/                   # Component source (C#)
│   └── Xxx/                     #   + component_info.json (name, version, dependencies, requirements)
├── Doc/                         # Documentation, one folder per component
│   └── Xxx/                     #   README.md + README.cn.md (+ topic pages, always in pairs)
├── Example/                     # Demo scenes, one folder per component
│   └── Xxx/
│       ├── demos.json           #   optional: order + titles + descriptions for the browser
│       └── *Demo.tscn           #   one scene per feature
├── Test/                        # gdUnit4 suites (Integration/ needs a rendering device)
├── Tools/                       # Python helpers: components, tests, doc checks
├── addons/                      # Vendored addon: gdUnit4
├── COMPONENTS.md                # Generated component registry
└── README.md                    # This file (+ README.cn.md)
```

## 🔧 Development

> Before changing code, read [Tools/README.md](Tools/README.md) - the component layout and the checks that
> run over it are documented there. Scratch files belong in the git-ignored `tmp/`, and a build has to be
> warning-free (the analyzer settings are in `.editorconfig`).

### Building from Source

1. Clone the repository:
   ```bash
   git clone https://github.com/GodotNodeExtension/GodotNodeExtension.git
   cd GodotNodeExtension
   ```

2. Open in Godot 4.7+ with .NET support enabled

3. Build the project:
   ```bash
   dotnet build
   ```

### Tools and Tests

Everything is driven by the tools in [`Tools/`](Tools/README.md) (plain Python 3, no packages):

```bash
python Tools/components.py list                # components, versions, dependencies, dependents
python Tools/components.py dependents --component GodotSkia   # what a change here affects

python Tools/run_tests.py --mode fast          # only the components you changed
python Tools/run_tests.py                      # those + every component that depends on them
python Tools/run_tests.py --mode all --render --integration    # everything, real rendering device

python Tools/check_doc_parity.py               # EN/CN docs still agree
python Tools/check_doc_examples.py             # the C# snippets in the docs still compile
python Tools/check_doc_coverage.py             # every public member still carries XML docs
```

`run_tests.py` is the whole-project entry - CI runs it, and it builds into the output directory that every
session shares; `--component NAME` scopes it to one component and the components that depend on it.

The integration suites under `Test/<Component>/Integration/` render through the engine's real device
and need `--render`; the runner also takes `--component NAME`, `--suite res://…`, `--base REF` and
`--changed NAME`.

### Adding New Components

```bash
python Tools/components.py create
```

Run it without arguments: it asks for the component name, its type, the description, the author and the
version (Enter takes the value in brackets). The non-interactive form passes them instead:

```bash
python Tools/components.py create MyComponent --type library --description "What it does" --author you
```

Either way it scaffolds the layout below - sources, an example scene, the English/Chinese documentation
pair and a gdUnit4 suite - and refreshes `COMPONENTS.md`, so the new component is ready for `verify` and for
its first test run. Doing it by hand is the same steps:

1. Create a new directory under `Component/[YourComponentName]/`
2. Add your C# source files with `[Tool]` and `[GlobalClass]` attributes
3. Create a `component_info.json` file with dependency information
4. Add documentation under `Doc/[YourComponentName]/` (English plus the matching `*.cn.md` files); the
   tools and the PR check look for a `README.md` there
5. Create examples under `Example/[YourComponentName]/`. A component may ship **several** demo
   scenes: the browser lists every `*.tscn` in that folder under the component's tree node, ordered and
   described by an optional `demos.json` (sub-directories become nested groups, folders starting with
   `_` are skipped)
6. Add tests under `Test/[YourComponentName]/` (gdUnit4; suites that need a rendering device go into
   `Test/[YourComponentName]/Integration/`)
7. Update the main `COMPONENTS.md` file

The tools discover the new component from those directories, so no list has to be edited anywhere:
`python Tools/components.py verify --component [YourComponentName]` checks the layout above.

### Contributing

We welcome contributions! Please follow these steps:

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/amazing-component`)
3. Commit your changes (`git commit -m 'Add amazing component'`)
4. Push to the branch (`git push origin feature/amazing-component`)
5. Open a Pull Request

## 📋 Requirements

- **Godot**: 4.7 or higher (built and tested with 4.7.2)
- **.NET SDK**: 10.0 or higher (the sources target `net10.0`)
- **Platform**: Windows, Linux, macOS

## 📄 License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## 🤝 Support

- **Issues**: Report bugs and request features on [GitHub Issues](https://github.com/GodotNodeExtension/GodotNodeExtension/issues)
- **Discussions**: Join the conversation in [GitHub Discussions](https://github.com/GodotNodeExtension/GodotNodeExtension/discussions)
- **Documentation**: Check individual component README files for detailed usage instructions

---

Made with ❤️ for the Godot community
