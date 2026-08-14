# AirCOM

> 把另一台电脑上的串口设备，映射成本机的串口。
>
> A 电脑通过 AirCOM 使用 B 电脑上接的串口设备，任意串口软件打开本机虚拟口即可收发数据。

```
串口软件 ──COM10── com0com ──COM11── A端服务 ──网络── B端服务 ──COMx── 真实串口设备
   (A 电脑)                                              (B 电脑)
```

## 这是什么

AirCOM 解决一个简单的问题：**串口设备插在 B 电脑上，但我想在 A 电脑上用它。**

比如：B 电脑接了个串口传感器、单片机、工业设备，你不想搬设备，也不想在 A 上装驱动连线。用 AirCOM，B 电脑运行软件共享这个串口，A 电脑运行软件后，串口设备就像插在 A 上一樣——A 电脑上任意串口软件（串口助手、你的程序、PuTTY）打开一个虚拟 COM 口就能收发数据。

典型用途：远程调试串口设备、共享一台串口仪器、跨电脑使用串口外设。

## 特性

- **单程序双角色**：一个 `AirCOM.exe`，启动选"使用远程串口(A端)"或"共享本地串口(B端)"。
- **绿色免安装**：解压即用，自带 .NET 运行时，目标机器无需装任何运行时。
- **com0com 自动管理**：A 端首次用自动装虚拟串口驱动（带商业签名，无需测试签名、无需重启、无桌面水印），自动选空闲 COM 端口对，配置持久化。
- **任意串口软件通用**：A 端出现的是标准 COM 口，串口助手、PuTTY、你的程序都能直接打开。
- **透传数据与参数**：串口数据双向透传，波特率/数据位/校验/停止位随参数帧同步到对端真实串口。
- **断开/重连健壮**：对端关闭、网络中断自动检测，B 端可持续监听多次连接。

## 当前状态

✅ **局域网完善版**（`lan-v2`，v0.1.21）：两台同局域网 Windows 电脑，串口设备远程映射，双向透传，真机长时间稳定（含 Modbus RTU 兼容）。

⏳ **跨网络**（待实现）：A 在家、B 在公司这种跨 NAT 场景，需要后续的信令服务器 + P2P 打洞/中继。

## 下载使用

### 1. 下载

从 [Releases](../../releases) 页面下载最新版 `AirCOM-vX.X.X.zip`（约 64 MB）。解压到任意目录。

### 2. 两台电脑的部署

| 角色 | 在哪台 | 装什么 |
|---|---|---|
| **B 端**（接了串口设备的那台） | B 电脑 | 解压即可，纯绿色 |
| **A 端**（要用串口的那台） | A 电脑 | 解压即可，首次用会自动装 com0com 驱动（弹一次 UAC 提权） |

### 3. B 端配置（提供串口）

在 B 电脑双击 `AirCOM.exe`：
1. 选 **"作为 B 端运行"**。
2. "共享串口"下拉选你的串口设备（如 COM4，USB-TTL 模块）。
3. "监听端口"填一个空闲 TCP 端口（默认 51000，可改）。
4. "波特率"填你的设备要求的值（如 115200）。**这是唯一需要设对的波特率**（它配置 B 端真实串口）。
5. 点 **"启动监听"**。状态显示"监听中"。

### 4. A 端配置（使用远程串口）

在 A 电脑双击 `AirCOM.exe`：
1. 选 **"作为 A 端运行"**。
2. "B 端 IP"填 B 电脑的局域网 IP（在 B 电脑 `ipconfig` 查，如 `192.168.1.50`）。
3. "端口"填 B 端监听的端口（默认 51000，要和 B 端一致）。
4. 点 **"连接"**。首次会弹 UAC，点"是"装驱动（只一次，以后不用）。连接成功后界面显示分配的虚拟口（如 `COM50`）和"跟随 B 端"的实际波特率。
5. 打开你的串口软件，选 **COM50**（界面提示的那个号），波特率随意（建议与设备一致），即可收发数据。

### 5. 直连场景（无路由器）

如果 A、B 之间是网线直连（没有路由器），需手动设固定 IP：
- A 电脑：`192.168.137.1`，子网掩码 `255.255.255.0`
- B 电脑：`192.168.137.2`，子网掩码 `255.255.255.0`

设好后 A 端填 `192.168.137.2` 连接。（设 IP：`Win+R` → `ncpa.cpl` → 网卡右键属性 → IPv4 属性。）

## 常见问题

**Q：连接不上？**
- 检查 B 端是否已点"启动监听"。
- 检查 A 端填的 IP 是否正确（`ipconfig` 查 B 的 IP）。
- 检查 B 电脑防火墙是否放行（首次启动监听会弹窗，点"允许访问"）。
- A 电脑 `ping B的IP` 看网络是否通。
- 提示"串口 COMx 被占用"：该串口被其他程序占着，关掉占用它的程序。
- 提示"监听端口已占用"：换个端口，或关掉占用该端口的程序。

