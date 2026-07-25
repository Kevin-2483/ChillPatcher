# -*- coding: utf-8 -*-
"""
构建配置 - 所有路径和工具链设置

路径优先级: build_config.json > 环境变量 > 常见安装位置 > PATH 自动查找
  MINGW_ROOT  — MinGW 安装根目录
  FLUTTER_ROOT — Flutter SDK 根目录
  VS_INSTALL   — Visual Studio 安装根目录
  INNOSETUP    — Inno Setup 安装目录 (installer 任务用)

配置文件: scripts/build_config.json (不存在时自动创建含默认值)
"""
import json
import os
import subprocess
import shutil
import winreg
from pathlib import Path

# ── 根目录 ──
ROOT = Path(__file__).resolve().parent.parent
CONFIG_FILE = Path(__file__).resolve().parent / "build_config.json"

# ════════════════════════════════════════════
#  配置文件加载 / 保存
# ════════════════════════════════════════════

DEFAULT_CONFIG = {
    "_comment": "本地构建路径配置。留空 = 自动发现。编辑后保存即可生效。",
    "_help": {
        "steam_library": "Steam 库根目录 (如 D:\\SteamLibrary)。构建 ChillPatcher 时需要从此路径引用游戏 DLL。",
        "toolchain.mingw_root": "MinGW 安装目录 (如 G:/mingw64)。留空自动从 PATH 发现。",
        "toolchain.flutter_root": "Flutter SDK 目录 (如 G:/flutter_SDK/flutter)。留空自动从 PATH 发现。",
        "toolchain.vs_install": "Visual Studio 安装目录。留空自动通过 vswhere 发现。",
        "toolchain.go_root": "Go 安装目录。留空自动从 PATH 发现。",
        "toolchain.node_root": "Node.js 安装目录。留空自动从 PATH 发现。",
    },
    "steam_library": "D:\\SteamLibrary",
    "toolchain": {
        "mingw_root": "",
        "flutter_root": "",
        "vs_install": "",
        "go_root": "",
        "node_root": "",
    },
}

_config_cache: dict | None = None


def load_config() -> dict:
    """加载 build_config.json，不存在则创建默认。幂等，结果缓存。"""
    global _config_cache
    if _config_cache is not None:
        return _config_cache
    if CONFIG_FILE.exists():
        try:
            _config_cache = json.loads(CONFIG_FILE.read_text(encoding="utf-8"))
            # 补齐缺失的顶层键，并更新注释/帮助文本
            for k, v in DEFAULT_CONFIG.items():
                if k not in _config_cache:
                    _config_cache[k] = v
                elif isinstance(v, dict) and isinstance(_config_cache.get(k), dict):
                    for sk, sv in v.items():
                        if sk not in _config_cache[k]:
                            _config_cache[k][sk] = sv
            # 始终用最新的 _comment / _help
            for doc_key in ("_comment", "_help"):
                _config_cache[doc_key] = DEFAULT_CONFIG[doc_key]
            save_config(_config_cache)
            return _config_cache
        except Exception:
            pass
    _config_cache = json.loads(json.dumps(DEFAULT_CONFIG))  # deep copy
    save_config(_config_cache)
    return _config_cache


def save_config(cfg: dict | None = None):
    """保存配置到 build_config.json。"""
    if cfg is None:
        cfg = _config_cache or load_config()
    CONFIG_FILE.parent.mkdir(parents=True, exist_ok=True)
    CONFIG_FILE.write_text(
        json.dumps(cfg, indent=2, ensure_ascii=False),
        encoding="utf-8",
    )


def _find_steam_from_registry() -> str:
    """从 Windows 注册表读取 Steam 安装路径。"""
    for hive, key_path in [
        (winreg.HKEY_CURRENT_USER, r"Software\Valve\Steam"),
        (winreg.HKEY_LOCAL_MACHINE, r"Software\Valve\Steam"),
        (winreg.HKEY_LOCAL_MACHINE, r"Software\WOW6432Node\Valve\Steam"),
    ]:
        try:
            with winreg.OpenKey(hive, key_path) as key:
                steam_path, _ = winreg.QueryValueEx(key, "SteamPath")
                p = Path(steam_path)
                if p.is_dir():
                    return str(p)
        except OSError:
            continue
    return ""


