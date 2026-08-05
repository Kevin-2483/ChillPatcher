# -*- coding: utf-8 -*-
"""
统一构建任务树 — 「全部」模式的正确依赖关系

依赖链:
  OmniPcmShared (共享)
  ├── ChillPatcher Mod → ZIP → gui_flutter/assets/ChillPatcher.zip
  ├── FH6 Bridge      → ZIP → gui_flutter/assets/FH6OmniBridge.zip
  └── OmniMixPlayer   → playerbuild/ (消费上面的 assets)
"""
import shutil

from build_config import (
    MOD_DIR, MOD_RELEASE, MOD_SDK_PROJ, MOD_MAIN_PROJ, MOD_ONEJS_PROJ,
    MOD_UI_DIRS, MOD_RELEASE_ZIP, MOD_FLUTTER_ASSET,
    PLAYER_DIR, PLAYER_BUILD, PLAYER_SDK_PROJ, PLAYER_BACKEND_PROJ,
    PLAYER_BACKEND_PUBLISH, PLAYER_MODULES_BUILD, PLAYER_MODULE_MAP,
    PLAYER_FLUTTER_DIR, PLAYER_FLUTTER_BUILD, PLAYER_FLUTTER_WEB_BUILD,
    PLAYER_WWWROOT, MEDIA_GEN_PROJ, MEDIA_GEN_PUBLISH,
    NATIVE_PLUGINS_DIR, OMNI_PCM_DLL, ROOT,
    FH6_DIR, FH6_BIN, FH6_STAGE, FH6_ZIP, FH6_FLUTTER_ASSETS,
)
from .base import TaskNode, TaskStatus
from .common import (
    _rmtree_ignore_locked, copy_file, copy_dir_contents,
    clean_cmake_cache, dotnet_build, dotnet_restore, dotnet_publish,
    info, run_cmd, read_version_info, write_version_json, package_zip,
)