**Q：A 端首次连接弹 UAC 是什么？**
A 端需要 com0com 虚拟串口驱动。首次用会自动安装，弹一次管理员权限确认。装完即用，无需重启、无桌面水印（驱动带商业签名）。以后再连接不会弹。

**Q：A 端显示的虚拟口号（COM50）是固定的吗？**
首次自动分配空闲的一对（如 COM50↔COM51），存到 `%AppData%\AirCOM\aircom-settings.json`，以后沿用。串口软件固定开那个号即可。如果该号被占用，会自动找下一对空闲的。

**Q：B 端关了 A 端会怎样？**
A 端几秒内检测到断开，显示"已断开（可重新连接）"。A 端重新点"连接"即可（B 端要先重新启动监听）。

**Q：能跨网络用吗（A 在家、B 在公司）？**
当前版本仅限同局域网。跨网络是后续计划（信令服务器 + P2P 打洞）。

**Q：支持什么系统？**
Windows 10/11 64 位。

## 从源码构建

需要 .NET 8 SDK。

```bash
git clone https://github.com/CORANGEY/AirCOM.git
cd AirCOM
dotnet build AirCOM.sln
dotnet test tests/AirCOM.Core.Tests   # 40 项单元测试
```

**一键发布绿色包**（生成 `publish/v<版本>/AirCOM-v<版本>.zip`）：
```powershell
.\tools\publish.ps1              # 当前版本
.\tools\publish.ps1 -BumpMinor   # 次版本号 +1
.\tools\publish.ps1 -BumpPatch   # 修订号 +1
```

## 项目结构

```
AirCOM/
├── src/
│   ├── AirCOM.App/          单程序入口（角色选择 + A/B 双角色统一界面）
│   ├── AirCOM.Client.A/    A 端逻辑（com0com 自动管理、AEngineHost）
│   ├── AirCOM.Client.B/    B 端逻辑（真实串口管理、BEngineHost）
│   ├── AirCOM.Core/        共享核心（协议、传输、串口抽象、桥接引擎）
│   ├── AirCOM.EchoB/       调试用：echo B 端
│   └── AirCOM.Server/      信令+中继服务器（跨网络，待实现）
├── tests/                   单元测试 + 端到端回环测试
├── tools/com0com/          com0com 安装包 + 驱动修复脚本
├── tools/publish.ps1       一键版本化发布脚本
├── CHANGELOG.md            更新日志
└── Directory.Build.props   全局版本号（当前 0.1.21）
```

## 架构与协议

- **协议**：自定义二进制帧（15 字节头 + 变长 payload + CRC16），借鉴 RFC 2217，承载串口数据 + 参数设置 + 控制线状态。12 种消息类型。
- **传输**：基于 `System.IO.Pipelines` 的高性能帧编解码，TCP keep-alive 检测死连接。
- **桥接**：`SerialBridge` 双向泵，串口 ↔ 网络数据搬运；任一端 EOF 立即触发停止。
- **虚拟驱动**：com0com 内核驱动（CyberCircuits 签名），A 端自动安装管理。

详见 `docs/` 目录。

## 开发路线

- ✅ M0 核心 + 协议
- ✅ M1 局域网直连
- ✅ M5 单程序 + com0com 自动管理
- ⏳ M2 跨网络（信令 + 中继）
- ⏳ M3 P2P 打洞
- ⏳ M4 端到端加密

## 许可

本项目自身代码以 [GPL-2.0-or-later](LICENSE) 发布（与 com0com 一致）。

发布包中捆绑分发的第三方组件：

| 组件 | 协议 | 说明 |
|---|---|---|
| [com0com](https://com0com.sourceforge.net/) 虚拟串口驱动（`com0com-setup-x64.exe`） | GPL-2.0-or-later | 原样独立分发，源码见其官方站点 |
| .NET 8 运行时 | MIT | 微软官方发布 |
| [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet)、[H.NotifyIcon](https://github.com/HavenDV/H.NotifyIcon) | MIT | NuGet 依赖 |

AirCOM 未包含 com0com 的任何源代码（AirCOM 为 C#/.NET，com0com 为 C 内核驱动，二者通过进程调用 `setupc.exe` 交互，构成独立作品关系；分发其安装包遵守 GPL 分发条款）。

## 致谢

- [com0com](https://com0com.sourceforge.net/) - 开源虚拟串口驱动，Vyacheslav Frolov / CyberCircuits
- [.NET 8](https://dotnet.microsoft.com/) / [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet)
