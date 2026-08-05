# -*- coding: utf-8 -*-
"""
原生插件构建任务
"""
from pathlib import Path

from build_config import (
    NATIVE_PLUGINS_DIR, NATIVE_PROJECTS_ALWAYS, NATIVE_PROJECTS_FULL_ONLY,
    OMNI_PCM_DLL, PLAYER_DIR,
)
from .base import TaskNode, TaskStatus
from .common import clean_cmake_cache, copy_file, info, run_cmd


def create_native_tasks(parent: TaskNode, projects: list[str]) -> list[TaskNode]:
    """在 parent 下为每个原生插件创建子任务节点。"""
    leaves = []
    for proj in projects:
        leaf = parent.create_leaf(
            f"Native: {proj}",
            f"构建原生插件 {proj}",
            run_fn=_make_native_build_fn(proj),
        )
        leaves.append(leaf)
    return leaves


def create_native_always_group(parent: TaskNode) -> TaskNode:
    """创建「必须构建」的原生插件组。"""
    g = parent.create_group("Native Plugins (always)", "始终构建的原生插件")
    create_native_tasks(g, NATIVE_PROJECTS_ALWAYS)
    return g


def create_native_full_group(parent: TaskNode) -> TaskNode:
    """创建「完整构建才包含」的原生插件组。"""
    g = parent.create_group("Native Plugins (full only)", "仅 --full 时构建的原生插件")
    create_native_tasks(g, NATIVE_PROJECTS_FULL_ONLY)
    return g


def create_stage_omni_pcm(parent: TaskNode) -> TaskNode:
    """创建 OmniPcmShared.dll 统一存放任务。"""
    return parent.create_leaf(
        "Stage OmniPcmShared.dll",
        "复制 OmniPcmShared.dll 到 bin/native/x64/",
        run_fn=_stage_omni_pcm_dll,
    )


# ── 内部函数 ──

def _make_native_build_fn(proj: str):
    def _build():
        src = NATIVE_PLUGINS_DIR / proj
        build_script = src / "build.bat"
        if not build_script.exists():
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
        if code != 0:
            info(f"  WARNING: {proj} build failed (exit={code})")
        return code
    return _build


def _stage_omni_pcm_dll() -> int:
    src = NATIVE_PLUGINS_DIR / "OmniPcmShared" / "build" / "x64" / "bin" / "Release" / "OmniPcmShared.dll"
    dst = OMNI_PCM_DLL
    if src.exists():
        dst.parent.mkdir(parents=True, exist_ok=True)
        copy_file(src, dst.parent)
        info("  OmniPcmShared.dll staged to bin/native/x64/")
        return 0
    else:
        info(f"  WARNING: OmniPcmShared.dll not found at {src}")
        return 1
