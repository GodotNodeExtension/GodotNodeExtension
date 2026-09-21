[English](README.md) | **中文**

# GodotNodeExtension

面向 Godot 4.7+、带 C# 支持的自定义节点与扩展集合，为游戏开发提供增强的 UI 组件与工具。

## 🚀 功能特性

- **自定义节点组件**：开箱即用，扩展 Godot 既有功能的节点
- **原子化设计**：模块化、自包含的组件，可独立安装
- **按需加载**：只安装你需要的组件及其特定依赖，避免臃肿的包
- **跨平台支持**：可用于 Windows、Linux 和 macOS
- **安装简单**：一个 .NET 工具用一条命令把组件连同其依赖一起装好
- **文档齐全**：每个组件都有完整的指南与示例

## 📦 可用组件

> 组件登记表——每个组件及其版本、依赖和状态——见 [COMPONENTS.md](COMPONENTS.md)；
> 它由各 `Component/<Component>/component_info.json` 生成，因此不会与源码脱节。

## 🛠️ 快速开始

### 前置条件

- **Godot 4.7+**，且启用 .NET 支持（使用 Godot 4.7.2 构建与测试）
- **.NET SDK 10.0+**（源码的目标框架为 `net10.0`；`global.json` 以 `rollForward: latestMajor` 锁定 SDK 10.0.0）
- **Git**（仅手动安装方式需要）

### 安装

#### 方式一：安装器 CLI（推荐）

安装器以 .NET 工具的形式发布，因此一条命令装好安装器，再用一条命令装一个组件：

```bash
dotnet tool install -g GodotNodeExtensionInstaller               # 只需执行一次

godotNodeInstall list                                            # 列出组件及其版本与依赖
godotNodeInstall install GodotChart                              # 安装到当前目录下的 Godot 项目
godotNodeInstall install GodotChart "D:/MyGame"                  # ... 或指定项目路径
godotNodeInstall install GodotChart "D:/MyGame" --example        # 同时安装该组件的示例场景
godotNodeInstall install GodotChart "D:/MyGame" --from-release   # 从最新 release 安装，而不是本仓库
godotNodeInstall install GodotChart "D:/MyGame" --force          # 覆盖已有副本

godotNodeInstall update --path "D:/MyGame"                       # 更新所有已安装的组件
godotNodeInstall check  --path "D:/MyGame"                       # 检查环境
```

组件会落到 `addons/GodotNodeExtension/<Component>/`，其 NuGet 依赖（以及这些依赖所需的组件，除非传入
`--skip-dependencies`）会替你装好。工具本身的目标框架是 `.NET 9`，因此如果你的机器只有更新的运行时，
要么安装 9.0 运行时，要么用 `DOTNET_ROLL_FORWARD=Major godotNodeInstall …` 启动它。

#### 方式二：手动安装

1. 克隆本仓库：
   ```bash
   git clone https://github.com/GodotNodeExtension/GodotNodeExtension.git
   ```

2. 把想要的组件从 `Component/[ComponentName]/` 复制到你项目的 `addons/GodotNodeExtension/[ComponentName]/` 目录

3. 安装组件的 `component_info.json` 中声明的 NuGet 依赖（如果有）

4. 构建你的项目：
   ```bash
   dotnet build
   ```

## 🏗️ 项目结构

```
GodotNodeExtension/
├── Component/                   # 组件源码（C#）
│   └── Xxx/                     #   + component_info.json（名称、版本、依赖、要求）
├── Doc/                         # 文档，每个组件一个目录
│   └── Xxx/                     #   README.md + README.cn.md（+ 专题页，始终成对出现）
├── Example/                     # 演示场景，每个组件一个目录
│   └── Xxx/
│       ├── demos.json           #   可选：浏览器中的顺序 + 标题 + 描述
│       └── *Demo.tscn           #   每个功能一个场景
├── Test/                        # gdUnit4 测试套件（Integration/ 需要渲染设备）
├── Tools/                       # Python 辅助脚本：组件、测试、文档检查
├── addons/                      # 随仓库携带的 addon：gdUnit4
├── COMPONENTS.md                # 生成的组件登记表
└── README.md                    # 本文件（+ README.cn.md）
```

## 🔧 开发

> 改代码前先读 [Tools/README.md](Tools/README.md)——组件布局与覆盖它的各项检查都写在那里。临时产物放在
> 已被 git 忽略的 `tmp/`；构建必须零警告（分析器设置见 `.editorconfig`）。

