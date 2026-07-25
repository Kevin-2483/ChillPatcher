# -*- coding: utf-8 -*-
"""
统一构建任务树
根据用户选择的模式和选项, 返回一棵 TaskNode 树。
正确的依赖关系:
  根 (3个): Backend / MediaGenerator / Flutter GUI
  Flutter GUI 的 assets/ 下有两个 zip: ChillPatcher.zip, FH6OmniBridge.zip
"""
from __future__ import annotations
import os
import shutil
from pathlib import Path

from tasks.base import TaskNode, TaskStatus
from tasks.common import (
    _rmtree_ignore_locked, copy_file, copy_dir_contents, copy_dir_except,
    dotnet_build, dotnet_restore, dotnet_publish,
    info, run_cmd, clean_cmake_cache,
    read_version_info, write_version_json, package_zip,
)
import build_config as C


# ── 清理 gui_flutter/assets/ 中的生成文件，避免跨构建残留 ──

_GENERATED_ASSETS = ["ChillPatcher.zip", "FH6OmniBridge.zip", "version_info.json"]


def _clean_generated_assets() -> bool:
    """删除 gui_flutter/assets/ 中由构建脚本生成的文件，避免跨构建残留。"""
    assets_dir = C.PLAYER_FLUTTER_DIR / "assets"
    if not assets_dir.exists():
        return True
    for name in _GENERATED_ASSETS:
        p = assets_dir / name
        if p.exists():
            p.unlink()
            info(f"  Removed stale asset: {name}")
    return True


# ════════════════════════════════════════════
#  构建树入口: 根据模式返回 TaskNode 根列表
# ════════════════════════════════════════════

def build_tree(mode: str, full: bool, skip_flutter: bool) -> list[TaskNode]:
    """返回构建树的根节点列表。"""
    _built_natives.clear()  # 每次重建树时重置去重
    roots = []

    if mode in ("all", "player"):
        roots.append(_backend(full, skip_flutter))
        roots.append(_media_generator())
        roots.append(_flutter_gui(full, skip_flutter, include_mod_assets=mode == "all"))
        roots.append(_assemble_playerbuild())
        roots.append(_installer())

    elif mode == "installer":
        # 仅打包安装程序 (假设 playerbuild 已就位)
        roots.append(_installer())

    elif mode == "mod":
        # 仅构建 mod, 输出到 release/ChillPatcher/ + zip
        roots.append(_chillpatcher_asset(full))

    elif mode == "fh6-asset":
        roots.append(_fh6_asset(full))

    elif mode == "fh6":
        roots.append(_fh6_mod(full))

    return roots


# ════════════════════════════════════════════
#  根 1: Backend
# ════════════════════════════════════════════

def _backend(full: bool, skip_flutter: bool) -> TaskNode:
    g = TaskNode("🖥 Backend",
        "OmniMixPlayer.Backend.exe (ASP.NET Single-File)\n"
        "  wwwroot/ 内嵌 Flutter Web WASM")

    g.create_leaf("Clean Backend", "", run_fn=_clean_backend)

    if full:
        g.create_leaf("Restore SDK NuGet", "",
                      run_fn=lambda: dotnet_restore(C.PLAYER_SDK_PROJ))

    # Flutter Web → wwwroot
    if skip_flutter:
        g.create_leaf("Flutter Web (跳过)", "",
                      run_fn=lambda: TaskStatus.DISABLED)
    else:
        fw = g.create_group("Flutter Web → wwwroot/", "WASM 编译后放入 Backend wwwroot")
        fw.create_leaf("clean assets", "清理 gui_flutter/assets/ 中的生成文件",
                       run_fn=_clean_generated_assets)
        fw.create_leaf("flutter clean", "清除 Flutter 构建缓存",
                       run_fn=lambda: run_cmd(["flutter", "clean"],
                                              cwd=C.PLAYER_FLUTTER_DIR))
        fw.create_leaf("pub get", "",
                       run_fn=lambda: run_cmd(["flutter", "pub", "get"],
                                              cwd=C.PLAYER_FLUTTER_DIR))
        fw.create_leaf("gen-l10n", "",
                       run_fn=lambda: run_cmd(["flutter", "gen-l10n"],
                                              cwd=C.PLAYER_FLUTTER_DIR))
        fw.create_leaf("build web --wasm", "编译 Flutter Web WASM",
                       run_fn=_build_flutter_web_and_copy)

    # SDK
    g.create_leaf("Build SDK", "OmniMixPlayer.SDK",
                  run_fn=lambda: dotnet_build(C.PLAYER_SDK_PROJ))

    # Backend publish
    if full:
        g.create_leaf("Restore Backend (win-x64)", "",
                      run_fn=lambda: run_cmd(
                          ["dotnet", "restore", str(C.PLAYER_BACKEND_PROJ),
                           "--runtime", "win-x64"]))
    g.create_leaf("Publish Backend", "Single-File 发布",
                  run_fn=_publish_backend)

    # Native plugins (通用, 不归属特定模块)
    ng = g.create_group("Native Plugins", "后端需要的原生 DLL")
    for proj in ["OmniAudioDecoder"]:
        ng.create_leaf(f"  {proj}", "", run_fn=_make_native_fn(proj))

    # Modules (各自带 native bridge)
    mg = g.create_group("Modules", "播放器功能模块")

    _module_native = {
        "Netease": "netease_bridge",
        "QQMusic": "qqmusic_bridge",
        "Spotify": "SpotifyLibrespotBridge",
    }

    for src_name, _ in C.PLAYER_MODULE_MAP:
        m = mg.create_group(f"  {src_name}", f"模块: {src_name}")
        m.create_leaf(f"Build C# 模块", "",
                      run_fn=_make_module_fn(src_name, full))
        # 如果此模块有 native bridge, 加入
        if src_name in _module_native:
            m.create_leaf(f"Native: {_module_native[src_name]}", "",
                          run_fn=_make_native_fn(_module_native[src_name]))

    return g


