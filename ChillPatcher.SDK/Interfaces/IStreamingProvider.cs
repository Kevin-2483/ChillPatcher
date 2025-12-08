using System;
using System.Threading;
using System.Threading.Tasks;

namespace ChillPatcher.SDK.Interfaces
{
    #region 核心枚举

    /// <summary>
    /// 音频来源类型
    /// </summary>
    public enum PlayableSourceType
    {
        /// <summary>本地文件</summary>
        Local = 0,
        /// <summary>缓存文件</summary>
        Cached = 1,
        /// <summary>远程 URL（Unity 原生支持的格式）</summary>
        Remote = 2,
        /// <summary>PCM 数据流（模块提供解码后的 PCM 数据）</summary>
        PcmStream = 3
    }

    /// <summary>
    /// 音频格式
    /// </summary>
    public enum AudioFormat
    {
        Unknown = 0,
        Mp3 = 1,
        Ogg = 2,
        Wav = 3,
        Flac = 4,
        Aac = 5
    }

    /// <summary>
    /// 音质等级
    /// </summary>
    public enum AudioQuality
    {
        /// <summary>标准 (128kbps)</summary>
        Standard = 0,
        /// <summary>较高 (192kbps)</summary>
        Higher = 1,
        /// <summary>极高 (320kbps)</summary>
        ExHigh = 2,
        /// <summary>无损 (FLAC)</summary>
        Lossless = 3,
        /// <summary>Hi-Res (24bit)</summary>
        HiRes = 4,
        /// <summary>高清环绕声</summary>
        JYEffect = 5,
        /// <summary>沉浸环绕声</summary>
        Sky = 6,
        /// <summary>超清母带</summary>
        JYMaster = 7
    }

    #endregion

    #region PCM 流式接口

    /// <summary>
    /// PCM 音频流信息
    /// 用于模块提供解码后的 PCM 数据
    /// </summary>
    public class PcmStreamInfo
    {
        /// <summary>采样率 (Hz)</summary>
        public int SampleRate { get; set; }

        /// <summary>声道数</summary>
        public int Channels { get; set; }

        /// <summary>总 PCM 帧数（0 表示未知/流式）</summary>
        public ulong TotalFrames { get; set; }

        /// <summary>音频格式 ("mp3", "flac" 等)</summary>
        public string Format { get; set; }

        /// <summary>是否支持 Seek</summary>
        public bool CanSeek { get; set; }

        /// <summary>时长（秒），0 表示未知</summary>
        public float Duration => TotalFrames > 0 && SampleRate > 0 
            ? (float)TotalFrames / SampleRate 
            : 0f;
    }

    /// <summary>
    /// PCM 数据读取器接口
    /// 模块实现此接口来提供流式解码的 PCM 数据
    /// 
    /// 使用场景：
    /// - 流媒体 FLAC（模块负责下载 + 解码）
    /// - 其他 Unity 不原生支持的格式
    /// 
    /// 工作流程：
    /// 1. 模块从 URL 下载数据到缓存
    /// 2. 模块使用解码器（如 dr_flac）解码
    /// 3. 主插件调用 ReadFrames 获取 PCM 数据
    /// 4. 主插件使用 PCMReaderCallback 填充 AudioClip
    /// </summary>
    public interface IPcmStreamReader : IDisposable
    {
        /// <summary>PCM 流信息</summary>
        PcmStreamInfo Info { get; }

        /// <summary>当前帧位置</summary>
        ulong CurrentFrame { get; }

        /// <summary>是否已到达末尾</summary>
        bool IsEndOfStream { get; }

        /// <summary>是否已准备好读取（有足够的缓冲数据）</summary>
        bool IsReady { get; }

        /// <summary>
        /// 读取 PCM 帧
        /// </summary>
        /// <param name="buffer">交错格式的 float 缓冲区（长度 = 帧数 * 声道数）</param>
        /// <param name="framesToRead">要读取的帧数</param>
        /// <returns>实际读取的帧数，-1 表示错误</returns>
        long ReadFrames(float[] buffer, int framesToRead);