### 从源码构建

1. 克隆仓库：
   ```bash
   git clone https://github.com/GodotNodeExtension/GodotNodeExtension.git
   cd GodotNodeExtension
   ```

2. 用启用 .NET 支持的 Godot 4.7+ 打开

3. 构建项目：
   ```bash
   dotnet build
   ```

### 工具与测试

一切都由 [`Tools/`](Tools/README.md) 里的工具驱动（纯 Python 3，无需安装任何包）：

```bash
python Tools/components.py list                # 组件、版本、依赖、依赖方
python Tools/components.py dependents --component GodotSkia   # 改动这里会影响什么

python Tools/run_tests.py --mode fast          # 只测你改动过的组件
python Tools/run_tests.py                      # 再测所有依赖它们的组件
python Tools/run_tests.py --mode all --render --integration    # 全部测一遍，使用真实渲染设备

python Tools/check_doc_parity.py               # 中英文文档仍然一致
python Tools/check_doc_examples.py             # 文档里的 C# 片段仍然能编译
python Tools/check_doc_coverage.py             # 每个公共成员仍然带 XML 文档
```

`run_tests.py` 是全项目入口——CI 跑的就是它，并且它构建到所有会话共用的那个输出目录；`--component NAME`
可以把它收窄到单个组件以及依赖它的那些组件。

`Test/<Component>/Integration/` 下的集成测试套件通过引擎的真实设备渲染，因此需要 `--render`；运行器还
接受 `--component NAME`、`--suite res://…`、`--base REF` 和 `--changed NAME`。

### 添加新组件

```bash
python Tools/components.py create
```

不带参数运行时，它会依次询问组件名、类型、说明、作者和版本（直接回车即取中括号里的值）。也可以不走
交互，改用参数一次给全：

```bash
python Tools/components.py create MyComponent --type library --description "What it does" --author you
```

两种方式都会生成下面这套布局——源码、示例场景、中英文文档对和 gdUnit4 套件——并刷新
`COMPONENTS.md`，新组件随即能过 `verify`、也能跑第一次测试。手动做的话就是下面这些步骤：

1. 在 `Component/[YourComponentName]/` 下新建目录
2. 添加带 `[Tool]` 和 `[GlobalClass]` 特性的 C# 源文件
3. 创建 `component_info.json` 文件，写入依赖信息
4. 在 `Doc/[YourComponentName]/` 下添加文档（英文加上对应的 `*.cn.md` 文件）；工具和 PR 检查会在那里
   找 `README.md`
5. 在 `Example/[YourComponentName]/` 下创建示例。一个组件可以带**多个**演示场景：浏览器会把该目录下的
   每个 `*.tscn` 列在组件的树节点下，顺序与描述取自可选的 `demos.json`（子目录会成为嵌套分组，以
   `_` 开头的目录会被跳过）
6. 在 `Test/[YourComponentName]/` 下添加测试（gdUnit4；需要渲染设备的套件放到
   `Test/[YourComponentName]/Integration/`）
7. 更新主 `COMPONENTS.md` 文件

工具会从这些目录发现新组件，因此无需在任何地方编辑列表：
`python Tools/components.py verify --component [YourComponentName]` 会检查上面的目录布局。

### 贡献

欢迎贡献！请按以下步骤进行：

1. Fork 本仓库
2. 创建功能分支（`git checkout -b feature/amazing-component`）
3. 提交你的改动（`git commit -m 'Add amazing component'`）
4. 推送到该分支（`git push origin feature/amazing-component`）
5. 发起 Pull Request

## 📋 环境要求

- **Godot**：4.7 或更高（使用 4.7.2 构建与测试）
- **.NET SDK**：10.0 或更高（源码的目标框架为 `net10.0`）
- **平台**：Windows、Linux、macOS

## 📄 许可证

本项目基于 MIT 许可证授权 - 详见 [LICENSE](LICENSE) 文件。

## 🤝 支持

- **Issues**：在 [GitHub Issues](https://github.com/GodotNodeExtension/GodotNodeExtension/issues) 报告缺陷、提出功能请求
- **Discussions**：在 [GitHub Discussions](https://github.com/GodotNodeExtension/GodotNodeExtension/discussions) 参与讨论
- **文档**：查看各组件自己的 README 文件，获取详细用法说明

---

用 ❤️ 为 Godot 社区打造