def create_all_tasks(full: bool = False, skip_flutter: bool = False) -> TaskNode:
    """全部模式: 一棵大树, 正确反映产物流向。"""
    root = TaskNode("CHILL BUILD", "完整构建流水线")

    # ════════════════════════════════════════
    #  共享: OmniPcmShared (所有人依赖)
    # ════════════════════════════════════════
    shared = root.create_group("Shared", "共享原生库 → bin/native/x64/")
    shared.create_leaf(
        "OmniPcmShared", "编译 OmniPcmShared.dll",
        run_fn=_build_omni_pcm,
    )

    # ════════════════════════════════════════
    #  ChillPatcher Mod → ZIP 进 Flutter assets
    # ════════════════════════════════════════
    mod = root.create_group(
        "ChillPatcher Mod",
        "BepInEx 插件 → release/ChillPatcher/ → gui_flutter/assets/ChillPatcher.zip",
    )
    mod.create_leaf("Clean", "清理 release/ChillPatcher/",
                    run_fn=_make_mod_clean(full))
    if full:
        mod.create_leaf("Restore NuGet", "",
                        run_fn=_mod_restore)
    mod.create_leaf("ChillPatcher.SDK", "构建 SDK",
                    run_fn=lambda: dotnet_build(MOD_SDK_PROJ))
    mod.create_leaf("Main Plugin", "chillpatcher.dll",
                    run_fn=lambda: dotnet_build(MOD_MAIN_PROJ))
    mod.create_leaf("OneJS", "ChillPatcher.OneJS",
                    run_fn=lambda: dotnet_build(MOD_ONEJS_PROJ))

    ui_g = mod.create_group("UI (esbuild)", "Preact 前端打包")
    for ui_dir in MOD_UI_DIRS:
        ui_g.create_leaf(ui_dir.name, f"esbuild 打包 {ui_dir.name}",
                         run_fn=_make_ui_fn(ui_dir))

    if full:
        nat_g = mod.create_group("Native Plugins", "mod 需要的原生 DLL")
        _add_native_leaves(nat_g, [
            "OmniAudioDecoder", "OmniPcmShared", "SpotifyLibrespotBridge",
            "EsbuildBridge", "SmtcBridge", "netease_bridge", "qqmusic_bridge",
            "kugou_bridge",
        ])

    mod.create_leaf("Assemble", "组装 → release/ChillPatcher/",
                    run_fn=_mod_assemble)
    mod.create_leaf("ZIP + Version", "打包 zip → Flutter assets",
                    run_fn=_mod_zip_and_version)

    # ════════════════════════════════════════
    #  FH6 Bridge → ZIP 进 Flutter assets
    # ════════════════════════════════════════
    fh6 = root.create_group(
        "FH6 Bridge",
        "Forza Horizon 6 桥接 → gui_flutter/assets/FH6OmniBridge.zip",
    )
    fh6.create_leaf("Build version.dll", "编译 FH6 桥接",
                    run_fn=lambda: run_cmd(["build.bat"], cwd=FH6_DIR))
    fh6.create_leaf("Package ZIP", "打包 → Flutter assets",
                    run_fn=_fh6_package)

    # ════════════════════════════════════════
    #  OmniMixPlayer → playerbuild/
    # ════════════════════════════════════════
    player = root.create_group(
        "OmniMixPlayer",
        "播放器后端 + Flutter GUI → playerbuild/",
    )
    player.create_leaf("Clean", "清理 playerbuild/",
                       run_fn=_player_clean)

    pnat = player.create_group("Native Plugins", "播放器需要的原生 DLL")
    _add_native_leaves(pnat, [
        "OmniAudioDecoder", "OmniPcmShared", "SpotifyLibrespotBridge",
        "EsbuildBridge", "SmtcBridge", "netease_bridge", "qqmusic_bridge",
        "kugou_bridge",
    ])

    # Flutter Web
    if not skip_flutter:
        fw = player.create_group("Flutter Web (WASM)", "→ wwwroot/ (打包进 Backend)")
        fw.create_leaf("pub get", "",
                       run_fn=lambda: run_cmd(["flutter", "pub", "get"],
                                              cwd=PLAYER_FLUTTER_DIR))
        fw.create_leaf("gen-l10n", "",
                       run_fn=lambda: run_cmd(["flutter", "gen-l10n"],
                                              cwd=PLAYER_FLUTTER_DIR))
        fw.create_leaf("build web --wasm", "编译 → wwwroot/",
                       run_fn=_flutter_web_build)

    if full:
        player.create_leaf("Restore SDK NuGet", "",
                           run_fn=lambda: dotnet_restore(PLAYER_SDK_PROJ))
    player.create_leaf("OmniMixPlayer.SDK", "",
                       run_fn=lambda: dotnet_build(PLAYER_SDK_PROJ))

    if full:
        player.create_leaf("Restore Backend NuGet", "",
                           run_fn=lambda: run_cmd(
                               ["dotnet", "restore", str(PLAYER_BACKEND_PROJ),
                                "--runtime", "win-x64"]))
    player.create_leaf("Backend Publish", "单文件发布",
                       run_fn=_publish_backend)

    # Modules
    pmod = player.create_group("Modules", "播放器模块")
    for src_name, module_id in PLAYER_MODULE_MAP:
        pmod.create_leaf(src_name, f"→ modules/{module_id}/",
                         run_fn=_make_module_fn(src_name, full))

    player.create_leaf("MediaGenerator", "单文件发布",
                       run_fn=lambda: dotnet_publish(MEDIA_GEN_PROJ, MEDIA_GEN_PUBLISH,
                                                     single_file=True))

    player.create_leaf("Assemble", "组装 playerbuild/",
                       run_fn=_player_assemble)

    # Flutter GUI
    if not skip_flutter:
        fg = player.create_group("Flutter GUI", "Windows 桌面端 → playerbuild/")
        fg.create_leaf("gen-l10n", "",
                       run_fn=lambda: run_cmd(["flutter", "gen-l10n"],
                                              cwd=PLAYER_FLUTTER_DIR))
        fg.create_leaf("build windows --release", "编译 + 复制到 playerbuild/",
                       run_fn=_flutter_gui_build)

    player.create_leaf("Version Info", "写入 version_info.json",
                       run_fn=_player_version)

    return root


# ════════════════════════════════════════════
#  辅助: 原生插件叶子
# ════════════════════════════════════════════

def _add_native_leaves(parent: TaskNode, projects: list[str]):
    for proj in projects:
        parent.create_leaf(proj, f"build.bat {proj}",
                           run_fn=_make_native_fn(proj))


def _make_native_fn(proj: str):
    def _build():
        src = NATIVE_PLUGINS_DIR / proj
        if not (src / "build.bat").exists():
            info(f"SKIP {proj}: no build.bat")
            return TaskStatus.SKIPPED
        clean_cmake_cache(src)
        args = ["build.bat"]
        if proj in ("netease_bridge", "qqmusic_bridge", "kugou_bridge"):
            args.append("--no-pause")
        code = run_cmd(args, cwd=src)
        if code == 0 and proj == "kugou_bridge":
            dll = src / "ChillKugou.dll"
            if dll.exists():
                dst = PLAYER_DIR / "modules" / "Kugou" / "native" / "x64"
                dst.mkdir(parents=True, exist_ok=True)
                copy_file(dll, dst)
        return code
    return _build