# ════════════════════════════════════════════
#  根 2: MediaGenerator
# ════════════════════════════════════════════

def _media_generator() -> TaskNode:
    g = TaskNode("🛠 MediaGenerator CLI", "chill-gen-media.exe")
    g.create_leaf("Publish MediaGenerator", "Single-File 发布",
                  run_fn=lambda: dotnet_publish(C.MEDIA_GEN_PROJ, C.MEDIA_GEN_PUBLISH,
                                                single_file=True))
    return g


# ════════════════════════════════════════════
#  根 3: Flutter GUI App
# ════════════════════════════════════════════

def _flutter_gui(full: bool, skip_flutter: bool, include_mod_assets: bool) -> TaskNode:
    g = TaskNode("📱 Flutter GUI App",
        "Flutter Windows 桌面应用\n"
        "  产物: omni_mix_player.exe + omnimix_audio.dll\n"
        "  assets/ 内嵌: FH6OmniBridge.zip"
        + (" + ChillPatcher.zip" if include_mod_assets else ""))

    if skip_flutter:
        g.create_leaf("Flutter GUI (跳过)", "",
                      run_fn=lambda: TaskStatus.DISABLED)
        if include_mod_assets:
            g.children.append(_chillpatcher_asset(full))
        g.children.append(_fh6_asset(full))
        return g

    g.create_leaf("clean assets", "清理 gui_flutter/assets/ 中的生成文件",
                  run_fn=_clean_generated_assets)

    g.create_leaf("flutter clean", "清除 Flutter 构建缓存",
                  run_fn=lambda: run_cmd(["flutter", "clean"],
                                         cwd=C.PLAYER_FLUTTER_DIR))

    g.create_leaf("gen-l10n", "",
                  run_fn=lambda: run_cmd(["flutter", "gen-l10n"],
                                         cwd=C.PLAYER_FLUTTER_DIR))

    # Rust 音频播放器 (Flutter FFI)
    rust_dir = C.PLAYER_FLUTTER_DIR / "rust"
    g.create_leaf("Rust: omnimix_audio", "cargo build --release → cdylib",
                  run_fn=lambda: run_cmd(["cargo", "build", "--release"],
                                         cwd=rust_dir))

    # ── 资产必须在 Flutter build 前生成，随后由 Flutter 打入应用包 ──
    if include_mod_assets:
        g.children.append(_chillpatcher_asset(full))
    g.children.append(_fh6_asset(full))

    g.create_leaf("build windows --release", "编译 Flutter Windows",
                  run_fn=_build_flutter_windows)

    # Version info
    g.create_leaf("Version Info", "写入 version_info.json → assets/",
                  run_fn=_write_version_assets)
    return g


# ════════════════════════════════════════════
#  Asset: ChillPatcher.zip
# ════════════════════════════════════════════

