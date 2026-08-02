# -*- coding: utf-8 -*-
"""
ChillPatcher Mod 构建任务树
"""
import shutil

from build_config import (
    MOD_DIR, MOD_RELEASE, MOD_SDK_PROJ, MOD_MAIN_PROJ, MOD_ONEJS_PROJ,
    MOD_UI_DIRS, MOD_RELEASE_ZIP, MOD_FLUTTER_ASSET,
    NATIVE_PLUGINS_DIR, OMNI_PCM_DLL, ROOT, FH6_DIR,
)
from .base import TaskNode, TaskStatus
from .common import (
    copy_file, copy_dir_contents, dotnet_build, dotnet_restore,
    info, run_cmd, read_version_info, write_version_json, package_zip,
)


def create_mod_tasks(full: bool = False) -> TaskNode:
    """创建 ChillPatcher Mod 完整构建任务树。"""
    root = TaskNode("ChillPatcher Mod", "BepInEx 插件 - 输出到 release/ChillPatcher/")

    # ── Clean ──
    root.create_leaf("Clean", "清理 release/ChillPatcher/", run_fn=_make_clean(full))

    # ── Restore NuGet ──
    if full:
        root.create_leaf("Restore NuGet", "恢复所有 NuGet 包", run_fn=_restore_all)

    # ── SDK ──
    root.create_leaf("ChillPatcher.SDK", "构建 SDK 项目", run_fn=_build_sdk)

    # ── Main Plugin ──
    root.create_leaf("Main Plugin", "构建主插件 ChillPatcher.dll", run_fn=_build_main)

    # ── OneJS ──
    root.create_leaf("OneJS", "构建 ChillPatcher.OneJS", run_fn=_build_onejs)

    # ── UI (esbuild) ──
    ui_group = root.create_group("UI (esbuild)", "Preact UI 打包")
    for ui_dir in MOD_UI_DIRS:
        ui_group.create_leaf(
            f"UI: {ui_dir.name}",
            f"esbuild 打包 {ui_dir.name}",
            run_fn=_make_ui_build_fn(ui_dir),
        )

    # ── Native Plugins ──
    if full:
        from .native import create_native_tasks, create_stage_omni_pcm
        native_group = root.create_group("Native Plugins", "原生 C++ 插件编译")
        create_native_tasks(native_group, [
            "OmniAudioDecoder", "OmniPcmShared", "SpotifyLibrespotBridge",
            "EsbuildBridge", "SmtcBridge", "netease_bridge", "qqmusic_bridge",
            "kugou_bridge",
        ])
        create_stage_omni_pcm(root)

    # ── Assemble ──
    root.create_leaf("Assemble", "组装发布目录", run_fn=_assemble)

    # ── Package ZIP ──
    root.create_leaf("Package ZIP", "打包 ChillPatcher.zip → Flutter assets",
                     run_fn=_package)

    # ── Version Info ──
    root.create_leaf("Version Info", "写入 version_info.json",
                     run_fn=_write_version)

    return root


# ── 内部执行函数 ──

def _make_clean(full: bool):
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


def _restore_all() -> int:
    for proj in [MOD_SDK_PROJ, MOD_MAIN_PROJ, MOD_ONEJS_PROJ]:
        if proj.exists():
            code = dotnet_restore(proj)
            if code != 0:
                return code
    return 0


def _build_sdk() -> int:
    return dotnet_build(MOD_SDK_PROJ)


def _build_main() -> int:
    return dotnet_build(MOD_MAIN_PROJ)


def _build_onejs() -> int:
    return dotnet_build(MOD_ONEJS_PROJ)


def _make_ui_build_fn(ui_dir):
    def _build():
        name = ui_dir.name
        if not ui_dir.exists():
            info(f"SKIP {name}: directory not found")
            return TaskStatus.SKIPPED
        code = run_cmd(["where", "npm"])
        if code != 0:
            info("npm not found! Skipping esbuild.")
            return TaskStatus.SKIPPED
        if not (ui_dir / "node_modules").exists():
            info(f"  Installing npm deps for {name}...")
            code = run_cmd(["npm", "install"], cwd=ui_dir)
            if code != 0:
                info(f"  ERROR: npm install failed for {name}")
                return code
        code = run_cmd(["npm", "run", "build"], cwd=ui_dir)
        if code != 0:
            info(f"  ERROR: esbuild failed for {name}")
        return code
    return _build