# ════════════════════════════════════════════
#  共享
# ════════════════════════════════════════════

def _build_omni_pcm() -> int:
    src = NATIVE_PLUGINS_DIR / "OmniPcmShared"
    clean_cmake_cache(src)
    code = run_cmd(["build.bat"], cwd=src)
    if code == 0:
        # stage 到 bin/native/x64/
        dll_src = src / "build" / "x64" / "bin" / "Release" / "OmniPcmShared.dll"
        if dll_src.exists():
            OMNI_PCM_DLL.parent.mkdir(parents=True, exist_ok=True)
            copy_file(dll_src, OMNI_PCM_DLL.parent)
            info("  OmniPcmShared.dll staged")
    return code


# ════════════════════════════════════════════
#  Mod
# ════════════════════════════════════════════

def _make_mod_clean(full: bool):
    def _clean():
        if MOD_RELEASE.exists():
            shutil.rmtree(MOD_RELEASE)
        if full:
            for d in [MOD_DIR / "bin", MOD_DIR / "obj"]:
                if d.exists():
                    shutil.rmtree(d)
        MOD_RELEASE.mkdir(parents=True, exist_ok=True)
        (MOD_RELEASE / "native" / "x64").mkdir(parents=True, exist_ok=True)
        (MOD_RELEASE / "SDK").mkdir(exist_ok=True)
        info("release/ChillPatcher cleaned")
        return True
    return _clean


def _mod_restore() -> int:
    for proj in [MOD_SDK_PROJ, MOD_MAIN_PROJ, MOD_ONEJS_PROJ]:
        if proj.exists():
            code = dotnet_restore(proj)
            if code != 0:
                return code
    return 0


def _make_ui_fn(ui_dir):
    def _build():
        name = ui_dir.name
        if not ui_dir.exists():
            info(f"SKIP {name}: dir not found")
            return TaskStatus.SKIPPED
        if run_cmd(["where", "npm"]) != 0:
            info("npm not found, skipping esbuild")
            return TaskStatus.SKIPPED
        if not (ui_dir / "node_modules").exists():
            info(f"  npm install for {name}...")
            if run_cmd(["npm", "install"], cwd=ui_dir) != 0:
                return 1
        return run_cmd(["npm", "run", "build"], cwd=ui_dir)
    return _build


def _mod_assemble() -> bool:
    mod_bin = MOD_DIR / "bin"

    # 主插件
    for f in mod_bin.glob("ChillPatcher.*"):
        if f.suffix in (".dll", ".config") and "SDK" not in f.stem:
            copy_file(f, MOD_RELEASE)

    # SDK
    sdk_bin = MOD_DIR / "bin" / "SDK"
    if sdk_bin.exists():
        for f in sdk_bin.glob("*.dll"):
            copy_file(f, MOD_RELEASE / "SDK")

    # 原生 DLL (排除后端桥接)
    native_src = ROOT / "bin" / "native" / "x64"
    native_dst = MOD_RELEASE / "native" / "x64"
    native_exclude = {"ChillNetease.dll", "ChillQQMusic.dll", "ChillKugou.dll"}
    if native_src.exists():
        native_dst.mkdir(parents=True, exist_ok=True)
        for f in native_src.glob("*.dll"):
            if f.name not in native_exclude:
                copy_file(f, native_dst)

    # VC++ 运行时
    lib_dir = MOD_DIR / "lib"
    if lib_dir.exists():
        for f in lib_dir.glob("*.dll"):
            copy_file(f, native_dst)

    # puerts.dll
    puerts = MOD_DIR / "ChillPatcher.OneJS" / "native" / "x64" / "puerts.dll"
    if puerts.exists():
        copy_file(puerts, native_dst)

    # RIME
    _assemble_rime()
    info("Mod files assembled")
    return True