        /// <summary>
        /// 定位到指定帧
        /// </summary>
        /// <param name="frameIndex">目标帧索引</param>
        /// <returns>是否成功（或已设置延迟 Seek）</returns>
        bool Seek(ulong frameIndex);

        #region Seek 支持（流媒体扩展）

        /// <summary>
        /// 是否支持 Seek 操作
        /// 流媒体在缓存下载完成前可能不支持
        /// </summary>
        bool CanSeek { get; }

        /// <summary>
        /// 是否有待定的 Seek 操作（缓存未完成时设置的延迟 Seek）
        /// </summary>
        bool HasPendingSeek { get; }

        /// <summary>
        /// 待定 Seek 的目标帧（-1 表示无待定）
        /// </summary>
        long PendingSeekFrame { get; }

        /// <summary>
        /// 取消待定的 Seek 操作
        /// </summary>
        void CancelPendingSeek();

        #endregion

        #region 缓存进度（流媒体扩展）

        /// <summary>
        /// 缓存下载进度（0-100）
        /// 边下边播时，可用于显示缓冲进度
        /// 如果不支持或不适用，返回 -1
        /// </summary>
        double CacheProgress { get; }

        /// <summary>
        /// 缓存是否下载完成
        /// </summary>
        bool IsCacheComplete { get; }

        #endregion
    }

    #endregion

    #region 核心数据结构

    /// <summary>
    /// 可播放的音频源
    /// 参考 go-musicfox 的 PlayableSource 设计
    /// </summary>
    public class PlayableSource
    {
        /// <summary>歌曲 UUID</summary>
        public string UUID { get; set; }

        /// <summary>来源类型</summary>
        public PlayableSourceType SourceType { get; set; }

        /// <summary>本地文件路径（Local/Cached 类型使用）</summary>
        public string LocalPath { get; set; }

        /// <summary>远程 URL（Remote 类型使用）</summary>
        public string Url { get; set; }

        /// <summary>
        /// PCM 数据读取器（PcmStream 类型使用）
        /// 模块负责创建和管理，主插件只读取数据
        /// </summary>
        public IPcmStreamReader PcmReader { get; set; }

        /// <summary>音频格式</summary>
        public AudioFormat Format { get; set; }

        /// <summary>音质等级</summary>
        public AudioQuality Quality { get; set; }

        /// <summary>URL 过期时间（Remote 类型使用，null 表示不过期）</summary>
        public DateTime? ExpiresAt { get; set; }

        /// <summary>文件大小 (bytes)，可选</summary>
        public long? FileSize { get; set; }

        /// <summary>检查 URL 是否已过期</summary>
        public bool IsExpired => ExpiresAt.HasValue && DateTime.UtcNow >= ExpiresAt.Value;

        /// <summary>检查 URL 是否即将过期（提前 30 秒）</summary>
        public bool IsAboutToExpire => ExpiresAt.HasValue && DateTime.UtcNow >= ExpiresAt.Value.AddSeconds(-30);

        /// <summary>是否是远程资源</summary>
        public bool IsRemote => SourceType == PlayableSourceType.Remote;

        /// <summary>是否是 PCM 流</summary>
        public bool IsPcmStream => SourceType == PlayableSourceType.PcmStream;

        /// <summary>获取实际路径（本地路径或 URL）</summary>
        public string GetPath() => IsRemote ? Url : LocalPath;

        /// <summary>
        /// 创建一个本地文件源
        /// </summary>
        public static PlayableSource FromLocal(string uuid, string path, AudioFormat format = AudioFormat.Unknown)
        {
            return new PlayableSource
            {
                UUID = uuid,
                SourceType = PlayableSourceType.Local,
                LocalPath = path,
                Format = format != AudioFormat.Unknown ? format : AudioFormatExtensions.FromExtension(System.IO.Path.GetExtension(path))
            };
        }

        /// <summary>
        /// 创建一个远程 URL 源
        /// </summary>
        public static PlayableSource FromUrl(string uuid, string url, AudioFormat format, DateTime? expiresAt = null)
        {
            return new PlayableSource
            {
                UUID = uuid,
                SourceType = PlayableSourceType.Remote,
                Url = url,
                Format = format,
                ExpiresAt = expiresAt
            };
        }