def _find_steam_library() -> str:
    """自动发现 Steam 库路径。返回首个包含目标游戏的库路径，否则返回 Steam 安装路径下的 steamapps。"""
    # 1) 尝试从注册表找到 Steam 安装路径
    steam_install = _find_steam_from_registry()
    # 2) 读取 libraryfolders.vdf 找实际库目录
    candidates = []
    if steam_install:
        vdf = Path(steam_install) / "steamapps" / "libraryfolders.vdf"
        if vdf.exists():
            try:
                text = vdf.read_text(encoding="utf-8", errors="replace")
                import re
                for m in re.finditer(r'"path"\s+"([^"]+)"', text):
                    lib = m.group(1).replace("\\\\", "\\")
                    p = Path(lib)
                    if p.is_dir():
                        candidates.append(p)
            except Exception:
                pass
        # Steam 安装目录本身的 steamapps 也加入
        default_lib = Path(steam_install) / "steamapps"
        if default_lib.is_dir() and default_lib not in candidates:
            candidates.append(default_lib)

    # 3) 候选：如果配置中的 steam_library 有效则也用
    cfg = load_config()
    cfg_lib = cfg.get("steam_library", "")
    if cfg_lib:
        p = Path(cfg_lib)
        if p.is_dir() and p not in candidates:
            candidates.append(p)

    # # 4) 检查哪个库包含目标游戏
    # target = "Chill with You Lo-Fi Story"
    # for lib in candidates:
    #     game_dir = lib / "common" / target
    #     if game_dir.is_dir():
    #         return str(lib)
    target = "Chill with You Lo-Fi Story"
    for lib in candidates:
        # VDF "path" 指向 Steam 安装根，游戏在 {path}/steamapps/common/ 下
        game_dir = lib / "steamapps" / "common" / target
        if game_dir.is_dir():
            return str(lib)

    # 5) 回退到配置值
    if cfg_lib:
        return cfg_lib
    # 6) 绝对回退
    return DEFAULT_CONFIG["steam_library"]


# ════════════════════════════════════════════
#  工具链 — 全部自动发现, 不再硬编码
# ════════════════════════════════════════════

def _find_in_path(name: str) -> Path | None:
    """在 PATH 中查找可执行文件。"""
    exe = name if name.endswith(".exe") else f"{name}.exe"
    p = shutil.which(name) or shutil.which(exe)
    return Path(p) if p else None

def _find_vswhere() -> Path | None:
    """查找 vswhere.exe (Visual Studio 定位器)。"""
    # 优先使用 PATH 中的
    p = _find_in_path("vswhere")
    if p: return p
    # 标准安装位置
    prog = os.environ.get("ProgramFiles(x86)", "C:/Program Files (x86)")
    p = Path(prog) / "Microsoft Visual Studio" / "Installer" / "vswhere.exe"
    return p if p.is_file() else None

def _find_vs_install_dir() -> Path | None:
    """自动发现 VS 安装目录 (vswhere → 环境变量 → 常见路径)。"""
    # 环境变量
    for env in ["VS_INSTALL", "VSINSTALLDIR", "VS170COMNTOOLS", "VS180COMNTOOLS"]:
        v = os.environ.get(env, "")
        if v and Path(v).is_dir():
            return Path(v)

    # vswhere
    vs = _find_vswhere()
    if vs:
        try:
            result = subprocess.run(
                [str(vs), "-latest", "-property", "installationPath"],
                capture_output=True, text=True, timeout=10,
            )
            p = result.stdout.strip()
            if p and Path(p).is_dir():
                return Path(p)
        except Exception:
            pass

    # 常见路径
    for base in [os.environ.get("ProgramFiles", "C:/Program Files"),
                 "D:/Program Files", "C:/Program Files (x86)", "D:/Program Files (x86)"]:
        for ver in ["Microsoft Visual Studio/2022", "Microsoft Visual Studio/18",
                     "Microsoft Visual Studio/17", "Microsoft Visual Studio/16"]:
            for ed in ["Community", "Professional", "Enterprise", "BuildTools"]:
                p = Path(base) / ver / ed
                if p.is_dir():
                    return p
    return None

def _find_cmake_dir() -> Path | None:
    """查找 CMake 安装目录。"""
    exe = _find_in_path("cmake")
    if exe:
        return exe.parent.parent  # .../bin/cmake.exe → .../
    for p in [Path("C:/Program Files/CMake"), Path("C:/Program Files (x86)/CMake")]:
        if p.joinpath("bin/cmake.exe").is_file():
            return p
    return None

def _find_mingw_dir() -> Path | None:
    """查找 MinGW 安装目录 (gcc.exe)。"""
    gcc = _find_in_path("gcc")
    if gcc:
        return gcc.parent.parent  # .../bin/gcc.exe → .../
    for base in [os.environ.get("MINGW_ROOT", ""),
                 "G:/mingw64", "D:/mingw64", "C:/mingw64",
                 "C:/msys64/mingw64", "C:/Program Files/mingw64",
                 "C:/ProgramData/chocolatey/lib/mingw/tools/install"]:
        if base:
            p = Path(base)
            if p.joinpath("bin/gcc.exe").is_file():
                return p
    # Chocolatey 安装路径可能带版本号
    choco = Path("C:/ProgramData/chocolatey/lib/mingw/tools/install")
    if choco.is_dir():
        for d in choco.iterdir():
            if d.joinpath("bin/gcc.exe").is_file():
                return d
    return None

