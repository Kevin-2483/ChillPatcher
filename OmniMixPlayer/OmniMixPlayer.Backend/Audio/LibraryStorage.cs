using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LiteDB;
using Microsoft.Extensions.Logging;
using OmniMixPlayer.Backend.Storage;
using OmniMixPlayer.SDK.Protos.Models;

namespace OmniMixPlayer.Backend.Audio
{
    /// <summary>
    /// 音乐库 LiteDB 存储层
    /// 6 张表: tracks, albums, tags, track_tags, playlists, playlist_entries
    /// </summary>
    public sealed class LibraryStorage : IDisposable
    {
        private readonly LiteDatabase _db;
        private readonly ILogger _logger;

        // LiteDB 文档类型（映射 proto 消息）
        public sealed class TrackDoc
        {
            [BsonId] public string Uuid { get; set; }
            public string Title { get; set; }
            public string Artist { get; set; }
            public string AlbumId { get; set; }
            public float Duration { get; set; }
            public string ModuleId { get; set; }
            public int SourceType { get; set; }
            public string SourcePath { get; set; }
            public bool IsFavorite { get; set; }
            public bool IsExcluded { get; set; }
            public string CoverUri { get; set; }
            public int PlayCount { get; set; }
            public long CreatedAt { get; set; }
            public long? LastPlayedAt { get; set; }
            public byte[] ExtendedData { get; set; }
        }

        public sealed class AlbumDoc
        {
            [BsonId] public string Id { get; set; }
            public string Title { get; set; }
            public string Artist { get; set; }
            public string CoverUri { get; set; }
            public int Year { get; set; }
            public string ModuleId { get; set; }
        }

        public sealed class TagDoc
        {
            [BsonId] public string Id { get; set; }
            public string Name { get; set; }
            public string Color { get; set; }
            public string ModuleId { get; set; }
            public int Kind { get; set; }
        }

        public sealed class TrackTagDoc
        {
            [BsonId] public string Id { get; set; } // $"{TrackUuid}_{TagId}"
            public string TrackUuid { get; set; }
            public string TagId { get; set; }
        }

        public sealed class PlaylistDoc
        {
            [BsonId] public string Id { get; set; }
            public string Name { get; set; }
            public string ModuleId { get; set; }
            public int Kind { get; set; }
            public string CoverUri { get; set; }
            public int SortOrder { get; set; }
            public long CreatedAt { get; set; }
            public long UpdatedAt { get; set; }
        }

        public sealed class PlaylistEntryDoc
        {
            [BsonId] public string Id { get; set; }
            public string PlaylistId { get; set; }
            public string TrackUuid { get; set; }
            public int Position { get; set; }
            public long AddedAt { get; set; }
        }

        public LibraryStorage(string configBaseDir, ILogger logger = null)
        {
            _logger = logger;

            var dbDir = string.IsNullOrEmpty(configBaseDir)
                ? AppDomain.CurrentDomain.BaseDirectory
                : configBaseDir;

            if (!Directory.Exists(dbDir))
                Directory.CreateDirectory(dbDir);

            var dbPath = Path.Combine(dbDir, "omnimix_library.db");
            EnsureDatabaseVersion(dbPath);

            _db = new LiteDatabase(dbPath);
            WriteDatabaseVersion();
            _logger?.LogInformation("LibraryStorage initialized at {Path}", dbPath);

            EnsureIndexes();
        }