def _assemble_rime():
    rime_dir = NATIVE_PLUGINS_DIR / "rime"
    rime_dll = rime_dir / "librime" / "build" / "bin" / "Release" / "rime.dll"
    if rime_dll.exists():
        copy_file(rime_dll, MOD_RELEASE)

    rd = MOD_RELEASE / "rime-data"
    rs = rd / "shared"
    ro = rs / "opencc"
    for d in [rs, ro, rd / "user"]:
        d.mkdir(parents=True, exist_ok=True)

    def _cp(src_dir, *files):
        for f in files:
            s = src_dir / f
            if s.exists():
                copy_file(s, rs)

    _cp(rime_dir / "rime-schemas" / "prelude",
        "symbols.yaml", "punctuation.yaml", "key_bindings.yaml")
    _cp(rime_dir / "RimeDefaultConfig", "default.yaml", "luna_pinyin.custom.yaml")

    essay = rime_dir / "rime-schemas" / "essay" / "essay.txt"
    if essay.exists():
        copy_file(essay, rs)

    _cp(rime_dir / "rime-schemas" / "luna-pinyin",
        "luna_pinyin.schema.yaml", "luna_pinyin.dict.yaml", "pinyin.yaml")
    _cp(rime_dir / "rime-schemas" / "stroke",
        "stroke.schema.yaml", "stroke.dict.yaml")
    _cp(rime_dir / "rime-schemas" / "double-pinyin",
        "double_pinyin.schema.yaml", "double_pinyin_abc.schema.yaml",
        "double_pinyin_flypy.schema.yaml", "double_pinyin_mspy.schema.yaml")

    opencc_src = rime_dir / "librime" / "share" / "opencc"
    if opencc_src.exists():
        for f in opencc_src.glob("*.json"):
            copy_file(f, ro)
        for f in opencc_src.glob("*.ocd2"):
            copy_file(f, ro)
    info("  RIME data assembled")


def _mod_zip_and_version() -> bool:
    ok = package_zip(MOD_RELEASE, MOD_RELEASE_ZIP, MOD_FLUTTER_ASSET)

    from build_config import PLAYER_FLUTTER_DIR
    data = read_version_info(MOD_DIR, PLAYER_FLUTTER_DIR, MOD_DIR,
                             MOD_DIR / "MyPluginInfo.cs")
    write_version_json(data, MOD_FLUTTER_ASSET.parent / "version_info.json")
    return ok


# ════════════════════════════════════════════
#  FH6
# ════════════════════════════════════════════

def _fh6_package() -> bool:
    if not FH6_BIN.exists():
        info(f"  ERROR: {FH6_BIN} missing")
        return False
    if FH6_STAGE.exists():
        shutil.rmtree(FH6_STAGE)
    FH6_STAGE.mkdir(parents=True, exist_ok=True)
    copy_file(FH6_BIN, FH6_STAGE)
    if OMNI_PCM_DLL.exists():
        copy_file(OMNI_PCM_DLL, FH6_STAGE)
    readme = FH6_DIR / "README.md"
    if readme.exists():
        copy_file(readme, FH6_STAGE)
    return package_zip(FH6_STAGE, FH6_ZIP, FH6_FLUTTER_ASSETS)


# ════════════════════════════════════════════
#  Player
# ════════════════════════════════════════════

def _player_clean() -> bool:
    _rmtree_ignore_locked(PLAYER_BUILD)
    (PLAYER_BUILD / "modules").mkdir(parents=True, exist_ok=True)
    (PLAYER_BUILD / "native" / "x64").mkdir(parents=True, exist_ok=True)
    info("playerbuild cleaned")
    return True


def _flutter_web_build() -> int:
    code = run_cmd(["flutter", "build", "web", "--wasm", "-t", "lib/main_web.dart"],
                   cwd=PLAYER_FLUTTER_DIR)
    if code != 0:
        return code
    _rmtree_ignore_locked(PLAYER_WWWROOT)
    PLAYER_WWWROOT.mkdir(parents=True, exist_ok=True)
    copy_dir_contents(PLAYER_FLUTTER_WEB_BUILD, PLAYER_WWWROOT)
    info("  Flutter Web → wwwroot/")
    return 0


def _publish_backend() -> int:
    if PLAYER_BACKEND_PUBLISH.exists():
        shutil.rmtree(PLAYER_BACKEND_PUBLISH)
    return dotnet_publish(PLAYER_BACKEND_PROJ, PLAYER_BACKEND_PUBLISH,
                          single_file=True)


def _make_module_fn(src_name: str, full: bool):
    def _build():
        proj = PLAYER_DIR / "modules" / src_name / f"ChillPatcher.Module.{src_name}.csproj"
        if not proj.exists():
            info(f"  SKIP: project not found")
            return TaskStatus.SKIPPED
        if full:
            code = dotnet_restore(proj)
            if code != 0:
                return code
        return dotnet_build(proj)
    return _build