def _assemble() -> bool:
    mod_bin = MOD_DIR / "bin"

    # 主插件 DLL + config
    for f in mod_bin.glob("ChillPatcher.*"):
        if f.suffix in (".dll", ".config") and "SDK" not in f.stem:
            copy_file(f, MOD_RELEASE)

    # SDK
    sdk_bin = MOD_DIR / "bin" / "SDK"
    if sdk_bin.exists():
        for f in sdk_bin.glob("*.dll"):
            copy_file(f, MOD_RELEASE / "SDK")

    # 原生 DLL
    native_src = ROOT / "bin" / "native" / "x64"
    native_dst = MOD_RELEASE / "native" / "x64"
    native_exclude = {"ChillNetease.dll", "ChillQQMusic.dll", "ChillKugou.dll"}
    if native_src.exists():
        native_dst.mkdir(parents=True, exist_ok=True)
        for f in native_src.glob("*.dll"):
            if f.name not in native_exclude:
                copy_file(f, native_dst)

    # VC++ 运行时 DLL
    lib_dir = MOD_DIR / "lib"
    if lib_dir.exists():
        for f in lib_dir.glob("*.dll"):
            copy_file(f, native_dst)

    # puerts.dll
    puerts = MOD_DIR / "ChillPatcher.OneJS" / "native" / "x64" / "puerts.dll"
    if puerts.exists():
        copy_file(puerts, native_dst)

    # RIME 输入法
    _assemble_rime()
    info("Mod files assembled")
    return True


def _assemble_rime():
    """组装 RIME 输入法引擎数据。"""
    rime_dir = NATIVE_PLUGINS_DIR / "rime"

    # rime.dll
    rime_dll = rime_dir / "librime" / "build" / "bin" / "Release" / "rime.dll"
    if rime_dll.exists():
        copy_file(rime_dll, MOD_RELEASE)

    # 目录结构
    rime_data = MOD_RELEASE / "rime-data"
    rime_shared = rime_data / "shared"
    rime_opencc = rime_shared / "opencc"
    for d in [rime_shared, rime_opencc, rime_data / "user"]:
        d.mkdir(parents=True, exist_ok=True)

    # prelude
    _copy_rime_files(rime_dir / "rime-schemas" / "prelude", rime_shared,
                     ["symbols.yaml", "punctuation.yaml", "key_bindings.yaml"])

    # default config
    _copy_rime_files(rime_dir / "RimeDefaultConfig", rime_shared,
                     ["default.yaml", "luna_pinyin.custom.yaml"])

    # essay
    essay = rime_dir / "rime-schemas" / "essay" / "essay.txt"
    if essay.exists():
        copy_file(essay, rime_shared)

    # luna_pinyin
    _copy_rime_files(rime_dir / "rime-schemas" / "luna-pinyin", rime_shared,
                     ["luna_pinyin.schema.yaml", "luna_pinyin.dict.yaml", "pinyin.yaml"])

    # stroke
    _copy_rime_files(rime_dir / "rime-schemas" / "stroke", rime_shared,
                     ["stroke.schema.yaml", "stroke.dict.yaml"])

    # double_pinyin
    _copy_rime_files(rime_dir / "rime-schemas" / "double-pinyin", rime_shared,
                     ["double_pinyin.schema.yaml", "double_pinyin_abc.schema.yaml",
                      "double_pinyin_flypy.schema.yaml", "double_pinyin_mspy.schema.yaml"])

    # OpenCC
    opencc_src = rime_dir / "librime" / "share" / "opencc"
    if opencc_src.exists():
        for f in opencc_src.glob("*.json"):
            copy_file(f, rime_opencc)
        for f in opencc_src.glob("*.ocd2"):
            copy_file(f, rime_opencc)

    info("  RIME input method data assembled")


def _copy_rime_files(src_dir, dst_dir, files):
    for fname in files:
        src = src_dir / fname
        if src.exists():
            copy_file(src, dst_dir)


def _package() -> bool:
    return package_zip(MOD_RELEASE, MOD_RELEASE_ZIP, MOD_FLUTTER_ASSET)


def _write_version() -> bool:
    from build_config import PLAYER_FLUTTER_DIR
    fh6_file = FH6_DIR / "src" / "bridge.cpp" if FH6_DIR else (MOD_DIR / "MyPluginInfo.cs")
    data = read_version_info(
        MOD_DIR, PLAYER_FLUTTER_DIR, FH6_DIR,
        fh6_file,
    )
    asset_ver = MOD_FLUTTER_ASSET.parent / "version_info.json"
    write_version_json(data, asset_ver)
    return True