def _find_go_dir() -> Path | None:
    """查找 Go 安装目录。"""
    go = _find_in_path("go")
    if go:
        return go.parent.parent  # .../bin/go.exe → .../
    for p in [Path("C:/Program Files/Go"), Path("C:/Go"),
              Path("D:/Program Files/Go")]:
        if p.joinpath("bin/go.exe").is_file():
            return p
    return None

def _find_node_dir() -> Path | None:
    """查找 Node.js 安装目录。"""
    node = _find_in_path("node")
    if node:
        return node.parent
    for p in [Path("C:/Program Files/nodejs"), Path("D:/Program Files/nodejs")]:
        if p.joinpath("node.exe").is_file():
            return p
    return None

def _find_flutter_dir() -> Path | None:
    """查找 Flutter SDK 目录。"""
    flutter = _find_in_path("flutter")
    if flutter:
        return flutter.parent.parent  # .../bin/flutter.bat → .../
    for p in [os.environ.get("FLUTTER_ROOT", ""),
              "G:/flutter_SDK/flutter", "C:/flutter", "C:/src/flutter",
              "D:/flutter", "D:/flutter_SDK/flutter"]:
        if p:
            d = Path(p)
            if d.joinpath("bin/flutter.bat").is_file() or d.joinpath("bin/flutter.exe").is_file():
                return d
    return None

# ── 自动发现结果缓存 ──
_vs_dir: Path | None = None
_mingw_dir: Path | None = None
_cmake_dir: Path | None = None
_go_dir: Path | None = None
_node_dir: Path | None = None
_flutter_dir: Path | None = None
_steam_library: str = ""
_toolchain_setup_done: bool = False


def setup_toolchain():
    """自动发现并配置所有工具链到 PATH，设置游戏路径环境变量。幂等，仅首次调用生效。"""
    global _toolchain_setup_done, _vs_dir, _mingw_dir, _cmake_dir, _go_dir, _node_dir, _flutter_dir, _steam_library
    if _toolchain_setup_done:
        return
    _toolchain_setup_done = True

    cfg = load_config()
    tc = cfg.get("toolchain", {})

    # ── Steam / 游戏路径 ──
    # 优先级: CHILL_STEAM_LIBRARY 环境变量 > 自动发现 > 配置文件 > 默认
    if "CHILL_STEAM_LIBRARY" not in os.environ:
        _steam_library = _find_steam_library()
        os.environ["CHILL_STEAM_LIBRARY"] = _steam_library
    else:
        _steam_library = os.environ["CHILL_STEAM_LIBRARY"]
    # 回写到配置，方便 GUI 显示/修改
    if _steam_library and _steam_library != cfg.get("steam_library", ""):
        cfg["steam_library"] = _steam_library
        save_config(cfg)

    paths_to_add: list[str] = []

    def _add(p: Path | None):
        if p and str(p) not in os.environ.get("PATH", ""):
            paths_to_add.append(str(p))

    # ── Go (配置覆盖 > 自动发现) ──
    if tc.get("go_root"):
        _go_dir = Path(tc["go_root"])
    else:
        _go_dir = _find_go_dir()
    if _go_dir:
        _add(_go_dir / "bin")

    # ── MinGW (CGo 需要) ──
    if tc.get("mingw_root"):
        _mingw_dir = Path(tc["mingw_root"])
    else:
        _mingw_dir = _find_mingw_dir()
    if _mingw_dir:
        _add(_mingw_dir / "bin")
        if "CC" not in os.environ:
            os.environ["CC"] = "gcc"

    # ── Visual Studio + MSVC ──
    if tc.get("vs_install"):
        _vs_dir = Path(tc["vs_install"])
    else:
        _vs_dir = _find_vs_install_dir()
    if _vs_dir:
        llvm_bin = _vs_dir / "VC" / "Tools" / "Llvm" / "bin"
        if llvm_bin.joinpath("clang.exe").is_file():
            _add(llvm_bin)

    # ── CMake ──
    _cmake_dir = _find_cmake_dir()
    if _cmake_dir:
        _add(_cmake_dir / "bin")
    elif _vs_dir:
        vscmake = _vs_dir / "Common7" / "IDE" / "CommonExtensions" / "Microsoft" / "CMake" / "CMake" / "bin"
        if vscmake.joinpath("cmake.exe").is_file():
            _add(vscmake)

    # ── Node.js ──
    if tc.get("node_root"):
        _node_dir = Path(tc["node_root"])
    else:
        _node_dir = _find_node_dir()
    if _node_dir:
        _add(_node_dir)

    # ── Flutter ──
    if tc.get("flutter_root"):
        _flutter_dir = Path(tc["flutter_root"])
    else:
        _flutter_dir = _find_flutter_dir()
    if _flutter_dir:
        _add(_flutter_dir / "bin")

    if paths_to_add:
        os.environ["PATH"] = ";".join(paths_to_add) + ";" + os.environ.get("PATH", "")

    print(f"[Toolchain] SteamLib={_steam_library}, Go={_go_dir}, Mingw={_mingw_dir}, "
          f"VS={_vs_dir}, CMake={_cmake_dir}, Node={_node_dir}, Flutter={_flutter_dir}")


