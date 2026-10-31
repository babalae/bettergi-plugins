# BetterGI 插件

BetterGI 官方插件的开发仓库。插件机制的设计见主程序仓库的 [Docs/design/plugin-system.md](https://github.com/babalae/better-genshin-impact/blob/main/Docs/design/plugin-system.md)。

## 插件列表

### 传送

| 插件标识 | 名称 | 说明 |
| --- | --- | --- |
| `experimental_teleport` | 实验传送 | PR #3697 的实验性传送：页面状态机、安全区拖动、滑块缩放，带插件设置界面。迁移与未迁移的内容见 [插件说明](src/BetterGI.Plugin.ExperimentalTeleport/README.md) |

实现标识为 `experimental_teleport.state_machine`。插件复制 PR 中的整个 `TpTask.cs` 改写为 `ExperimentalTpTask`，实现主程序的 `ITpTask`，经可插拔能力 `ITpTaskFactory` 替换主程序的全部传送与大地图操作。启用后在插件页“功能选择 → 传送”中选择“实验传送”，参数通过插件卡片上的“设置”按钮在弹窗中调整。

### OCR 模型

主程序只内置 PP-OCRv6。其他版本、其他语言的 PaddleOCR 模型在这里以插件形式提供，每个“版本 + 语言”一个插件。

| 插件标识 | 名称 | 模型 |
| --- | --- | --- |
| `ppocr_v4` | PaddleOCR V4 中文 | PP-OCRv4 检测 + 中文识别 |
| `ppocr_v4_en` | PaddleOCR V4 英文 | PP-OCRv4 检测 + 英文、数字识别 |
| `ppocr_v5` | PaddleOCR V5 中文 | PP-OCRv5 检测 + 中文识别（也能识别英文和日文） |
| `ppocr_v5_latin` | PaddleOCR V5 拉丁语系 | PP-OCRv5 检测 + 拉丁字母语言识别 |
| `ppocr_v5_eslav` | PaddleOCR V5 斯拉夫语系 | PP-OCRv5 检测 + 俄语、乌克兰语、白俄罗斯语识别 |
| `ppocr_v5_korean` | PaddleOCR V5 韩文 | PP-OCRv5 检测 + 韩文识别 |

每个插件提供一个 OCR 模型实现，标识为 `<插件标识>.model`。在 BetterGI 的插件页安装并启用后，到“设置 → OCR 配置 → OCR 模型”中选择（插件页的“功能选择”中也能选）。

## 目录结构

```text
bettergi-plugins/
├─ Directory.Build.props      公共属性：目标框架、主程序仓库位置、资源包版本
├─ Directory.Build.targets    公共构建步骤：引用主程序、复制模型与资源、生成 plugin.json、打包
├─ bettergi-plugins.sln       插件工程，以及用于编译和调试的主程序工程
└─ src/
   ├─ Shared/PpOcrModels.cs   OCR 插件共用的模型组合代码
   └─ BetterGI.Plugin.*/      每个插件一个工程
```

工程文件中可以使用的构建项与属性：

| 名称 | 说明 |
| --- | --- |
| `BgiPluginId`、`BgiPluginName`、`Version`、`Description` | 写入 `plugin.json` |
| `BgiPaddleOcrModel`、`BgiPaddleOcrFile` | 从 `BetterGI.Assets.Model` 包复制 PaddleOCR 模型目录、单个文件到插件目录 `Models/PaddleOCR/` |
| `BgiPluginContent` | 插件自带的资源文件（路径相对工程目录），按相同的相对路径放进插件目录，如 `<BgiPluginContent Include="Assets\**\*.png" />` |
| `BgiIncludeSharedSource` | 设为 `false` 时不编译 `src/Shared` 下的共享源码（目前只有 OCR 插件使用） |

插件需要 XAML 设置界面时设 `<UseWPF>true</UseWPF>`，并直接引用 `CommunityToolkit.Mvvm`（`ExcludeAssets="runtime"`）：源码生成器不会随主程序工程传递，运行时使用主程序的程序集。

## 构建

插件引用主程序工程开发，主程序仓库需要与本仓库放在同一个目录下：

```text
MainProject/
├─ better-genshin-impact/
└─ bettergi-plugins/
```

位置不同时用 `-p:BetterGIRepoDir=<主程序仓库目录>\` 指定。

```powershell
dotnet build bettergi-plugins.sln -c Release
```

构建产物在 `artifacts/`：

- `artifacts/plugins/<插件标识>/`：插件目录，可以直接复制到 BetterGI 程序目录的 `Plugins/` 下；
- `artifacts/<插件标识>-<版本>.zip`：安装包，在 BetterGI 的插件页点“从文件安装”。

构建插件时会同时编译主程序。主程序正在运行时，它的输出目录被占用，编译会失败，请先退出 BetterGI。

## 调试

Debug 构建后，插件会自动复制到主程序 Debug 输出目录的 `Plugins/` 下（`better-genshin-impact/BetterGenshinImpact/bin/x64/Debug/net10.0-windows10.0.22621.0/Plugins/`）。启动 Debug 版 BetterGI，在插件页启用插件即可。不需要复制时加 `-p:BgiCopyToHost=false`。

## NuGet 包

```powershell
dotnet pack src/BetterGI.Plugin.PpOcrV5/BetterGI.Plugin.PpOcrV5.csproj -c Release
```

包生成在 `artifacts/`。插件文件放在包内 `plugin/` 目录，包类型为 `BetterGIPlugin`，不声明依赖。BetterGI 的插件页可以直接“从文件安装”`.nupkg`。

## 插件设置界面

插件可以提供自己的设置界面（见实验传送插件）。插件页为它显示“设置”按钮，点击后在单独的弹窗中打开：

1. 设置 VM 继承 `ObservableObject`，标 `[PluginSettingsView(typeof(视图))]`，每个插件最多一个。每次打开弹窗时用插件服务容器新建 VM，构造函数可以注入 `IPluginContext` 和主程序服务；弹窗关闭后释放实现了 `IDisposable` 的 VM；
2. 视图是有无参构造函数的 `UserControl`，DataContext 为 VM。弹窗宽 640，高度随内容变化，超出时滚动；弹窗只有“关闭”按钮，修改应立即保存。视图只能使用自己声明的资源和应用级主题资源，局部样式用 `BasedOn` 继承 WPF-UI 默认样式；
3. 设置用 `IPluginContext.Settings` 读写，文件为 `User/PluginData/<插件标识>/settings.json`（System.Text.Json）。

## 新增一个 OCR 插件

1. 复制一个现有工程目录，修改目录名和工程文件名；
2. 在工程文件中修改 `BgiPluginId`、`BgiPluginName`、`Version`、`Description`，并用 `BgiPaddleOcrModel` 列出需要的模型目录（相对资源包 `BetterGI.Assets.Model` 的 `Assets/Model/PaddleOCR`）；
3. 修改实现类：`[PluggableImpl("model", "显示名")]`，用 `PpOcrModels.Create` 组合模型。模型名必须以 `PpOcrDet`、`PpOcrRec` 开头，硬件加速设置中的 OCR CPU 线程策略按这个前缀识别 PaddleOCR 模型；
4. `dotnet sln bettergi-plugins.sln add <工程> --solution-folder src --include-references false`。

清单 `plugin.json` 由构建生成，不需要手写。清单字段中不要使用双引号和反斜杠。