def _chillpatcher_asset(full: bool) -> TaskNode:
    g = TaskNode("📦 Asset: ChillPatcher.zip", "BepInEx Mod → Flutter assets/")

    g.create_leaf("Clean mod release", "", run_fn=_clean_mod_release)

    if full:
        g.create_leaf("Restore NuGet", "",
                      run_fn=lambda: _restore_mod_nuget())

    g.create_leaf("Build SDK", "ChillPatcher.SDK",
                  run_fn=lambda: dotnet_build(C.MOD_SDK_PROJ))
    g.create_leaf("Build Main Plugin", "ChillPatcher.dll",
                  run_fn=lambda: dotnet_build(C.MOD_MAIN_PROJ))
    g.create_leaf("Build OneJS", "ChillPatcher.OneJS",
                  run_fn=lambda: dotnet_build(C.MOD_ONEJS_PROJ))

    # UI
    ui_g = g.create_group("UI (esbuild)", "Preact 前端打包")
    for ui_dir in C.MOD_UI_DIRS:
        ui_g.create_leaf(f"  {ui_dir.name}", "",
                         run_fn=_make_ui_fn(ui_dir))

    # Native (full only)
    if full:
        ng = g.create_group("Native Plugins", "C++ 原生插件")
        for proj in ["OmniAudioDecoder", "OmniPcmShared",
                     "EsbuildBridge", "SmtcBridge"]:
            ng.create_leaf(f"  {proj}", "", run_fn=_make_native_fn(proj))
        g.create_leaf("Stage OmniPcmShared.dll", run_fn=_stage_omni_pcm)

    g.create_leaf("Assemble → release/ChillPatcher/",
        "组装 BepInEx 插件发布目录:\n"
        "  ├ ChillPatcher.dll + ChillPatcher.dll.config\n"
        "  ├ SDK/ChillPatcher.SDK.dll\n"
        "  ├ native/x64/OmniAudioDecoder.dll, OmniPcmShared.dll, ...\n"
        "  ├ native/x64/puerts.dll\n"
        "  ├ native/x64/VC++ runtime DLLs\n"
        "  ├ rime.dll + rime-data/ (输入法引擎)\n"
        "  └ ui/ (Preact 前端)",
        run_fn=_assemble_mod)
    g.create_leaf("Package ZIP → assets/",
        f"打包 release/ChillPatcher/ → {C.MOD_RELEASE_ZIP.name}\n"
        f"  复制到 {C.MOD_FLUTTER_ASSET}\n"
        "  Flutter 构建时该 zip 自动打入 assets/",
        run_fn=_package_mod_zip)
    return g


# ════════════════════════════════════════════
#  Asset: FH6OmniBridge.zip
# ════════════════════════════════════════════

def _fh6_asset(full: bool) -> TaskNode:
    g = TaskNode("📦 Asset: FH6OmniBridge.zip", "FH6 桥接 → Flutter assets/")

    if full:
        g.create_leaf("Build OmniPcmShared", "",
                      run_fn=_make_native_fn("OmniPcmShared"))
        g.create_leaf("Build FH6 Bridge", "",
                      run_fn=lambda: _build_fh6_bridge())
    else:
        g.create_leaf("(跳过编译, 使用已有产物)", "",
                      run_fn=lambda: TaskStatus.SUCCESS)

    g.create_leaf("Package ZIP → assets/",
        f"打包 → {C.FH6_ZIP.name}:\n"
        "  ├ version.dll (FH6 桥接)\n"
        "  ├ OmniPcmShared.dll\n"
        "  └ README.md\n"
        f"  复制到 {C.FH6_FLUTTER_ASSETS}",
        run_fn=_package_fh6_asset)
    return g


# ════════════════════════════════════════════
#  FH6 Mod (独立目标: release/FH6OmniBridge/)
# ════════════════════════════════════════════

def _fh6_mod(full: bool) -> TaskNode:
    g = TaskNode("📁 FH6 Mod", "→ release/FH6OmniBridge/")

    g.create_leaf("Clean FH6 release", "", run_fn=_clean_fh6_release)

    if full:
        g.create_leaf("Build OmniPcmShared", "", run_fn=_make_native_fn("OmniPcmShared"))
        g.create_leaf("Build FH6 Bridge", "", run_fn=_build_fh6_bridge)
    else:
        g.create_leaf("(跳过编译)", "", run_fn=lambda: TaskStatus.SUCCESS)

    g.create_leaf("Assemble → release/FH6OmniBridge/",
        "组装 FH6 原生 Mod:\n"
        "  ├ version.dll (FH6 桥接)\n"
        "  ├ OmniPcmShared.dll\n"
        "  └ README.md",
        run_fn=_assemble_fh6_mod)
    return g


# ════════════════════════════════════════════
#  Assemble playerbuild
# ════════════════════════════════════════════

def _assemble_playerbuild() -> TaskNode:
    g = TaskNode("📋 Assemble → playerbuild/", "合并所有产物到发布目录")

    # Backend (single-file exe + 运行时)
    g.create_leaf(
        "Backend publish → playerbuild/",
        "OmniMixPlayer.Backend.exe (单文件) + 运行时依赖 → playerbuild/ 根目录",
        run_fn=lambda: _copy_backend_to_build())

    # MediaGenerator
    g.create_leaf(
        "MediaGenerator → playerbuild/",
        "chill-gen-media.exe + config.json + *.pdb → playerbuild/ 根目录",
        run_fn=lambda: _copy_mediagen_to_build())

    # Flutter GUI
    g.create_leaf(
        "Flutter GUI → playerbuild/",
        "gui_flutter/build/windows/x64/runner/Release/* → playerbuild/\n"
        "  ├ omni_mix_player.exe\n"
        "  ├ flutter_windows.dll\n"
        "  ├ data/flutter_assets/  (含 assets/ChillPatcher.zip, assets/FH6OmniBridge.zip)\n"
        "  ├ omnimix_audio.dll (Rust)\n"
        "  └ OmniPcmShared.dll (Dart FFI)",
        run_fn=lambda: _copy_flutter_to_build())

    # Native decoders (后端用)
    g.create_leaf(
        "Native decoders → playerbuild/native/x64/",
        "OmniAudioDecoder.dll → playerbuild/native/x64/",
        run_fn=lambda: _copy_native_decoders())

    # 各模块 DLL
    for src_name, module_id in C.PLAYER_MODULE_MAP:
        g.create_leaf(
            f"Module {src_name} → playerbuild/modules/{module_id}/",
            f"*.dll + *.json + *.png → modules/{module_id}/\n"
            f"  native DLL → modules/{module_id}/native/x64/",
            run_fn=_make_copy_module_fn(src_name, module_id))

    # Cleanup
    g.create_leaf(
        "Cleanup", "删除 runtimes/ *.pdb *.xml *.deps.json",
        run_fn=lambda: _cleanup_build())

    return g