# 供外部读取自动发现结果
def get_vs_dir() -> Path | None: return _vs_dir
def get_mingw_dir() -> Path | None: return _mingw_dir
def get_flutter_dir() -> Path | None: return _flutter_dir
def get_steam_library() -> str: return _steam_library


# ════════════════════════════════════════════
#  目录路径常量
# ════════════════════════════════════════════

MOD_DIR = ROOT / "mods" / "chillPatcher" / "src"
MOD_RELEASE = ROOT / "release" / "ChillPatcher"
MOD_SDK_PROJ = MOD_DIR / "ChillPatcher.SDK" / "ChillPatcher.SDK.csproj"
MOD_MAIN_PROJ = MOD_DIR / "ChillPatcher.csproj"
MOD_ONEJS_PROJ = MOD_DIR / "ChillPatcher.OneJS" / "ChillPatcher.OneJS.csproj"
MOD_UI_DIRS = [MOD_DIR / "ui" / "default", MOD_DIR / "ui" / "window-manager"]

PLAYER_DIR = ROOT / "OmniMixPlayer"
PLAYER_BUILD = ROOT / "playerbuild"
PLAYER_SDK_PROJ = PLAYER_DIR / "OmniMixPlayer.SDK" / "OmniMixPlayer.SDK.csproj"
PLAYER_BACKEND_PROJ = PLAYER_DIR / "OmniMixPlayer.Backend" / "OmniMixPlayer.Backend.csproj"
PLAYER_BACKEND_PUBLISH = PLAYER_DIR / "bin" / "BackendPublish"
PLAYER_MODULES_BUILD = PLAYER_DIR / "bin" / "Modules"
PLAYER_FLUTTER_DIR = PLAYER_DIR / "gui_flutter"
PLAYER_FLUTTER_BUILD = PLAYER_FLUTTER_DIR / "build" / "windows" / "x64" / "runner" / "Release"
PLAYER_FLUTTER_WEB_BUILD = PLAYER_FLUTTER_DIR / "build" / "web"
PLAYER_WWWROOT = PLAYER_DIR / "OmniMixPlayer.Backend" / "wwwroot"

MEDIA_GEN_PROJ = ROOT / "ChillPatcher.MediaGenerator" / "ChillPatcher.MediaGenerator.csproj"
MEDIA_GEN_PUBLISH = ROOT / "ChillPatcher.MediaGenerator" / "bin" / "publish"

NATIVE_PLUGINS_DIR = ROOT / "NativePlugins"
NATIVE_PROJECTS_ALWAYS = [
    "OmniAudioDecoder", "OmniPcmShared", "SpotifyLibrespotBridge",
    "EsbuildBridge", "SmtcBridge",
]
NATIVE_PROJECTS_FULL_ONLY = ["netease_bridge", "qqmusic_bridge"]

PLAYER_MODULE_MAP = [
    ("LocalFolder", "com.chillpatcher.localfolder"),
    ("Netease", "com.chillpatcher.netease"),
    ("Bilibili", "com.chillpatcher.bilibili"),
    ("QQMusic", "com.chillpatcher.qqmusic"),
    ("Spotify", "com.chillpatcher.spotify"),
]

FH6_DIR = ROOT / "mods" / "ForzaHorizon6OmniBridge"
FH6_BIN = FH6_DIR / "bin" / "version.dll"
FH6_STAGE = ROOT / "release" / "FH6OmniBridge"
FH6_ZIP = ROOT / "release" / "FH6OmniBridge.zip"
FH6_FLUTTER_ASSETS = PLAYER_FLUTTER_DIR / "assets" / "FH6OmniBridge.zip"

MOD_RELEASE_ZIP = ROOT / "release" / "ChillPatcher.zip"
MOD_FLUTTER_ASSET = PLAYER_FLUTTER_DIR / "assets" / "ChillPatcher.zip"

OMNI_PCM_DLL = ROOT / "bin" / "native" / "x64" / "OmniPcmShared.dll"