        /// <summary>
        /// 创建一个 PCM 流源
        /// </summary>
        public static PlayableSource FromPcmStream(string uuid, IPcmStreamReader reader, AudioFormat originalFormat = AudioFormat.Flac)
        {
            return new PlayableSource
            {
                UUID = uuid,
                SourceType = PlayableSourceType.PcmStream,
                PcmReader = reader,
                Format = originalFormat
            };
        }
    }

    #endregion

    #region 核心接口

    /// <summary>
    /// 可播放源解析器接口
    /// 模块实现此接口来提供音频播放源
    /// 
    /// 设计原则：
    /// - 主插件只负责 URL 播放
    /// - 认证、缓存等逻辑由模块自己处理
    /// - 保持简单：解析 → 获取 URL → 播放
    /// </summary>
    public interface IPlayableSourceResolver
    {
        /// <summary>
        /// 解析可播放源
        /// 模块内部处理优先级：本地文件 → 缓存 → 远程获取
        /// </summary>
        /// <param name="uuid">歌曲 UUID</param>
        /// <param name="quality">期望的音质等级</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>可播放源，失败返回 null</returns>
        Task<PlayableSource> ResolveAsync(
            string uuid,
            AudioQuality quality = AudioQuality.ExHigh,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 刷新远程 URL（当 URL 过期时调用）
        /// </summary>
        /// <param name="uuid">歌曲 UUID</param>
        /// <param name="quality">期望的音质等级</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>新的可播放源，失败返回 null</returns>
        Task<PlayableSource> RefreshUrlAsync(
            string uuid,
            AudioQuality quality = AudioQuality.ExHigh,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// 流媒体音乐源提供器
    /// 继承 IMusicSourceProvider，增加 URL 解析能力
    /// 
    /// 模块职责：
    /// - 注册固定的流媒体歌单
    /// - 处理登录认证（模块内部解决）
    /// - 提供 URL 解析
    /// </summary>
    public interface IStreamingMusicSourceProvider : IMusicSourceProvider, IPlayableSourceResolver
    {
        /// <summary>
        /// 是否已就绪（如登录状态等）
        /// 主插件可以根据此状态显示/隐藏该模块的内容
        /// </summary>
        bool IsReady { get; }

        /// <summary>
        /// 就绪状态变化事件
        /// </summary>
        event Action<bool> OnReadyStateChanged;
    }

    #endregion

    #region 辅助扩展

    /// <summary>
    /// AudioFormat 扩展方法
    /// </summary>
    public static class AudioFormatExtensions
    {
        /// <summary>
        /// 从文件扩展名解析音频格式
        /// </summary>
        public static AudioFormat FromExtension(string extension)
        {
            if (string.IsNullOrEmpty(extension))
                return AudioFormat.Unknown;

            extension = extension.TrimStart('.').ToLowerInvariant();
            return extension switch
            {
                "mp3" => AudioFormat.Mp3,
                "ogg" => AudioFormat.Ogg,
                "wav" => AudioFormat.Wav,
                "flac" => AudioFormat.Flac,
                "aac" or "m4a" => AudioFormat.Aac,
                _ => AudioFormat.Unknown
            };
        }

        /// <summary>
        /// 获取格式的文件扩展名
        /// </summary>
        public static string ToExtension(this AudioFormat format)
        {
            return format switch
            {
                AudioFormat.Mp3 => ".mp3",
                AudioFormat.Ogg => ".ogg",
                AudioFormat.Wav => ".wav",
                AudioFormat.Flac => ".flac",
                AudioFormat.Aac => ".aac",
                _ => ""
            };
        }

        /// <summary>
        /// 检查格式是否被 Unity 原生支持（不需要自定义解码器）
        /// </summary>
        public static bool IsUnitySupportedNatively(this AudioFormat format)
        {
            return format switch
            {
                AudioFormat.Mp3 or AudioFormat.Ogg or AudioFormat.Wav or AudioFormat.Aac => true,
                AudioFormat.Flac => false, // 需要自定义解码器
                _ => false
            };
        }
    }

    #endregion
}