# ── 组装实现 ──

def _copy_backend_to_build() -> bool:
    if not C.PLAYER_BACKEND_PUBLISH.exists():
        info("  WARNING: Backend publish not found")
        return False
    copy_dir_contents(C.PLAYER_BACKEND_PUBLISH, C.PLAYER_BUILD)
    _rmtree_ignore_locked(C.PLAYER_BUILD / "modules")
    info("  Backend → playerbuild/")
    return True


def _copy_mediagen_to_build() -> bool:
    if not C.MEDIA_GEN_PUBLISH.exists():
        info("  WARNING: MediaGenerator publish not found")
        return False
    for f in C.MEDIA_GEN_PUBLISH.glob("chill-gen-media.exe"):
        copy_file(f, C.PLAYER_BUILD)
    for f in C.MEDIA_GEN_PUBLISH.glob("*.pdb"):
        copy_file(f, C.PLAYER_BUILD)
    for cfg in ["config.json"]:
        src = C.MEDIA_GEN_PUBLISH / cfg
        if src.exists():
            copy_file(src, C.PLAYER_BUILD)
    info("  MediaGenerator → playerbuild/")
    return True


def _copy_flutter_to_build() -> bool:
    if not C.PLAYER_FLUTTER_BUILD.exists():
        info("  WARNING: Flutter build not found")
        return False
    for item in C.PLAYER_FLUTTER_BUILD.iterdir():
        dst = C.PLAYER_BUILD / item.name
        if item.is_dir():
            shutil.copytree(item, dst, dirs_exist_ok=True)
        else:
            copy_file(item, C.PLAYER_BUILD)
    # OmniPcmShared.dll 放到 exe 旁边供 Dart FFI 加载
    if C.OMNI_PCM_DLL.exists():
        copy_file(C.OMNI_PCM_DLL, C.PLAYER_BUILD)
    info("  Flutter GUI → playerbuild/")
    return True


def _copy_native_decoders() -> bool:
    native_src = C.ROOT / "bin" / "native" / "x64"
    if not native_src.exists():
        info("  WARNING: bin/native/x64 not found")
        return False
    native_dst = C.PLAYER_BUILD / "native" / "x64"
    native_dst.mkdir(parents=True, exist_ok=True)
    for dll in ["OmniAudioDecoder.dll"]:
        src = native_src / dll
        if src.exists():
            copy_file(src, native_dst)
    info("  Native decoders → playerbuild/native/x64/")
    return True


def _make_copy_module_fn(src_name: str, module_id: str):
    # Assemblies already loaded by the Backend host process.
    # Modules are loaded into the same process, so they don't need copies.
    _HOST_ASSEMBLIES = {
        "Google.Protobuf",
        "Grpc.AspNetCore.Server", "Grpc.AspNetCore.Server.ClientFactory",
        "Grpc.Core.Api", "Grpc.Net.Client", "Grpc.Net.ClientFactory",
        "Grpc.Net.Common",
        "Newtonsoft.Json",
        "Microsoft.Extensions.DependencyInjection.Abstractions",
        "Microsoft.Extensions.Logging.Abstractions",
    }

    def _fn():
        src_dir = C.PLAYER_MODULES_BUILD / src_name
        dst_dir = C.PLAYER_BUILD / "modules" / module_id
        if not src_dir.exists():
            info(f"  WARNING: Module {src_name} not built")
            return False
        dst_dir.mkdir(parents=True, exist_ok=True)
        for ext in ("*.dll", "*.json", "*.png"):
            for f in src_dir.glob(ext):
                if f.stem in _HOST_ASSEMBLIES:
                    continue  # already provided by Backend host
                copy_file(f, dst_dir)
        # Native from build output
        for src_n in [src_dir / "native" / "x64"]:
            if src_n.exists():
                dst_n = dst_dir / "native" / "x64"
                dst_n.mkdir(parents=True, exist_ok=True)
                for f in src_n.iterdir():
                    if f.suffix in (".dll", ".exe"):
                        copy_file(f, dst_n)
        # Native from source tree
        src_mod = C.PLAYER_DIR / "modules" / src_name
        for src_n in [src_mod / "native" / "x64"]:
            if src_n.exists():
                dst_n = dst_dir / "native" / "x64"
                dst_n.mkdir(parents=True, exist_ok=True)
                for f in src_n.iterdir():
                    if f.suffix in (".dll", ".exe"):
                        copy_file(f, dst_n)
        info(f"  {src_name} → modules/{module_id}/")
        return True
    return _fn


