# 实验传送插件

插件标识 `experimental_teleport`，实现标识 `experimental_teleport.state_machine`。

把 [PR #3697](https://github.com/babalae/better-genshin-impact/pull/3697)（实验性传送）的传送部分做成插件，替换主程序的内置传送。插件的 `ExperimentalTpTask` 是 PR 中 `TpTask.cs` 的完整副本，固定使用实验模式，实现主程序的 `ITpTask` 接口。

## 使用

1. 在 BetterGI 的插件页安装并启用本插件；
2. 在插件页“功能选择 → 传送”中选择“实验传送”；
3. 需要调整参数时，点击插件页中本插件的“设置”按钮，在弹出的窗口中修改。修改立即保存，之后创建的传送任务使用新参数。

选中后，主程序中所有经过传送任务的操作都改用插件：地图追踪的传送点、七天神像回血、自动秘境、地脉花、尘歌壶、领取奖励前打开大地图、脚本的 `genshin.tp`、`genshin.moveMapTo`、`genshin.clickMapPoint`、`genshin.setBigMapZoomLevel` 等。停用插件或改回“内置传送”后，下一次创建传送任务时恢复原有流程。

设置保存在 `User/PluginData/experimental_teleport/settings.json`，卸载插件时保留。

## 实现方式

主程序把 `TpTask` 的全部公开方法抽成接口 `ITpTask`：传送（`Tp`、`TpToStatueOfTheSeven`）、打开和确认大地图、移动与点击地图、拖动、缩放、识别地图位置和范围、取最近的传送点、切换国家和区域。调用方不再 `new TpTask(...)`，而是通过可插拔能力 `ITpTaskFactory`（标识 `teleport`）创建：

```csharp
var tpTask = Pluggable.Get<ITpTaskFactory>().Create(runtime, config, ct);
await tpTask.Tp(x, y);
```

内置实现 `builtin.standard` 返回主程序的 `TpTask`。本插件的 `ExperimentalTpTaskFactory` 每次创建时读取插件设置，返回新的 `ExperimentalTpTask`。传送任务带有实例状态（切换区域后记住的国家中心、识别缓存、拖动失败的起点），所以接口是工厂，每次使用创建一个实例。

插件中的文件与 PR 的对应关系：

| 插件文件 | 来源 | 改动 |
| --- | --- | --- |
| `ExperimentalTpTask.cs` | PR 的 `TpTask.cs` | 改为主程序当前的运行环境写法；去掉实验模式开关，删除只在非实验模式下使用的代码；参数取自插件设置；超时与“不可传送”重试来自 PR 的 `ExperimentalTeleportTask.cs`，合并进 `Tp` |
| `ExperimentalTeleportUiStateMachine.cs` | PR 的同名文件 | 参数取自插件设置 |
| `MapDragger.cs` | PR 的 `ExperimentalTeleportDrag.cs` | 参数取自插件设置 |
| `TeleportAssets.cs` | PR 的 `SwitchAreaRegionAssets.cs`、`SwitchAreaTemplateMatchDecisions.cs`，以及 `Recognition.json` 中新增的 `TeleportUnavailablePrompt` | 素材从插件目录读取 |
| `TeleportExceptions.cs` | PR `TpTask.cs` 中的异常类型 | 无 |
| `ExperimentalTeleportParameters.cs` | PR `TpConfig.cs` 中的 `GetEffectiveExperimentalTeleport*` | 倍率按主程序“传送操作速度”换算 |
| `ExperimentalTeleportSettings*.cs`、`.xaml` | PR `TpConfig.cs` 中的实验参数和 `CommonSettingsPage.xaml` 中的设置项 | 改为插件设置窗口 |
| `ExperimentalTpTaskFactory.cs` | 新增 | 读取设置、计算参数、加载素材，创建 `ExperimentalTpTask` |

## 迁移到插件的能力

PR 中实验模式下的全部传送逻辑都在插件中：

- **页面状态机**：主界面、大地图、区域列表、候选列表、传送详情、不可传送提示、加载画面、两级未知界面共 9 种状态；“确保在大地图”“打开区域列表”“选择区域”“确认传送”四种操作；未知界面回主界面恢复（最多 2 次）、候选列表点击（最多 2 次）、按 F 确认（最多 2 次）、面板未出现时的一次回退点击。
- **打开地图补按 M**：大地图未出现时按间隔补按，霜月地图超时 ×1.6。
- **加载画面识别**：画面中央饱和度标准差低于 25，且 85% 以上像素为纯暗（灰度 < 70）或纯亮（> 220），并且不在主界面。确认后至少 1 秒才算完成；一直没看到加载画面但已回到主界面 5 秒时按成功处理。
- **传送不可用提示**：出现时关闭提示后重试，三次都出现时报“传送点可能未开启”。
- **区域列表模板匹配**：16 个区域的模板和 15 个 b 变体（千星奇域没有），在右侧 1/3 区域内匹配，阈值 0.8；只有游戏语言下的区域名与中文名相同时才用模板，否则直接 OCR；模板缺失或未命中时回退 OCR。点击后等待选中生效再继续。
- **目标地图准备**：记录当前选中的大地图，与目标不同时先切换；一次拖动可达时不缩放、不切换国家；当前位置比最近国家中心远 1000 以上时切换国家；仍不可达时缩小到最小地图；当前位置识别两次都失败时直接切换到目标国家。
- **安全区拖动**：地图界面按钮区域与至冬底部按钮区域为危险区；按“能否一次拖完、余量大小”排序起点，从前 30% 中随机选；起点避开传送锚点、七天神像、秘境图标；拖动无效时换起点重试（最多 3 次），局部没有可用起点时全屏探测一次；拖动后识别失败时禁用该起点；直线分步拖动；还原 Windows 显示缩放，乘以理论拖动距离修正倍率；用回读的实际光标位移推算地图移动。
- **黑图处理**：拖动后画面平均亮度低于 50（旧日之海低于 32）时沿原跑道拖回；拖回后仍然过暗时跳到离目标最近的国家，在同一轮继续移动。
- **提前结束拖动、可点击区域**：目标离边缘和按钮区域都超过 40 像素时结束本轮移动；判断能否点击、点击校正后是否安全时使用与拖动相同的按钮区域，并按目标所在国家处理至冬的底部按钮。
- **滑块缩放**：拖动左侧缩放滑块代替滚轮。
- **识别缓存**：一次操作内缓存大地图范围和缩放等级。
- **点击校正的图标数量上限**：参与绝对坐标对齐的图标预期 24 个、识别 48 个、平移假设 16 个。
- **随倍率缩放的等待**：传送面板等待、候选列表点击前等待（200 ms × 倍率）、点击区域重试、重试间隔、地表图层切换和区域选择的等待。
- **超时估算**：每次尝试按打开地图超时、10 次状态切换、10 次轮询前等待、10 次操作间隔和 5 秒余量计，乘以 3 次尝试，不少于 60 秒。
- **脚本的大地图移动**：PR 让 `genshin.moveMapTo`、`genshin.clickMapPoint`、`genshin.moveIndependentMapTo` 在实验模式下经状态机打开地图，再用“目标移到画面中心”的方式移动。主程序的这三个方法依次调用 `CheckInBigMapUi`、`SwitchRecentlyCountryMap`（或 `SwitchArea`）、`MoveMapTo`/`ClickMapPoint`，插件中这几个方法分别是状态机打开地图和居中移动，效果与 PR 相同，主程序的 `Genshin.cs` 不需要修改。
- **全部实验参数**：高级参数开关、理论拖动距离修正倍率、单步最大输入距离、拖动步进间隔、状态识别轮询间隔、轮询开始前等待、状态切换等待上限、打开地图总超时、补按 M 间隔、按下左键后延迟、松开左键前延迟、详细调试日志。不启用高级参数时按 PR 的基准值和下限随倍率缩放。

## 没有迁移的功能

- **`Navigation.GetTpPriorPosition`**：PR 在路径追踪中记录传送前的小地图位置，实验传送识别大地图中心时先用它作为先验（`TryGetTeleportPriorMoveMapState`）。这需要修改主程序的 `Navigation`，插件删除了这一步，直接走常规识别。只影响识别速度。
- **主程序配置与设置页**：PR 在 `TpConfig` 中新增“启用实验性传送”开关和全部实验参数，把“传送操作速度”从毫秒改为倍率并迁移旧配置，并修改了设置页。插件用“功能选择”代替开关，参数放在插件设置窗口；不改主程序配置，倍率按 `传送操作速度 / 20 ms` 换算（默认为 1）。
- **内置传送的改动**：PR 同时修改了非实验模式下的流程，例如内置传送也使用区域模板匹配，打开大地图、切换地表图层、切换区域、等待传送面板的超时与轮询方式。主程序的 `TpTask` 保持原样，选“内置传送”时没有这些改动；插件中只保留了实验模式用到的部分。
- **`Genshin.cs` 中的 `RunExperimentalMapOperation`**：见上文“脚本的大地图移动”，插件不需要它。
- **`SwitchAreaMenuGridLayout`**：PR 中区域列表 2×8 网格的坐标标定，生产代码没有使用。

## 与 PR 的行为差异

- **其他任务直接调用的方法也使用实验行为**：PR 中只有 `Tp` 和脚本的三个地图移动方法会启用状态机，其他任务直接调用 `OpenBigMapUi`、`SwitchArea`、`AdjustMapZoomLevel` 时没有活动的状态机，走内置逻辑。插件固定使用实验模式，这些调用也改用状态机和滑块缩放，涉及：路径追踪打开大地图、地脉花打开地图与缩放、尘歌壶切换区域、领取奖励和体力计划打开大地图、脚本的 `genshin.setBigMapZoomLevel`。
- **`MoveMapTo`、`ClickMapPoint` 固定为居中版本**：主程序中只有脚本调用它们，PR 中脚本在实验模式下调用的也是居中版本，所以插件不再保留非居中版本。
- **识别缓存的清空时机**：PR 的缓存只在一次实验操作内有效。插件的实例可能被一个任务长期持有（例如地脉花），所以在公开的 `OpenBigMapUi`、`CheckInBigMapUi`、`MoveMapTo`、`GetBigMapRect` 开始时，以及切换区域、缩放之后都会清空缓存。
- **“当前选中的大地图”**：PR 中这个静态字段在主程序里，内置传送也会更新。插件有自己的静态字段，只由实验传送更新；切换到插件后的第一次传送会先切换一次地图或国家。
- **`AdjustMapZoomLevel` 的当前缩放等级无效时**：重新截图识别后再拖动滑块。

## 维护

`ExperimentalTpTask` 是一份副本，主程序 `TpTask` 修改后不会自动同步，需要手动把相关改动同步到这里。`ITpTask` 增加方法时插件必须实现，否则第一次创建传送任务时抛出 `TypeLoadException`，插件按二进制不兼容停用。

## 素材

`Assets/1920x1080/` 下的图片来自 PR #3697，构建时按相对路径放进插件目录。低于 1080P 时按宽度缩小后使用。
