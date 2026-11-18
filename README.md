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

### 界面语言

主程序内置简体中文、繁體中文、English、日本語、Русский，其他界面语言在这里以插件形式提供，每种语言一个插件。设计见主程序仓库的 [Docs/design/ui-language-plugin.md](https://github.com/babalae/better-genshin-impact/blob/main/Docs/design/ui-language-plugin.md)。

| 插件标识 | 名称 | 语言 |
| --- | --- | --- |
| `lang_it` | 界面语言：意大利语 | Italiano（`it`） |

在 BetterGI 的插件页安装并启用后，到“设置 → 软件UI语言”中选择。已选择的语言在插件停用后回退为简体中文，重新启用后自动恢复。

### ncnn 模型资源

| 插件标识 | 名称 | 说明 |
| --- | --- | --- |
| `ncnn_models` | ncnn 模型 | ncnn 推理后端所需的 det/rec 与 YOLO/分类模型（.param/.bin） |

与其他插件不同，这个插件**不提供任何 `[PluggableImpl]` 实现**，只通过 `BgiPluginContent` 附带 ncnn 模型文件——空壳入口 dll 仅用于通过插件清单的入口校验。它让 ncnn 模型独立于主程序资源包分发：安装后模型位于 `Plugins/ncnn_models/Models/<类别>/Ncnn/`，主程序按此约定读取（见 `NcnnModelPaths`），与插件的启用/可插拔状态无关。这类模型不走 `BgiPaddleOcrModel`，因此工程把 `BgiIncludeSharedSource` 设为 `false`。

## 目录结构

```text
bettergi-plugins/
├─ Directory.Build.props      公共属性：目标框架、主程序仓库位置、资源包版本
├─ Directory.Build.targets    公共构建步骤：引用主程序、复制模型与资源、生成 plugin.json、打包
├─ bettergi-plugins.sln       插件工程，以及用于编译和调试的主程序工程
└─ src/
   ├─ Shared/PpOcrModels.cs   OCR 插件共用的模型组合代码
   ├─ BetterGI.Plugin.Lang*/  语言插件，翻译文件在工程的 I18n/ 下
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

## 新增一个语言插件

1. 复制 `src/BetterGI.Plugin.LangIt`，修改目录名和工程文件名，如 `BetterGI.Plugin.LangPtBr`；
2. 在工程文件中修改 `BgiPluginId`（`lang_<语言代码>`，代码转小写、`-` 换成 `_`，如 `lang_pt_br`）、`BgiPluginName`、`Description`，以及 `BgiPluginContent` 中的语言文件；
3. 修改 `PluginInfo.cs` 中的程序集特性，如 `[assembly: UiLanguage("pt-BR", "Português (Brasil)")]`。语言文件默认为插件目录下的 `I18n/<语言代码>.json`，其他位置用特性的 `File` 属性指定；
4. 把语言文件放到工程的 `I18n/` 下，Key 为简体中文原文，值为翻译，空字符串表示未翻译（界面显示中文）。在主程序仓库运行 `.\Build\Scripts\i18n-sync.ps1`，会一并补齐插件中缺少的 Key、删除废弃 Key；
5. `dotnet sln bettergi-plugins.sln add <工程> --solution-folder src --include-references false`。

语言代码与内置语言相同（如 `en`）时，插件覆盖内置翻译：插件中非空的条目优先，其余条目仍取内置翻译。