def _cleanup_build() -> bool:
    _rmtree_ignore_locked(C.PLAYER_BUILD / "runtimes")
    for pat in ("*.pdb", "*.xml", "*.deps.json"):
        for f in C.PLAYER_BUILD.rglob(pat):
            try:
                f.unlink()
            except Exception:
                pass
    info("  Cleaned unnecessary files")
    return True


# ════════════════════════════════════════════
#  内部实现函数
# ════════════════════════════════════════════

def _clean_backend() -> bool:
    _rmtree_ignore_locked(C.PLAYER_BUILD)
    (C.PLAYER_BUILD / "modules").mkdir(parents=True, exist_ok=True)
    (C.PLAYER_BUILD / "native" / "x64").mkdir(parents=True, exist_ok=True)
    info("playerbuild cleaned")
    return True


def _build_flutter_web_and_copy() -> int:
    code = run_cmd(["flutter", "build", "web", "--wasm", "-t", "lib/main_web.dart"],
                   cwd=C.PLAYER_FLUTTER_DIR)
    if code != 0:
        info("  WARNING: Flutter Web build failed")
        return code
    _rmtree_ignore_locked(C.PLAYER_WWWROOT)
    C.PLAYER_WWWROOT.mkdir(parents=True, exist_ok=True)
    copy_dir_contents(C.PLAYER_FLUTTER_WEB_BUILD, C.PLAYER_WWWROOT)
    info("  Flutter Web (WASM) → wwwroot/")
    return 0


def _publish_backend() -> int:
    if C.PLAYER_BACKEND_PUBLISH.exists():
        shutil.rmtree(C.PLAYER_BACKEND_PUBLISH)
    return dotnet_publish(C.PLAYER_BACKEND_PROJ, C.PLAYER_BACKEND_PUBLISH,
                          single_file=True)


def _build_flutter_windows() -> int:
    code = run_cmd(["flutter", "build", "windows", "--release"],
                   cwd=C.PLAYER_FLUTTER_DIR)
    if code != 0:
        info("  WARNING: Flutter build failed")
    return code


# ── Session 级去重: 同一个构建中, native 只编译一次 ──
_built_natives: set[str] = set()

# ── Python 原生构建 (不再依赖 build.bat, 消除 shell 编码/路径问题) ──

def _build_native(proj: str) -> int:
    src = C.NATIVE_PLUGINS_DIR / proj
    bin64 = C.ROOT / "bin" / "native" / "x64"
    bin64.mkdir(parents=True, exist_ok=True)

    if proj == "OmniAudioDecoder":
        return _rust_build(src, "omni_audio_decoder", [
            (bin64 / "OmniAudioDecoder.dll", "OmniAudioDecoder"),
            (bin64 / "ChillAudioDecoder.dll", "ChillAudioDecoder"),
            (bin64 / "ChillFlacDecoder.dll", "ChillFlacDecoder"),
        ])
    elif proj == "SpotifyLibrespotBridge":
        mod_dst = C.PLAYER_DIR / "modules" / "Spotify" / "native" / "x64"
        mod_dst.mkdir(parents=True, exist_ok=True)
        return _rust_build(src, "spotify_librespot_bridge", [
            (mod_dst / "SpotifyLibrespotBridge.dll", None),
        ])
    elif proj == "OmniPcmShared":
        return _cmake_build(src, "OmniPcmShared.dll", [bin64 / "OmniPcmShared.dll"])
    elif proj == "SmtcBridge":
        return _cmake_build(src, "ChillSmtcBridge.dll", [bin64 / "ChillSmtcBridge.dll"])
    elif proj == "EsbuildBridge":
        return _go_cgo_build(src, "ChillEsbuildBridge.dll", bin64)
    elif proj == "qqmusic_bridge":
        return _go_cgo_build(src, "ChillQQMusic.dll", bin64)
    elif proj == "netease_bridge":
        # netease_bridge 构建逻辑复杂 (clone go-musicfox, patch, etc), 保留 build.bat
        clean_cmake_cache(src)
        return run_cmd(["build.bat", "--no-pause"], cwd=src)
    else:
        info(f"  WARNING: unknown native project: {proj}")
        return 1