def _player_assemble() -> bool:
    # Backend
    info("Backend...")
    if PLAYER_BACKEND_PUBLISH.exists():
        copy_dir_contents(PLAYER_BACKEND_PUBLISH, PLAYER_BUILD)
        _rmtree_ignore_locked(PLAYER_BUILD / "modules")
    else:
        info("  WARNING: Backend publish output not found")

    # Native decoders
    native_src = ROOT / "bin" / "native" / "x64"
    if native_src.exists():
        native_dst = PLAYER_BUILD / "native" / "x64"
        native_dst.mkdir(parents=True, exist_ok=True)
        for dll in ["OmniAudioDecoder.dll", "ChillAudioDecoder.dll",
                     "ChillFlacDecoder.dll", "OmniPcmShared.dll"]:
            s = native_src / dll
            if s.exists():
                copy_file(s, native_dst)
        info("  Native decoders copied")

    # MediaGenerator
    if MEDIA_GEN_PUBLISH.exists():
        for f in MEDIA_GEN_PUBLISH.glob("chill-gen-media.exe"):
            copy_file(f, PLAYER_BUILD)
        for f in MEDIA_GEN_PUBLISH.glob("*.pdb"):
            copy_file(f, PLAYER_BUILD)
        for cfg in ["config.json"]:
            s = MEDIA_GEN_PUBLISH / cfg
            if s.exists():
                copy_file(s, PLAYER_BUILD)

    # Modules
    info("Modules...")
    for src_name, module_id in PLAYER_MODULE_MAP:
        src_dir = PLAYER_MODULES_BUILD / src_name
        dst_dir = PLAYER_BUILD / "modules" / module_id
        if not src_dir.exists():
            info(f"  WARNING: {src_name} output not found")
            continue
        info(f"  {src_name} → modules/{module_id}/")
        dst_dir.mkdir(parents=True, exist_ok=True)
        for ext in ("*.dll", "*.json", "*.png"):
            for f in src_dir.glob(ext):
                copy_file(f, dst_dir)
        for src_native in [src_dir / "native" / "x64"]:
            if src_native.exists():
                dn = dst_dir / "native" / "x64"
                dn.mkdir(parents=True, exist_ok=True)
                for f in src_native.iterdir():
                    if f.suffix in (".dll", ".exe"):
                        copy_file(f, dn)
        src_module = PLAYER_DIR / "modules" / src_name
        for src_native in [src_module / "native" / "x64"]:
            if src_native.exists():
                dn = dst_dir / "native" / "x64"
                dn.mkdir(parents=True, exist_ok=True)
                for f in src_native.iterdir():
                    if f.suffix in (".dll", ".exe"):
                        copy_file(f, dn)

    # Cleanup
    _rmtree_ignore_locked(PLAYER_BUILD / "runtimes")
    for pat in ("*.pdb", "*.xml", "*.deps.json"):
        for f in PLAYER_BUILD.rglob(pat):
            try:
                f.unlink()
            except Exception:
                pass
    info("  Cleanup done")
    info("Player assembled")
    return True


def _flutter_gui_build() -> int:
    code = run_cmd(["flutter", "build", "windows", "--release"],
                   cwd=PLAYER_FLUTTER_DIR)
    if code != 0:
        return code

    # 复制 OmniPcmShared 和 Flutter 产物到 playerbuild
    if OMNI_PCM_DLL.exists():
        copy_file(OMNI_PCM_DLL, PLAYER_FLUTTER_BUILD)

    if PLAYER_FLUTTER_BUILD.exists():
        for item in PLAYER_FLUTTER_BUILD.iterdir():
            dst = PLAYER_BUILD / item.name
            if item.is_dir():
                shutil.copytree(item, dst, dirs_exist_ok=True)
            else:
                copy_file(item, PLAYER_BUILD)
    if OMNI_PCM_DLL.exists():
        copy_file(OMNI_PCM_DLL, PLAYER_BUILD)
    info("  Flutter GUI → playerbuild/")
    return 0


def _player_version() -> bool:
    fh6_file = FH6_DIR / "src" / "bridge.cpp"
    data = read_version_info(MOD_DIR, PLAYER_FLUTTER_DIR, FH6_DIR, fh6_file)
    write_version_json(data,
                       PLAYER_BUILD / "version_info.json",
                       PLAYER_FLUTTER_DIR / "assets" / "version_info.json")
    return True
