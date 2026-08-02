# 酷狗音乐概念版模块 (Kugou)

酷狗音乐概念版集成模块，通过 Go 原生桥接 DLL 调用酷狗概念版接口，支持二维码登录、账号歌单同步和标准音质流式播放。

## 功能特性

- **二维码登录** — 使用酷狗音乐概念版 App 扫码登录，会话自动持久化
- **账号歌单** — 自动加载账号创建和收藏的歌单，并作为只读歌单导入音乐库
- **原始顺序** — 根据远端 `sort` 字段和分页抓取顺序恢复歌曲排列
- **流式播放** — 模块提供临时播放 URL，后端负责下载、缓存和解码
- **标准音质** — 固定使用 128 kbps MP3，提高歌曲兼容性和可播放率
- **每日 VIP** — 登录后自动尝试领取并升级当天的 VIP 试听权益
- **封面显示** — 自动下载歌曲封面并进行内存缓存
- **歌词显示** — 自动搜索和加载 LRC 歌词
- **安全验证** — 播放触发酷狗风控时提供本机安全验证页面
- **请求缓存** — 歌单接口使用内存和磁盘缓存，网络异常时可回退到短期旧数据

## 架构

```text
┌──────────────────────────────────────────┐
│              OmniMixPlayer.SDK           │
│  IMusicModule / IStreamingMusicSource... │
└──────────────────┬───────────────────────┘
                   │
┌──────────────────▼───────────────────────┐
│           C# 模块层 (KugouModule)        │
│  命名空间: OmniMixPlayer.Module.Kugou    │
│  实现: IStreamingMusicSourceProvider     │
│        (提供可播放 URL，后端下载+解码),   │
│        ICoverProvider, ILyricProvider,   │
│        IModuleUIProvider                 │
└──────────────────┬───────────────────────┘
                   │ P/Invoke (cdecl)
┌──────────────────▼───────────────────────┐
│          Go 网桥层 (kugou_bridge)        │
│  NativePlugins/kugou_bridge/             │
│  CGO → c-shared DLL                     │
│  设备、会话、Cookie、签名、缓存和风控验证 │
└──────────────────┬───────────────────────┘
                   │ HTTPS
┌──────────────────▼───────────────────────┐
│             酷狗音乐服务器               │
│ gateway.kugou.com / lyrics.kugou.com    │
└──────────────────────────────────────────┘
```

## 编译

### 环境要求

- **Go** 1.21+（需要 CGO 和 x64 GCC/Clang）
- **.NET 10 SDK**
- **Windows x64**

### 编译步骤

```batch
# 1. 编译 Go 网桥 DLL
cd NativePlugins\kugou_bridge
build.bat

# 2. 编译 C# 模块
cd OmniMixPlayer\modules\Kugou
dotnet restore
dotnet build -c Release
```

## 文件结构

```text
OmniMixPlayer/modules/Kugou/
├── KugouModule.cs              # 主模块入口、播放解析和歌单注册
├── ModuleInfo.cs               # 模块 ID/名称/版本常量
├── KugouBridge.cs              # P/Invoke Go DLL 桥接
├── KugouCoverLoader.cs         # 歌曲封面下载和缓存
├── QRLoginManager.cs           # 概念版二维码登录流程
└── native/x64/
    └── ChillKugou.dll          # Go 编译产物

NativePlugins/kugou_bridge/
├── main.go                     # C 导出函数入口
├── api/
│   ├── client.go               # 酷狗 API 客户端和通用请求
│   ├── device_registration.go  # V2 设备注册
│   ├── verification.go         # 账号安全验证
│   ├── cookies.go              # Cookie 持久化
│   ├── cache.go                # HTTP 响应缓存
│   ├── storage.go              # 设备和会话存储
│   ├── sign.go                 # 请求签名
│   └── types.go                # 数据结构和错误类型
├── build.bat                   # Windows x64 编译脚本
└── ChillKugou.h                # C 头文件
```

## 登录数据

登录和设备数据保存在模块数据目录：

```text
<播放器目录>/modules/com.chillpatcher.kugou/data/
├── kugou_device.json           # DFID、MID、UUID 和设备注册状态
├── kugou_sessions.json         # 活跃账号、Token 和用户信息
├── kugou_cookies.json          # 持久化 Cookie
└── http_cache/                 # 歌单分页响应缓存
```

如需完全重置登录和设备身份，请先关闭 Backend，再删除整个模块数据目录。仅退出当前账号可直接使用模块页面中的“退出登录”。

## 配置

模块通过 `IModuleConfigManager` 保存每日 VIP 状态，无需手动编辑：

- `VipClaimDate` — 最近处理 VIP 权益的日期
- `VipClaimReceived` — 当天是否已领取
- `VipClaimUpgraded` — 当天奖励是否已升级

播放音质固定为标准 128 kbps，当前没有可调整的音质配置，部分歌曲因为vip的原因，高音质导致无法播放。歌单全部按只读来源处理，不会修改云端内容。

## 工作流程

1. 模块加载 `ChillKugou.dll`，恢复持久设备、Cookie 和登录会话。
2. 未登录时生成二维码并每 1.5 秒轮询；登录成功后保存会话。
3. 已登录时先检查每日 VIP，再分页拉取账号歌单和歌曲。
4. 模块通过 `ILibraryRegistry` 注册 Track 和 Playlist，并用 `ReplacePlaylistEntries` 保存歌曲位置。
5. 播放时根据 UUID 查询标准音质 hash 和临时 URL，再交给 Backend 下载和解码。
6. 如果接口返回安全验证错误，模块页面显示本机验证入口，完成验证后可重新检查播放地址。

## 实现的 SDK 接口

| 接口 | 说明 |
| --- | --- |
| `IMusicModule` | 基础模块入口和生命周期 |
| `IStreamingMusicSourceProvider` | 流媒体音源（提供 URL，后端负责下载和解码） |
| `ICoverProvider` | 歌曲封面；当前不提供 Album 封面 |
| `ILyricProvider` | LRC 歌词 |
| `IModuleUIProvider` | 二维码、歌单刷新、VIP 状态和安全验证页面 |

当前模块没有实现 `IFavoriteExcludeHandler` 或 `IDeleteHandler`，因此不支持收藏写回、排除和删除。所有酷狗账号歌单均为只读映射。

## 灵感来源
[MakcRe/KuGouMusicApi](https://github.com/MakcRe/KuGouMusicApi)

## 许可证

本项目仅供学习研究使用。请遵守酷狗音乐服务条款和当地版权法律。