def _rust_build(src: Path, dll_stem: str,
                copies: list[tuple[Path, str | None]]) -> int:
    """cargo build --release + 复制 DLL 到目标位置。"""
    # 确保 Rust 工具链在 PATH
    cargo = shutil.which("cargo") or "cargo"
    info(f"  cargo build --release ({dll_stem})")
    code = run_cmd([cargo, "build", "--release"], cwd=src)
    if code != 0:
        return code
    target_dll = src / "target" / "release" / f"{dll_stem}.dll"
    if not target_dll.exists():
        info(f"  ERROR: {target_dll} not found after build")
        return 1
    for dst_path, _name_hint in copies:
        dst_path.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(target_dll, dst_path)
        info(f"  copy → {dst_path}")
    return 0


def _cmake_build(src: Path, dll_name: str,
                 dst_paths: list[Path], extra_defines: list[str] | None = None) -> int:
    """CMake 配置 + 构建 (x64 Release) + 复制 DLL。"""
    build_dir = src / "build" / "x64"
    build_dir.mkdir(parents=True, exist_ok=True)
    defines = ["-DCMAKE_BUILD_TYPE=Release"] + (extra_defines or [])

    cmake = _find_vs_cmake() or shutil.which("cmake") or "cmake"

    # VS Generator 名 (用 vswhere 动态获取版本号)
    vs_gen = _get_vs_generator()
    if vs_gen:
        cmake_args = ["-G", vs_gen, "-A", "x64"] + defines
    else:
        cmake_args = ["-G", "Ninja",
                      "-DCMAKE_C_COMPILER=cl",
                      "-DCMAKE_CXX_COMPILER=cl"] + defines

    info(f"  cmake -G {vs_gen or 'Ninja'} -A x64 -S {src} -B {build_dir}")
    # 清理旧的 Ninja 缓存 (generator 不匹配会导致 cmake 报错)
    cache_file = build_dir / "CMakeCache.txt"
    if cache_file.exists():
        cache_text = cache_file.read_text(encoding="utf-8", errors="ignore")
        if "Ninja" in cache_text and vs_gen:
            info(f"  removing stale Ninja cache...")
            shutil.rmtree(build_dir, ignore_errors=True)
            build_dir.mkdir(parents=True, exist_ok=True)
    code = run_cmd([cmake] + cmake_args +
                   ["-S", str(src), "-B", str(build_dir)])
    if code != 0:
        return code

    info(f"  cmake --build {build_dir} --config Release")
    code = run_cmd([cmake, "--build", str(build_dir), "--config", "Release"])
    if code != 0:
        return code

    found_any = False
    for dst in dst_paths:
        for candidate in [
            build_dir / "bin" / "Release" / dll_name,
            build_dir / "Release" / dll_name,
            build_dir / dll_name,
            src / "bin" / dll_name,          # 有些 CMake 项目输出到 src/bin/
            src / "bin" / "Release" / dll_name,
        ]:
            if candidate.exists():
                dst.parent.mkdir(parents=True, exist_ok=True)
                if candidate.resolve() == dst.resolve():
                    info(f"  skip (same file): {dst}")
                else:
                    shutil.copy2(candidate, dst)
                    info(f"  copy → {dst}")
                found_any = True
                break
        else:
            info(f"  WARNING: {dll_name} not found in build output")
    return 0 if found_any else 1


def _get_vs_generator() -> str | None:
    """用 vswhere 获取 VS Generator 名 (如 'Visual Studio 18 2026')。"""
    import subprocess
    vs = C.get_vs_dir()
    if not vs:
        return None
    parts = vs.parts
    try:
        ver_dir = parts[parts.index("Microsoft Visual Studio") + 1]
        if ver_dir == "18":
            return "Visual Studio 18 2026"
        elif ver_dir == "2022":
            return "Visual Studio 17 2022"
        elif ver_dir == "2019":
            return "Visual Studio 16 2019"
    except (ValueError, IndexError):
        pass
    try:
        cmake = shutil.which("cmake") or "cmake"
        result = subprocess.run([cmake, "--help"], capture_output=True, text=True, timeout=10)
        for line in result.stdout.splitlines():
            if "Visual Studio" in line and "202" in line:
                return " ".join(line.strip().split()[:4])
    except Exception:
        pass
    return None


def _find_vs_cmake() -> str | None:
    """用 vswhere 动态查找 VS 自带的 cmake.exe（不硬编码版本号）。"""
    vs = C.get_vs_dir()
    if vs:
        p = vs / "Common7" / "IDE" / "CommonExtensions" / "Microsoft" / \
            "CMake" / "CMake" / "bin" / "cmake.exe"
        if p.is_file():
            return str(p)
    return None


def _go_cgo_build(src: Path, dll_name: str, dst_dir: Path) -> int:
    """Go CGo 构建 c-shared DLL。"""
    go = shutil.which("go") or "go"
    dst_dir.mkdir(parents=True, exist_ok=True)

    # CC 由 setup_toolchain 设置，其余补全
    go_env = os.environ.copy()
    go_env.setdefault("CGO_ENABLED", "1")
    go_env.setdefault("GOOS", "windows")
    go_env.setdefault("GOARCH", "amd64")

    info(f"  go build -buildmode=c-shared → {dll_name}")
    out = str(dst_dir / dll_name)
    code = run_cmd([
        go, "build", "-buildmode=c-shared",
        "-trimpath", "-ldflags=-s -w",
        "-o", out, ".",
    ], cwd=src, env=go_env)
    if code == 0:
        # 清除自动生成的 .h 文件 (C# P/Invoke 不需要)
        h_file = dst_dir / dll_name.replace(".dll", ".h")
        if h_file.exists():
            h_file.unlink()
    return code