        private void EnsureDatabaseVersion(string dbPath)
        {
            if (!File.Exists(dbPath)) return;

            try
            {
                using var db = new LiteDatabase(dbPath);
                var meta = db.GetCollection<BsonDocument>(StorageVersion.LiteDbCollection);
                var doc = meta.FindById(StorageVersion.LiteDbDocumentId);
                if (doc != null &&
                    doc.ContainsKey("version") &&
                    doc["version"].AsInt32 == StorageVersion.Current)
                {
                    return;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Library database version check failed; rebuilding {Path}", dbPath);
            }

            DeleteDatabaseFiles(dbPath);
            _logger?.LogInformation("Deleted incompatible library database; it will be rebuilt at {Path}", dbPath);
        }

        private void WriteDatabaseVersion()
        {
            var meta = _db.GetCollection<BsonDocument>(StorageVersion.LiteDbCollection);
            meta.Upsert(new BsonDocument
            {
                ["_id"] = StorageVersion.LiteDbDocumentId,
                ["version"] = StorageVersion.Current
            });
        }

        private static void DeleteDatabaseFiles(string dbPath)
        {
            foreach (var path in new[] { dbPath, $"{dbPath}-log", $"{dbPath}-shm", $"{dbPath}-wal" })
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
        }

        private void EnsureIndexes()
        {
            var tracks = _db.GetCollection<TrackDoc>("tracks");
            tracks.EnsureIndex(x => x.AlbumId);
            tracks.EnsureIndex(x => x.ModuleId);
            tracks.EnsureIndex(x => x.Title);

            var albums = _db.GetCollection<AlbumDoc>("albums");
            albums.EnsureIndex(x => x.ModuleId);

            var tags = _db.GetCollection<TagDoc>("tags");
            tags.EnsureIndex(x => x.ModuleId);

            var trackTags = _db.GetCollection<TrackTagDoc>("track_tags");
            trackTags.EnsureIndex(x => x.TrackUuid);
            trackTags.EnsureIndex(x => x.TagId);

            var playlists = _db.GetCollection<PlaylistDoc>("playlists");
            playlists.EnsureIndex(x => x.ModuleId);
            playlists.EnsureIndex(x => x.Kind);

            var playlistEntries = _db.GetCollection<PlaylistEntryDoc>("playlist_entries");
            playlistEntries.EnsureIndex(x => x.PlaylistId);
            playlistEntries.EnsureIndex(x => x.TrackUuid);
            playlistEntries.EnsureIndex(x => x.Position);
        }

        // ── Track ──

        public bool UpsertTrack(Track track)
        {
            var col = _db.GetCollection<TrackDoc>("tracks");
            var doc = ToTrackDoc(track);
            var existing = col.FindById(track.Uuid);
            col.Upsert(doc);
            return existing == null;
        }

        public int UpsertTracks(IEnumerable<Track> tracks)
        {
            var col = _db.GetCollection<TrackDoc>("tracks");
            int created = 0;
            foreach (var track in tracks)
            {
                var existed = col.FindById(track.Uuid) != null;
                col.Upsert(ToTrackDoc(track));
                if (!existed) created++;
            }
            return created;
        }

        public TrackDoc GetTrack(string uuid)
        {
            return _db.GetCollection<TrackDoc>("tracks").FindById(uuid);
        }

        public List<TrackDoc> QueryTracks(TrackQuery query)
        {
            return QueryTracksInternal(query, applyPaging: true);
        }

        public int CountTracks(TrackQuery query)
        {
            return QueryTracksInternal(query, applyPaging: false).Count;
        }

        private List<TrackDoc> QueryTracksInternal(TrackQuery query, bool applyPaging)
        {
            var col = _db.GetCollection<TrackDoc>("tracks");
            IEnumerable<TrackDoc> filtered;
            Dictionary<string, int> playlistPositions = null;

            if (!string.IsNullOrEmpty(query.ModuleId))
                filtered = col.Find(t => t.ModuleId == query.ModuleId);
            else if (!string.IsNullOrEmpty(query.AlbumId))
                filtered = col.Find(t => t.AlbumId == query.AlbumId);
            else
                filtered = col.FindAll();

            if (!string.IsNullOrEmpty(query.ModuleId))
                filtered = filtered.Where(t => t.ModuleId == query.ModuleId);
            if (!string.IsNullOrEmpty(query.AlbumId))
                filtered = filtered.Where(t => t.AlbumId == query.AlbumId);
            if (!string.IsNullOrEmpty(query.Text))
            {
                var text = query.Text.ToLowerInvariant();
                filtered = filtered.Where(t =>
                    (t.Title != null && t.Title.ToLowerInvariant().Contains(text)) ||
                    (t.Artist != null && t.Artist.ToLowerInvariant().Contains(text)));
            }
            if (query.HasIsFavorite)
                filtered = filtered.Where(t => t.IsFavorite == query.IsFavorite);
            if (query.HasIsExcluded)
                filtered = filtered.Where(t => t.IsExcluded == query.IsExcluded);

            // Tag filter — join through track_tags
            if (query.TagIds != null && query.TagIds.Count > 0)
            {
                var ttCol = _db.GetCollection<TrackTagDoc>("track_tags");
                var requiredTags = query.TagIds.Where(t => !string.IsNullOrWhiteSpace(t)).Distinct().ToList();
                var tagUuids = ttCol.Find(tt => requiredTags.Contains(tt.TagId))
                    .GroupBy(tt => tt.TrackUuid)
                    .Where(g => g.Select(tt => tt.TagId).Distinct().Count() == requiredTags.Count)
                    .Select(g => g.Key)
                    .ToHashSet();
                filtered = filtered.Where(t => tagUuids.Contains(t.Uuid));
            }

            // Playlist filter — join through playlist_entries
            if (!string.IsNullOrEmpty(query.PlaylistId))
            {
                var peCol = _db.GetCollection<PlaylistEntryDoc>("playlist_entries");
                playlistPositions = peCol.Find(pe => pe.PlaylistId == query.PlaylistId)
                    .GroupBy(pe => pe.TrackUuid, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(
                        group => group.Key,
                        group => group.Min(entry => entry.Position),
                        StringComparer.OrdinalIgnoreCase);
                filtered = filtered.Where(t => playlistPositions.ContainsKey(t.Uuid));
            }

            // Sort
            if (query.Sort != null)
            {
                var field = query.Sort.Field;
                var desc = query.Sort.Direction == SortDirection.Desc;
                filtered = field switch
                {
                    TrackSortField.Title => desc ? filtered.OrderByDescending(t => t.Title) : filtered.OrderBy(t => t.Title),
                    TrackSortField.Artist => desc ? filtered.OrderByDescending(t => t.Artist) : filtered.OrderBy(t => t.Artist),
                    TrackSortField.Duration => desc ? filtered.OrderByDescending(t => t.Duration) : filtered.OrderBy(t => t.Duration),
                    TrackSortField.PlayCount => desc ? filtered.OrderByDescending(t => t.PlayCount) : filtered.OrderBy(t => t.PlayCount),
                    TrackSortField.CreatedAt => desc ? filtered.OrderByDescending(t => t.CreatedAt) : filtered.OrderBy(t => t.CreatedAt),
                    _ => filtered
                };
            }
            else if (playlistPositions != null)
            {
                // A playlist query without an explicit user sort must preserve
                // the source playlist order stored in playlist_entries.Position.
                filtered = filtered
                    .OrderBy(t => playlistPositions.TryGetValue(t.Uuid, out var position) ? position : int.MaxValue)
                    .ThenBy(t => t.Uuid, StringComparer.OrdinalIgnoreCase);
            }

            if (applyPaging)
            {
                if (query.Offset > 0)
                    filtered = filtered.Skip(query.Offset);
                if (query.Limit > 0)
                    filtered = filtered.Take(query.Limit);
            }

            return filtered.ToList();
        }

        public bool DeleteTrack(string uuid)
        {
            var col = _db.GetCollection<TrackDoc>("tracks");
            // Also clean up track_tags and playlist_entries
            var ttCol = _db.GetCollection<TrackTagDoc>("track_tags");
            ttCol.DeleteMany(tt => tt.TrackUuid == uuid);
            var peCol = _db.GetCollection<PlaylistEntryDoc>("playlist_entries");
            peCol.DeleteMany(pe => pe.TrackUuid == uuid);
            return col.Delete(uuid);
        }

        // ── Track Tags ──

        public void SetTrackTags(string trackUuid, IEnumerable<string> tagIds)
        {
            var col = _db.GetCollection<TrackTagDoc>("track_tags");
            // Remove existing
            col.DeleteMany(tt => tt.TrackUuid == trackUuid);
            // Insert new
            foreach (var tagId in tagIds.Where(t => !string.IsNullOrWhiteSpace(t)).Distinct())
            {
                col.Upsert(new TrackTagDoc
                {
                    Id = $"{trackUuid}_{tagId}",
                    TrackUuid = trackUuid,
                    TagId = tagId
                });
            }
        }

        public bool AddTrackTag(string trackUuid, string tagId)
        {
            var col = _db.GetCollection<TrackTagDoc>("track_tags");
            var id = $"{trackUuid}_{tagId}";
            if (col.FindById(id) != null) return false;
            col.Insert(new TrackTagDoc { Id = id, TrackUuid = trackUuid, TagId = tagId });
            return true;
        }

        public bool RemoveTrackTag(string trackUuid, string tagId)
        {
            var col = _db.GetCollection<TrackTagDoc>("track_tags");
            return col.Delete($"{trackUuid}_{tagId}");
        }

        public List<string> GetTrackTags(string trackUuid)
        {
            var col = _db.GetCollection<TrackTagDoc>("track_tags");
            return col.Find(tt => tt.TrackUuid == trackUuid).Select(tt => tt.TagId).ToList();
        }

        // ── Album ──

        public bool UpsertAlbum(Album album)
        {
            var col = _db.GetCollection<AlbumDoc>("albums");
            var existed = col.FindById(album.Id) != null;
            col.Upsert(ToAlbumDoc(album));
            return !existed;
        }

        public int UpsertAlbums(IEnumerable<Album> albums)
        {
            var col = _db.GetCollection<AlbumDoc>("albums");
            int created = 0;
            foreach (var a in albums)
            {
                var existed = col.FindById(a.Id) != null;
                col.Upsert(ToAlbumDoc(a));
                if (!existed) created++;
            }
            return created;
        }

        public AlbumDoc GetAlbum(string id)
        {
            return _db.GetCollection<AlbumDoc>("albums").FindById(id);
        }

        public List<AlbumDoc> QueryAlbums(AlbumQuery query)
        {
            return QueryAlbumsInternal(query, applyPaging: true);
        }

        public int CountAlbums(AlbumQuery query)
        {
            return QueryAlbumsInternal(query, applyPaging: false).Count;
        }

        private List<AlbumDoc> QueryAlbumsInternal(AlbumQuery query, bool applyPaging)
        {
            var col = _db.GetCollection<AlbumDoc>("albums");
            var filtered = !string.IsNullOrEmpty(query.ModuleId)
                ? col.Find(a => a.ModuleId == query.ModuleId).AsEnumerable()
                : col.FindAll().AsEnumerable();

            if (!string.IsNullOrEmpty(query.ModuleId))
                filtered = filtered.Where(a => a.ModuleId == query.ModuleId);
            if (!string.IsNullOrEmpty(query.Text))
            {
                var text = query.Text.ToLowerInvariant();
                filtered = filtered.Where(a =>
                    (a.Title != null && a.Title.ToLowerInvariant().Contains(text)) ||
                    (a.Artist != null && a.Artist.ToLowerInvariant().Contains(text)));
            }

            // Tag filter requires track_tags + tracks join
            if (!string.IsNullOrEmpty(query.TagId))
            {
                var ttCol = _db.GetCollection<TrackTagDoc>("track_tags");
                var trackCol = _db.GetCollection<TrackDoc>("tracks");
                var albumIds = new HashSet<string>();
                foreach (var tt in ttCol.Find(t => t.TagId == query.TagId))
                {
                    var track = trackCol.FindById(tt.TrackUuid);
                    if (track != null && !string.IsNullOrEmpty(track.AlbumId))
                        albumIds.Add(track.AlbumId);
                }
                filtered = filtered.Where(a => albumIds.Contains(a.Id));
            }

            if (applyPaging)
            {
                if (query.Offset > 0) filtered = filtered.Skip(query.Offset);
                if (query.Limit > 0) filtered = filtered.Take(query.Limit);
            }

            return filtered.ToList();
        }

        public bool DeleteAlbum(string id)
        {
            var tracks = _db.GetCollection<TrackDoc>("tracks");
            foreach (var track in tracks.Find(t => t.AlbumId == id).ToList())
            {
                track.AlbumId = "";
                tracks.Update(track);
            }
            return _db.GetCollection<AlbumDoc>("albums").Delete(id);
        }

        // ── Tag ──

        public bool UpsertTag(Tag tag)
        {
            var col = _db.GetCollection<TagDoc>("tags");
            var existed = col.FindById(tag.Id) != null;
            col.Upsert(ToTagDoc(tag));
            return !existed;
        }

        public int UpsertTags(IEnumerable<Tag> tags)
        {
            var col = _db.GetCollection<TagDoc>("tags");
            int created = 0;
            foreach (var t in tags)
            {
                var existed = col.FindById(t.Id) != null;
                col.Upsert(ToTagDoc(t));
                if (!existed) created++;
            }
            return created;
        }

        public TagDoc GetTag(string id)
        {
            return _db.GetCollection<TagDoc>("tags").FindById(id);
        }

        public List<TagDoc> QueryTags(TagQuery query)
        {
            return QueryTagsInternal(query, applyPaging: true);
        }

        public int CountTags(TagQuery query)
        {
            return QueryTagsInternal(query, applyPaging: false).Count;
        }

        private List<TagDoc> QueryTagsInternal(TagQuery query, bool applyPaging)
        {
            var col = _db.GetCollection<TagDoc>("tags");
            var filtered = !string.IsNullOrEmpty(query.ModuleId)
                ? col.Find(t => t.ModuleId == query.ModuleId).AsEnumerable()
                : col.FindAll().AsEnumerable();

            if (!string.IsNullOrEmpty(query.ModuleId))
                filtered = filtered.Where(t => t.ModuleId == query.ModuleId);
            if (query.Kinds != null && query.Kinds.Count > 0)
            {
                var kinds = new HashSet<int>(query.Kinds.Select(k => (int)k));
                filtered = filtered.Where(t => kinds.Contains(t.Kind));
            }

            if (applyPaging)
            {
                if (query.Offset > 0) filtered = filtered.Skip(query.Offset);
                if (query.Limit > 0) filtered = filtered.Take(query.Limit);
            }

            return filtered.ToList();
        }

        public bool DeleteTag(string id)
        {
            var col = _db.GetCollection<TagDoc>("tags");
            // Clean up track_tags
            var ttCol = _db.GetCollection<TrackTagDoc>("track_tags");
            ttCol.DeleteMany(tt => tt.TagId == id);
            return col.Delete(id);
        }

        // ── Playlist ──

        public bool UpsertPlaylist(Playlist playlist)
        {
            var col = _db.GetCollection<PlaylistDoc>("playlists");
            var existed = col.FindById(playlist.Id) != null;
            col.Upsert(ToPlaylistDoc(playlist));
            return !existed;
        }

        public PlaylistDoc GetPlaylist(string id)
        {
            return _db.GetCollection<PlaylistDoc>("playlists").FindById(id);
        }

        public List<PlaylistDoc> QueryPlaylists(PlaylistQuery query)
        {
            return QueryPlaylistsInternal(query, applyPaging: true);
        }

        public int CountPlaylists(PlaylistQuery query)
        {
            return QueryPlaylistsInternal(query, applyPaging: false).Count;
        }

        private List<PlaylistDoc> QueryPlaylistsInternal(PlaylistQuery query, bool applyPaging)
        {
            var col = _db.GetCollection<PlaylistDoc>("playlists");
            var filtered = !string.IsNullOrEmpty(query.ModuleId)
                ? col.Find(p => p.ModuleId == query.ModuleId).AsEnumerable()
                : col.FindAll().AsEnumerable();

            if (!string.IsNullOrEmpty(query.ModuleId))
                filtered = filtered.Where(p => p.ModuleId == query.ModuleId);
            if (query.Kinds != null && query.Kinds.Count > 0)
            {
                var kinds = new HashSet<int>(query.Kinds.Select(k => (int)k));
                filtered = filtered.Where(p => kinds.Contains(p.Kind));
            }

            if (applyPaging)
            {
                if (query.Offset > 0) filtered = filtered.Skip(query.Offset);
                if (query.Limit > 0) filtered = filtered.Take(query.Limit);
            }

            return filtered.ToList();
        }

        public bool DeletePlaylist(string id)
        {
            var peCol = _db.GetCollection<PlaylistEntryDoc>("playlist_entries");
            peCol.DeleteMany(pe => pe.PlaylistId == id);
            return _db.GetCollection<PlaylistDoc>("playlists").Delete(id);
        }

        // ── Playlist Entries ──

        public void ReplacePlaylistEntries(string playlistId, IEnumerable<PlaylistEntrySpec> entries)
        {
            var col = _db.GetCollection<PlaylistEntryDoc>("playlist_entries");
            col.DeleteMany(pe => pe.PlaylistId == playlistId);

            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            int pos = 0;
            foreach (var entry in entries)
            {
                col.Insert(new PlaylistEntryDoc
                {
                    Id = $"{playlistId}_{Guid.NewGuid():N}",
                    PlaylistId = playlistId,
                    TrackUuid = entry.TrackUuid,
                    Position = entry.Position >= 0 ? entry.Position : pos,
                    AddedAt = now
                });
                pos++;
            }
        }

        public PlaylistEntryDoc InsertPlaylistEntry(string playlistId, PlaylistEntrySpec entry, int index)
        {
            var col = _db.GetCollection<PlaylistEntryDoc>("playlist_entries");
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var doc = new PlaylistEntryDoc
            {
                Id = $"{playlistId}_{Guid.NewGuid():N}",
                PlaylistId = playlistId,
                TrackUuid = entry.TrackUuid,
                Position = index >= 0 ? index : 0,
                AddedAt = now
            };

            // Shift positions if inserting at specific index
            if (index >= 0)
            {
                var existing = col.Find(pe => pe.PlaylistId == playlistId && pe.Position >= index).ToList();
                foreach (var e in existing)
                {
                    e.Position++;
                    col.Update(e);
                }
            }
            else
            {
                // Append
                var max = col.Find(pe => pe.PlaylistId == playlistId)
                    .Select(pe => (int?)pe.Position).Max() ?? -1;
                doc.Position = max + 1;
            }

            col.Insert(doc);
            return doc;
        }

        public bool RemovePlaylistEntry(string entryId)
        {
            return _db.GetCollection<PlaylistEntryDoc>("playlist_entries").Delete(entryId);
        }

        public bool MovePlaylistEntry(string entryId, int newIndex)
        {
            var col = _db.GetCollection<PlaylistEntryDoc>("playlist_entries");
            var entry = col.FindById(entryId);
            if (entry == null) return false;

            // Shift others
            var all = col.Find(pe => pe.PlaylistId == entry.PlaylistId).ToList();
            all.RemoveAll(e => e.Id == entryId);
            newIndex = Math.Clamp(newIndex, 0, all.Count);
            all.Insert(newIndex, entry);
            for (int i = 0; i < all.Count; i++)
            {
                all[i].Position = i;
                col.Update(all[i]);
            }
            return true;
        }

        public List<PlaylistEntryDoc> GetPlaylistEntries(string playlistId)
        {
            return _db.GetCollection<PlaylistEntryDoc>("playlist_entries")
                .Find(pe => pe.PlaylistId == playlistId)
                .OrderBy(pe => pe.Position)
                .ToList();
        }

        // ── Module cleanup ──

        public (int tracks, int albums, int tags, int playlists) UnregisterModule(string moduleId)
        {
            var trackCol = _db.GetCollection<TrackDoc>("tracks");
            var albumCol = _db.GetCollection<AlbumDoc>("albums");
            var tagCol = _db.GetCollection<TagDoc>("tags");
            var playlistCol = _db.GetCollection<PlaylistDoc>("playlists");
            var ttCol = _db.GetCollection<TrackTagDoc>("track_tags");
            var peCol = _db.GetCollection<PlaylistEntryDoc>("playlist_entries");

            var moduleTrackUuids = trackCol.Find(t => t.ModuleId == moduleId).Select(t => t.Uuid).ToHashSet();
            var moduleTagIds = tagCol.Find(t => t.ModuleId == moduleId).Select(t => t.Id).ToHashSet();
            var playlists = playlistCol.Find(p => p.ModuleId == moduleId).ToList();
            var modulePlaylistIds = playlists.Select(p => p.Id).ToHashSet();

            ttCol.DeleteMany(tt => moduleTrackUuids.Contains(tt.TrackUuid) || moduleTagIds.Contains(tt.TagId));
            peCol.DeleteMany(pe => moduleTrackUuids.Contains(pe.TrackUuid) || modulePlaylistIds.Contains(pe.PlaylistId));

            int tracks = trackCol.DeleteMany(t => t.ModuleId == moduleId);
            int albums = albumCol.DeleteMany(a => a.ModuleId == moduleId);
            int tagsRemoved = tagCol.DeleteMany(t => t.ModuleId == moduleId);

            // Clean up playlist_entries for playlists of this module
            int playlistsRemoved = 0;
            foreach (var pl in playlists)
            {
                if (playlistCol.Delete(pl.Id)) playlistsRemoved++;
            }

            return (tracks, albums, tagsRemoved, playlistsRemoved);
        }

        // ── Conversion helpers ──

        private static TrackDoc ToTrackDoc(Track t) => new TrackDoc
        {
            Uuid = t.Uuid,
            Title = t.Title,
            Artist = t.Artist,
            AlbumId = t.AlbumId,
            Duration = t.Duration,
            ModuleId = t.ModuleId,
            SourceType = (int)t.SourceType,
            SourcePath = t.SourcePath,
            IsFavorite = t.IsFavorite,
            IsExcluded = t.IsExcluded,
            CoverUri = t.CoverUri,
            PlayCount = t.PlayCount,
            CreatedAt = t.CreatedAt?.Seconds ?? 0,
            LastPlayedAt = t.LastPlayedAt?.Seconds,
            ExtendedData = t.ExtendedData?.ToByteArray()
        };

        public static Track ToTrackProto(TrackDoc doc) => new Track
        {
            Uuid = doc.Uuid ?? "",
            Title = doc.Title ?? "",
            Artist = doc.Artist ?? "",
            AlbumId = doc.AlbumId ?? "",
            Duration = doc.Duration,
            ModuleId = doc.ModuleId ?? "",
            SourceType = (SourceType)doc.SourceType,
            SourcePath = doc.SourcePath ?? "",
            IsFavorite = doc.IsFavorite,
            IsExcluded = doc.IsExcluded,
            CoverUri = doc.CoverUri ?? "",
            PlayCount = doc.PlayCount
        };

        private static AlbumDoc ToAlbumDoc(Album a) => new AlbumDoc
        {
            Id = a.Id,
            Title = a.Title,
            Artist = a.Artist,
            CoverUri = a.CoverUri,
            Year = a.Year,
            ModuleId = a.ModuleId
        };

        public static Album ToAlbumProto(AlbumDoc doc) => new Album
        {
            Id = doc.Id ?? "",
            Title = doc.Title ?? "",
            Artist = doc.Artist ?? "",
            CoverUri = doc.CoverUri ?? "",
            Year = doc.Year,
            ModuleId = doc.ModuleId ?? ""
        };

        private static TagDoc ToTagDoc(Tag t) => new TagDoc
        {
            Id = t.Id,
            Name = t.Name,
            Color = t.Color,
            ModuleId = t.ModuleId,
            Kind = (int)t.Kind
        };

        public static Tag ToTagProto(TagDoc doc) => new Tag
        {
            Id = doc.Id ?? "",
            Name = doc.Name ?? "",
            Color = doc.Color ?? "",
            ModuleId = doc.ModuleId ?? "",
            Kind = (TagKind)doc.Kind
        };

        private static PlaylistDoc ToPlaylistDoc(Playlist p) => new PlaylistDoc
        {
            Id = p.Id,
            Name = p.Name,
            ModuleId = p.ModuleId,
            Kind = (int)p.Kind,
            CoverUri = p.CoverUri,
            SortOrder = p.SortOrder,
            CreatedAt = p.CreatedAt?.Seconds ?? 0,
            UpdatedAt = p.UpdatedAt?.Seconds ?? 0
        };

        public static Playlist ToPlaylistProto(PlaylistDoc doc) => new Playlist
        {
            Id = doc.Id ?? "",
            Name = doc.Name ?? "",
            ModuleId = doc.ModuleId ?? "",
            Kind = (PlaylistKind)doc.Kind,
            CoverUri = doc.CoverUri ?? "",
            SortOrder = doc.SortOrder
        };

        public void Dispose()
        {
            try { _db?.Dispose(); }
            catch (Exception ex) { _logger?.LogError(ex, "Error disposing LibraryStorage"); }
        }
    }
}
