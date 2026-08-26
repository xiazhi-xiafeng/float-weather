# float-weather · 悬浮天气

> Windows 桌面悬浮天气小组件 —— 聚合 7 大天气数据源，主备切换 + 故障自动降级，常驻桌面、置顶半透明。
>
> 当前版本：**v0.1.2**（分支 `v0.1.2`）

---

## 特性

- ⚡ **常驻悬浮小组件**：置顶、半透明、可拖拽、位置记忆，单行 `图标 | 温度 | 天气 | 城市`，一目了然。
- 🪟 **悬浮交互增强**：右键菜单（与托盘同构）、双击整卡打开详情（防重）、拖拽贴边吸附（阈值可配）、低透明度下鼠标 hover 自动显形。
- 🐭 **鼠标穿透模式**：一键穿透并可继续操作桌面，穿透开启时鼠标掠过悬浮窗给出托盘解除提示，避免"找不到"。
- 🌗 **桌面直显（Bare）**：低亮度窗口直透桌面并自动抓取背景色，文字颜色随桌面自适应（采样取窗外像素 + 事件驱动 + 10s 低频兜底，深/浅背景均可靠切换）。
- 🖥️ **显示器健壮性**：显示器热插拔/分辨率变更时窗口自动复位不丢失，避免掉出虚拟屏。
- 🗂️ **详情窗口**：逐时预报、多日预报、空气质量、风力、湿度、生活指数，楼层式布局。
- 📈 **温度趋势曲线**：详情窗展示「过去 24h 观察曲线」与「未来 7 日高低温温差带」，逐点温度标注、高低温线最小间距防重叠。
- 🔀 **多源聚合 + 智能降级**：7 个数据源按优先级主备切换，单源超时/失败自动降级到下一源；连续失败进入熔断（60s）避免反复请求。
- 🛡️ **可靠性**：单请求 5s 超时、失败计数与熔断、内存缓存（断源时展示「上次数据」）、请求版本号防帧竞态。
- 🎨 **动态主题**：随天气类型与昼夜自动切换背景氛围色。
- 🧩 **系统集成**：系统托盘常驻（悬停显示实时天气摘要）、开机自启（路径漂移自动修复）、单实例（命名 Mutex）、定时自动刷新（防重入）。
- 🔑 **免 Key 运行**：内置 Open-Meteo / wttr.in / 中国天气网三个免 Key 源作为兜底，未配置任何 Key 也能取数。

---

## 数据源