def _make_native_fn(proj: str):
    def _build():
        if proj in _built_natives:
            info(f"SKIP {proj}: already built this session (dedup)")
            return TaskStatus.SKIPPED
        _built_natives.add(proj)

        src = C.NATIVE_PLUGINS_DIR / proj
        clean_cmake_cache(src)
        code = _build_native(proj)
        if code != 0:
            info(f"  WARNING: {proj} build failed (exit={code})")
        return code
    return _build


def _stage_omni_pcm() -> int:
    """验证 OmniPcmShared.dll 是否已就位 (由 Native Plugins/OmniPcmShared 构建产出)。
    DLL 已被 _cmake_build 复制到 bin/native/x64/, _assemble_mod 会从此处 glob 到 release/。"""
    src = C.ROOT / "bin" / "native" / "x64" / "OmniPcmShared.dll"
    if src.exists():
        info("  OmniPcmShared.dll ready")
        return 0
    info(f"  WARNING: OmniPcmShared.dll not found at {src}")
    return 1


def _make_module_fn(src_name: str, full: bool):
    def _build():
        proj = C.PLAYER_DIR / "modules" / src_name / f"ChillPatcher.Module.{src_name}.csproj"
        if not proj.exists():
            info(f"  SKIP: project not found")
            return TaskStatus.SKIPPED
        if full:
            code = dotnet_restore(proj)
            if code != 0:
                return code
        return dotnet_build(proj)
    return _build


# ════════════════════════════════════════════
#  安装程序任务
# ════════════════════════════════════════════

def _installer() -> TaskNode:
    from tasks.installer import installer_node
    return installer_node()


def _make_ui_fn(ui_dir: Path):
    def _build():
        name = ui_dir.name
        if not ui_dir.exists():
            info(f"SKIP {name}: not found")
            return TaskStatus.SKIPPED
        code = run_cmd(["where", "npm"])
        if code != 0:
            info("npm not found!")
            return TaskStatus.SKIPPED
        if not (ui_dir / "node_modules").exists():
            info(f"  npm install for {name}...")
            code = run_cmd(["npm", "install"], cwd=ui_dir)
            if code != 0:
                return code
        return run_cmd(["npm", "run", "build"], cwd=ui_dir)
    return _build


def _restore_mod_nuget() -> int:
    for proj in [C.MOD_SDK_PROJ, C.MOD_MAIN_PROJ, C.MOD_ONEJS_PROJ]:
        if proj.exists():
            code = dotnet_restore(proj)
            if code != 0:
                return code
    return 0


def _clean_mod_release() -> bool:
    if C.MOD_RELEASE.exists():
        shutil.rmtree(C.MOD_RELEASE)
    C.MOD_RELEASE.mkdir(parents=True, exist_ok=True)
    (C.MOD_RELEASE / "native" / "x64").mkdir(parents=True, exist_ok=True)
    (C.MOD_RELEASE / "SDK").mkdir(exist_ok=True)
    return True


def _assemble_mod() -> bool:
    mod_bin = C.MOD_DIR / "bin"
    # 主插件
    for f in mod_bin.glob("ChillPatcher.*"):
        if f.suffix in (".dll", ".config") and "SDK" not in f.stem:
            copy_file(f, C.MOD_RELEASE)
    # SDK
    sdk_bin = C.MOD_DIR / "bin" / "SDK"
    if sdk_bin.exists():
        for f in sdk_bin.glob("*.dll"):
            copy_file(f, C.MOD_RELEASE / "SDK")
    # 原生 DLL
    native_src = C.ROOT / "bin" / "native" / "x64"
    native_dst = C.MOD_RELEASE / "native" / "x64"
    exclude = {"ChillNetease.dll", "ChillQQMusic.dll",
               "ChillAudioDecoder.dll", "ChillFlacDecoder.dll"}
    if native_src.exists():
        native_dst.mkdir(parents=True, exist_ok=True)
        for f in native_src.glob("*.dll"):
            if f.name not in exclude:
                copy_file(f, native_dst)
    # VC++ runtime
    lib_dir = C.MOD_DIR / "lib"
    if lib_dir.exists():
        for f in lib_dir.glob("*.dll"):
            copy_file(f, native_dst)
    # puerts.dll
    puerts = C.MOD_DIR / "ChillPatcher.OneJS" / "native" / "x64" / "puerts.dll"
    if puerts.exists():
        copy_file(puerts, native_dst)
    # RIME
    _assemble_rime()

    # UI (复制全部源文件+构建产物, 排除 node_modules/)
    for ui_dir in C.MOD_UI_DIRS:
        dst_ui = C.MOD_RELEASE / "ui" / ui_dir.name
        dst_ui.mkdir(parents=True, exist_ok=True)
        copy_dir_except(ui_dir, dst_ui, skip={"node_modules"})
        info(f"  UI {ui_dir.name} → release/ChillPatcher/ui/{ui_dir.name}/")

    info("Mod files assembled")
    return True


