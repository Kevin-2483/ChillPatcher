using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ChillPatcher.SDK.Models;
using UnityEngine;

namespace ChillPatcher.SDK.Interfaces
{
    /// <summary>
    /// 音乐源提供器接口
    /// 模块实现此接口提供音乐列表和加载功能
    /// 
    /// 对于流媒体模块，应该实现 IStreamingMusicSourceProvider 接口
    /// </summary>
    public interface IMusicSourceProvider
    {
        /// <summary>
        /// 获取所有可用的音乐列表
        /// </summary>
        Task<List<MusicInfo>> GetMusicListAsync();

        /// <summary>
        /// 加载指定音乐的 AudioClip
        /// </summary>
        /// <param name="uuid">音乐 UUID</param>
        /// <returns>加载的 AudioClip</returns>
        Task<AudioClip> LoadAudioAsync(string uuid);

        /// <summary>
        /// 加载指定音乐的 AudioClip（支持取消）
        /// </summary>
        /// <param name="uuid">音乐 UUID</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>加载的 AudioClip</returns>
        Task<AudioClip> LoadAudioAsync(string uuid, CancellationToken cancellationToken);

        /// <summary>
        /// 卸载指定音乐的 AudioClip
        /// </summary>
        /// <param name="uuid">音乐 UUID</param>
        void UnloadAudio(string uuid);

        /// <summary>
        /// 刷新音乐列表
        /// </summary>
        Task RefreshAsync();

        /// <summary>
        /// 获取音乐源类型
        /// </summary>
        MusicSourceType SourceType { get; }
    }

    /// <summary>
    /// 封面提供器接口
    /// </summary>
    public interface ICoverProvider
    {
        /// <summary>
        /// 获取歌曲封面
        /// </summary>
        /// <param name="uuid">歌曲 UUID</param>
        /// <returns>封面 Sprite，如果没有返回 null</returns>
        Task<Sprite> GetMusicCoverAsync(string uuid);

        /// <summary>
        /// 获取专辑封面
        /// </summary>
        /// <param name="albumId">专辑 ID</param>
        /// <returns>封面 Sprite，如果没有返回 null</returns>
        Task<Sprite> GetAlbumCoverAsync(string albumId);
        
        /// <summary>
        /// 获取歌曲封面的原始字节数据（用于 SMTC 等需要字节数据的场景）
        /// </summary>
        /// <param name="uuid">歌曲 UUID</param>
        /// <returns>封面图片字节数组和 MIME 类型，如果没有返回 (null, null)</returns>
        Task<(byte[] data, string mimeType)> GetMusicCoverBytesAsync(string uuid);

        /// <summary>
        /// 清除封面缓存
        /// </summary>
        void ClearCache();

        /// <summary>
        /// 移除指定歌曲的封面缓存
        /// </summary>
        /// <param name="uuid">歌曲 UUID</param>
        void RemoveMusicCoverCache(string uuid);

        /// <summary>
        /// 移除指定专辑的封面缓存
        /// </summary>
        /// <param name="albumId">专辑 ID</param>
        void RemoveAlbumCoverCache(string albumId);
    }

    /// <summary>
    /// 收藏和排除处理器接口
    /// 模块实现此接口来管理收藏和排除状态
    /// </summary>
    public interface IFavoriteExcludeHandler
    {
        /// <summary>
        /// 检查歌曲是否已收藏
        /// </summary>
        bool IsFavorite(string uuid);

        /// <summary>
        /// 设置歌曲收藏状态
        /// </summary>
        void SetFavorite(string uuid, bool isFavorite);

        /// <summary>
        /// 检查歌曲是否已排除
        /// </summary>
        bool IsExcluded(string uuid);

        /// <summary>
        /// 设置歌曲排除状态
        /// </summary>
        void SetExcluded(string uuid, bool isExcluded);

        /// <summary>
        /// 获取所有收藏的歌曲 UUID
        /// </summary>
        IReadOnlyList<string> GetFavorites();

        /// <summary>
        /// 获取所有排除的歌曲 UUID
        /// </summary>
        IReadOnlyList<string> GetExcluded();
    }

    /// <summary>
    /// 删除处理器接口
    /// 模块实现此接口来支持删除功能
    /// </summary>
    public interface IDeleteHandler
    {
        /// <summary>
        /// 是否支持删除
        /// </summary>
        bool CanDelete { get; }

        /// <summary>
        /// 删除歌曲
        /// </summary>
        /// <param name="uuid">歌曲 UUID</param>
        /// <returns>是否删除成功</returns>
        bool Delete(string uuid);
    }
}