| 来源 | 类型 | 鉴权 | 覆盖 |
|------|------|:---:|------|
| [和风天气 QWeather](https://www.qweather.com/) | 官方开放接口 | JWT / Ed25519 | 实时 + 24h 逐时 + 7 日 + 空气质量 + 生活指数 |
| [高德天气 Amap](https://lbs.amap.com/) | 官方开放接口 | Web 服务 Key | 全国城市实时 + 3 日 |
| [心知天气 Seniverse](https://www.seniverse.com/) | 官方开放接口 | API Key | 实时 + 逐日 + 逐时 |
| [OpenWeather](https://openweathermap.org/) | 官方开放接口 | API Key | 全球实时 + 预报 |
| [Open-Meteo](https://open-meteo.com/) | 免费开放接口 | 无 | 全球实时 + 预报（坐标） |
| [wttr.in](https://wttr.in/) | 免费开放接口 | 无 | 全球实时 + 逐时 + 逐日（中文地名直查） |
| [中国天气网 ChinaWeather](http://www.weather.com.cn/) | 免费开放接口 | 无 | 实时 + 逐时 + 逐日（末位兜底） |

> 默认优先级：和风 > 高德 > 心知 > OpenWeather > Open-Meteo > wttr.in > 中国天气网。
> 和风字段最全、免费额度充足，作为默认主源；Open-Meteo / wttr.in 免 Key 兜底保证任何 Key 未配也可取数。

---

## 技术栈

- **语言 / 框架**：C# 12 · .NET 8 · WPF（`net8.0-windows`）
- **UI 绑定**：`CommunityToolkit.Mvvm`
- **数据访问**：`HttpClient` + `IHttpClientFactory` + `System.Text.Json`（源适配器模式）
- **DI / 配置**：`Microsoft.Extensions.DependencyInjection` + `Microsoft.Extensions.Configuration.Json`
- **日志**：`Serilog`（滚动文件，保留 7 天）
- **加密**：`Portable.BouncyCastle`（和风 JWT Ed25519 签发）
- **图标**：和风 `qweather-icons` 图标字体 + emoji 兜底

---

## 快速开始

### 环境要求

- Windows 10 / 11（需桌面环境）
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

### 构建与运行

```bash
git clone https://github.com/xiazhi-xiafeng/float-weather.git
cd float-weather/src/FloatWeather
dotnet run
```

或生成发布产物：

```bash
dotnet publish -c Release -r win-x64
```

### 配置

复制 `appsettings.json` 结构并在各数据源开放平台申请对应 Key：

```json
{
  "Weather": {
    "RefreshIntervalSeconds": 1800,
    "CityName": "北京",
    "PrimaryProvider": "中国天气网",
    "FallbackProvider": "Open-Meteo"
  },
  "Providers": {
    "QWeather":   { "ProjectId": "", "CredentialId": "", "PrivateKey": "", "ApiHost": "" },
    "Amap":       { "Key": "" },
    "OpenWeather":{ "Key": "" },
    "Seniverse":  { "Key": "" }
  }
}
```

- 无需任何 Key 也可运行（自动走免 Key 兜底源）。
- 用户在「设置」中填写的 Key 会写入本地 `user-config.json`，**该文件已加入 `.gitignore`，不会入库**。
- 和风需自行申请开发者账号并配置 `ProjectId / CredentialId / Ed25519 PrivateKey / ApiHost`，应用会据此动态签发 JWT。

---

## 项目结构

```
float-weather/
├─ README.md
├─ 技术方案.md                 # 详细技术设计与架构文档
├─ src/
│  └─ FloatWeather/            # 主程序 (WPF, net8.0-windows)
│     ├─ App.xaml(.cs)         # 启动、DI、单实例 Mutex、全局异常兜底
│     ├─ MainWindow.xaml(.cs)  # 详情主窗口
│     ├─ Converters/           # 值转换器
│     ├─ Models/Dto/           # 统一模型 + 各源私有响应模型
│     ├─ Providers/            # IWeatherProvider + 7 源实现
│     ├─ Services/             # 业务/降级/城市解析/配置/图标/JWT 等
│     ├─ Theme/                # 动态天气配色
│     ├─ ViewModels/           # MVVM 视图模型
│     ├─ Views/                # 悬浮/设置/源弹窗窗口
│     ├─ Resources/            # 图标资源
│     └─ appsettings.json      # 配置（含各源 Key 占位）
└─ tools/
   └─ ProviderTester/          # 多源 × 多地名取数回归测试
```

---

## 如何扩展新数据源

实现 `IWeatherProvider` 接口（适配器模式），接入 `SourceManager` 的源列表即可，无需改动调用方：

```csharp
public interface IWeatherProvider
{
    string Name { get; }
    Task<WeatherResult> GetWeatherAsync(string cityName, CancellationToken ct = default);
}
```

城市 / 坐标解析由 `CityResolver` 提供强类型方法，各源直接复用（区县优先、同名打分去歧义）。

---

## 致谢

- 各天气数据源开放平台
- 开源依赖：.NET / WPF / CommunityToolkit.Mvvm / Serilog / Portable.BouncyCastle 等

*本项目为个人学习 / 自用项目，请勿将其用于商业用途或对数据源造成高频请求压力。*

---

## 版本记录

### v0.1.2（当前）

悬浮窗与详情窗的显示修复，以及打包、健壮性、趋势与集成的新一轮增强：

**显示修复**
- 修复悬浮窗状态行文字被裁切：状态仅保留「数据源 + 更新时间」关键信息，并加自动省略号兜底，`中国天气网 · 更新于 09:59` 完整显示
- 修复详情窗大号温度显示不全：`37.8°` 末位数字与 `°` 符号不再丢失（温度独占一行、优化字号与图标/明细列宽）
- 修复桌面直显深背景看不清：亮度采样面片改取窗外像素（规避窗体自身半透明污染导致"亮背景误判"），并增加 10s 低频兜底采样，背景被其他窗口遮挡（无拖动事件）也能及时切换文字颜色

**打包与工程化**
- 应用版本号 0.1.2.0（AssemblyVersion/FileVersion），配置单文件自包含发布与应用图标
- 开机自启路径修复：EXE 路径变更/失效时自动重建注册表自启项

**健壮性与代码质量**
- 边缘吸附改按显示器工作区钳位：各屏四边不越界、不被任务栏遮挡
- `Ago()` 相对时间收敛到 `TimeText` 工具类；XAML 魔法值收敛到 `App.xaml` 资源
- 设置页数据源运行状态面板每 5 秒自动刷新，无需手动刷新

**新功能**
- 温度趋势曲线：详情窗「过去 24h」观察曲线 + 「未来 7 日」高低温温差带（高低线最小间距、逐点温度标注、点密自动退化为整体角标）
- 托盘悬停动态天气摘要：托盘 Tooltip 实时显示 `城市 温度 天气 · 湿度 · 更新时间`

### v0.1.1

悬浮窗交互与健壮性增强：

- 悬浮窗口增强：右键菜单（与托盘同构）、双击整卡打开详情（防重）
- 边缘吸附：拖拽贴近屏幕边缘自动贴边，吸附距离可在设置页配置（0–60px，0=关闭）
- 鼠标穿透模式 + 穿透泄漏提示（托盘气泡提醒，避免"找不到"窗口）
- 低透明度 hover 显形：透明度低于阈值时鼠标移入临时提亮、移出恢复
- 桌面直显（Bare）：亮度采样事件化（仅拖动/显形时取色），文字颜色随桌面自适应
- 显示器热插拔兜底：DisplaySettingsChanged 时窗口自动复位不丢失
- 逐时浮层延迟关闭（250ms 防抖）、温度转场动画、裸屏浮层描边

### v0.1.0

首个可用版本，功能覆盖：

- 悬浮小组件 + 详情窗口 + 设置窗口 + 系统托盘 + 开机自启 + 单实例
- 聚合 7 大天气数据源（和风 / 高德 / 心知 / OpenWeather / Open-Meteo / wttr.in / 中国天气网）
- 主备切换、故障熔断降级、5s 超时、内存缓存、动态主题