using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ChillPatcher.SDK.Models;

namespace ChillPatcher.SDK.Interfaces
{
    /// <summary>
    /// Tag 注册表接口
    /// </summary>
    public interface ITagRegistry
    {
        /// <summary>
        /// 注册一个新的 Tag
        /// Tag 是否为增长 Tag 取决于是否包含 IsGrowableAlbum=true 的专辑
        /// </summary>
        /// <param name="tagId">Tag 唯一标识符</param>
        /// <param name="displayName">显示名称</param>
        /// <param name="moduleId">所属模块 ID</param>
        /// <returns>注册成功的 Tag 信息</returns>
        TagInfo RegisterTag(string tagId, string displayName, string moduleId);

        /// <summary>
        /// 为 Tag 设置加载更多回调
        /// 当 Tag 被标记为增长 Tag 后，需要设置回调
        /// </summary>
        /// <param name="tagId">Tag ID</param>
        /// <param name="loadMoreCallback">加载更多回调</param>
        void SetLoadMoreCallback(string tagId, Func<Task<int>> loadMoreCallback);

        /// <summary>
        /// 标记 Tag 为增长 Tag (由 AlbumRegistry 在注册增长专辑时自动调用)
        /// 一个 Tag 只能有一个增长专辑，增长 Tag 互斥选中
        /// </summary>
        /// <param name="tagId">Tag ID</param>
        /// <param name="growableAlbumId">增长专辑 ID</param>
        void MarkAsGrowableTag(string tagId, string growableAlbumId);

        /// <summary>
        /// 注销 Tag
        /// </summary>
        /// <param name="tagId">Tag ID</param>
        void UnregisterTag(string tagId);

        /// <summary>
        /// 获取 Tag 信息
        /// </summary>
        TagInfo GetTag(string tagId);

        /// <summary>
        /// 获取所有已注册的 Tag
        /// </summary>
        IReadOnlyList<TagInfo> GetAllTags();

        /// <summary>
        /// 获取指定模块注册的所有 Tag
        /// </summary>
        IReadOnlyList<TagInfo> GetTagsByModule(string moduleId);

        /// <summary>
        /// 检查 Tag 是否已注册
        /// </summary>
        bool IsTagRegistered(string tagId);

        /// <summary>
        /// 根据位值获取 Tag 信息
        /// </summary>
        TagInfo GetTagByBitValue(ulong bitValue);

        /// <summary>
        /// 注销指定模块的所有 Tag
        /// </summary>
        void UnregisterAllByModule(string moduleId);

        /// <summary>
        /// 获取当前选中的增长列表 Tag (如果有)
        /// </summary>
        TagInfo GetCurrentGrowableTag();

        /// <summary>
        /// 获取所有增长列表 Tag
        /// </summary>
        IReadOnlyList<TagInfo> GetGrowableTags();
    }

    /// <summary>
    /// 专辑注册表接口
    /// </summary>
    public interface IAlbumRegistry
    {
        /// <summary>
        /// 注册专辑
        /// </summary>
        /// <param name="album">专辑信息</param>
        /// <param name="moduleId">所属模块 ID</param>
        void RegisterAlbum(AlbumInfo album, string moduleId);

        /// <summary>
        /// 注销专辑
        /// </summary>
        void UnregisterAlbum(string albumId);

        /// <summary>
        /// 获取专辑信息
        /// </summary>
        AlbumInfo GetAlbum(string albumId);

        /// <summary>
        /// 获取所有专辑
        /// </summary>
        IReadOnlyList<AlbumInfo> GetAllAlbums();

        /// <summary>
        /// 获取指定 Tag 下的所有专辑
        /// </summary>
        IReadOnlyList<AlbumInfo> GetAlbumsByTag(string tagId);

        /// <summary>
        /// 获取指定模块注册的所有专辑
        /// </summary>
        IReadOnlyList<AlbumInfo> GetAlbumsByModule(string moduleId);

        /// <summary>
        /// 检查专辑是否已注册
        /// </summary>
        bool IsAlbumRegistered(string albumId);

        /// <summary>
        /// 注销指定模块的所有专辑
        /// </summary>
        void UnregisterAllByModule(string moduleId);
    }

    /// <summary>
    /// 歌曲注册表接口
    /// </summary>
    public interface IMusicRegistry
    {
        /// <summary>
        /// 注册歌曲
        /// </summary>
        /// <param name="music">歌曲信息</param>
        /// <param name="moduleId">所属模块 ID</param>
        void RegisterMusic(MusicInfo music, string moduleId);

        /// <summary>
        /// 批量注册歌曲
        /// </summary>
        void RegisterMusicBatch(IEnumerable<MusicInfo> musicList, string moduleId);

        /// <summary>
        /// 注销歌曲
        /// </summary>
        void UnregisterMusic(string uuid);

        /// <summary>
        /// 获取歌曲信息
        /// </summary>
        MusicInfo GetMusic(string uuid);

        /// <summary>
        /// 获取所有歌曲
        /// </summary>
        IReadOnlyList<MusicInfo> GetAllMusic();

        /// <summary>
        /// 获取指定专辑下的所有歌曲
        /// </summary>
        IReadOnlyList<MusicInfo> GetMusicByAlbum(string albumId);

        /// <summary>
        /// 获取指定 Tag 下的所有歌曲
        /// </summary>
        IReadOnlyList<MusicInfo> GetMusicByTag(string tagId);

        /// <summary>
        /// 获取指定模块注册的所有歌曲
        /// </summary>
        IReadOnlyList<MusicInfo> GetMusicByModule(string moduleId);

        /// <summary>
        /// 检查歌曲是否已注册
        /// </summary>
        bool IsMusicRegistered(string uuid);

        /// <summary>
        /// 更新歌曲信息
        /// </summary>
        void UpdateMusic(MusicInfo music);

        /// <summary>
        /// 注销指定模块的所有歌曲
        /// </summary>
        void UnregisterAllByModule(string moduleId);
    }
}