def _assemble_rime():
    rime_dir = C.NATIVE_PLUGINS_DIR / "rime"
    rime_dll = rime_dir / "librime" / "build" / "bin" / "Release" / "rime.dll"
    if rime_dll.exists():
        copy_file(rime_dll, C.MOD_RELEASE)

    rd = C.MOD_RELEASE / "rime-data"
    rs = rd / "shared"
    ro = rs / "opencc"
    for d in [rs, ro, rd / "user"]:
        d.mkdir(parents=True, exist_ok=True)

    def _cp(src_dir, files):
        for fn in files:
            src = src_dir / fn
            if src.exists():
                copy_file(src, rs)

    _cp(rime_dir / "rime-schemas" / "prelude",
        ["symbols.yaml", "punctuation.yaml", "key_bindings.yaml"])
    _cp(rime_dir / "RimeDefaultConfig",
        ["default.yaml", "luna_pinyin.custom.yaml"])
    essay = rime_dir / "rime-schemas" / "essay" / "essay.txt"
    if essay.exists():
        copy_file(essay, rs)
    _cp(rime_dir / "rime-schemas" / "luna-pinyin",
        ["luna_pinyin.schema.yaml", "luna_pinyin.dict.yaml", "pinyin.yaml"])
    _cp(rime_dir / "rime-schemas" / "stroke",
        ["stroke.schema.yaml", "stroke.dict.yaml"])
    _cp(rime_dir / "rime-schemas" / "double-pinyin",
        ["double_pinyin.schema.yaml", "double_pinyin_abc.schema.yaml",
         "double_pinyin_flypy.schema.yaml", "double_pinyin_mspy.schema.yaml"])

    opencc_src = rime_dir / "librime" / "share" / "opencc"
    if opencc_src.exists():
        for f in opencc_src.glob("*.json"):
            copy_file(f, ro)
        for f in opencc_src.glob("*.ocd2"):
            copy_file(f, ro)
    info("  RIME data assembled")


def _package_mod_zip() -> bool:
    return package_zip(C.MOD_RELEASE, C.MOD_RELEASE_ZIP, C.MOD_FLUTTER_ASSET)


def _build_fh6_bridge() -> int:
    """FH6 bridge — CMake x64 构建 version.dll。"""
    clean_cmake_cache(C.FH6_DIR)
    dst = C.FH6_DIR / "bin" / "version.dll"
    dst.parent.mkdir(parents=True, exist_ok=True)
    return _cmake_build(C.FH6_DIR, "version.dll", [dst])


def _package_fh6_asset() -> bool:
    if C.FH6_STAGE.exists():
        shutil.rmtree(C.FH6_STAGE)
    C.FH6_STAGE.mkdir(parents=True, exist_ok=True)
    if C.FH6_BIN.exists():
        copy_file(C.FH6_BIN, C.FH6_STAGE)
    else:
        info("  WARNING: FH6 version.dll not found")
    if C.OMNI_PCM_DLL.exists():
        copy_file(C.OMNI_PCM_DLL, C.FH6_STAGE)
    return package_zip(C.FH6_STAGE, C.FH6_ZIP, C.FH6_FLUTTER_ASSETS)


def _clean_fh6_release() -> bool:
    if C.FH6_STAGE.exists():
        shutil.rmtree(C.FH6_STAGE)
    C.FH6_STAGE.mkdir(parents=True)
    return True


def _assemble_fh6_mod() -> bool:
    if C.FH6_BIN.exists():
        copy_file(C.FH6_BIN, C.FH6_STAGE)
    else:
        info(f"  WARNING: Missing {C.FH6_BIN}")
    if C.OMNI_PCM_DLL.exists():
        copy_file(C.OMNI_PCM_DLL, C.FH6_STAGE)
    readme = C.FH6_DIR / "README.md"
    if readme.exists():
        copy_file(readme, C.FH6_STAGE)
    info("  FH6 mod assembled")
    return True


def _write_version_assets() -> bool:
    fh6_file = C.FH6_DIR / "src" / "bridge.cpp"
    data = read_version_info(C.MOD_DIR, C.PLAYER_FLUTTER_DIR, C.FH6_DIR, fh6_file)
    # 写入 playerbuild/
    dst1 = C.PLAYER_BUILD / "version_info.json"
    # 写入 Flutter assets/
    dst2 = C.PLAYER_FLUTTER_DIR / "assets" / "version_info.json"
    write_version_json(data, dst1, dst2)
    return True
