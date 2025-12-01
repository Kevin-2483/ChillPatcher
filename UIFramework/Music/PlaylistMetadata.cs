using System;
using Newtonsoft.Json;

namespace ChillPatcher.UIFramework.Music
{
    /// <summary>
    /// 歌单元数据（playlist.json）- 仅用于用户自定义歌单名称
    /// </summary>
    [Serializable]
    public class PlaylistMetadata
    {
        /// <summary>
        /// 格式版本
        /// </summary>
        [JsonProperty("version")]
        public int Version { get; set; } = 2;

        /// <summary>
        /// 歌单显示名称（用户可自定义）
        /// </summary>
        [JsonProperty("displayName")]
        public string DisplayName { get; set; }

        /// <summary>
        /// 歌单描述（可选）
        /// </summary>
        [JsonProperty("description")]
        public string Description { get; set; } = "";
    }

    /// <summary>
    /// 专辑元数据（album.json）- 仅用于用户自定义专辑名称
    /// </summary>
    [Serializable]
    public class AlbumMetadata
    {
        /// <summary>
        /// 格式版本
        /// </summary>
        [JsonProperty("version")]
        public int Version { get; set; } = 1;

        /// <summary>
        /// 专辑显示名称（用户可自定义）
        /// </summary>
        [JsonProperty("displayName")]
        public string DisplayName { get; set; }

        /// <summary>
        /// 专辑描述（可选）
        /// </summary>
        [JsonProperty("description")]
        public string Description { get; set; } = "";
    }
}
