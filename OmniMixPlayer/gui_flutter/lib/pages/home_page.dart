import 'dart:async';

import 'package:flutter/material.dart';
import 'package:omnimix_gui/l10n/app_localizations.dart';

import '../generated/omni_mix_player/models/tag.pb.dart';
import '../generated/omni_mix_player/models/album.pb.dart';
import '../generated/omni_mix_player/models/playlist.pb.dart';
import '../generated/omni_mix_player/models/track.pb.dart';
import '../generated/omni_mix_player/services/playback.pb.dart';
import '../providers/app_state.dart';

class HomePage extends StatefulWidget {
  final AppState state;

  const HomePage({super.key, required this.state});

  @override
  State<HomePage> createState() => _HomePageState();
}

enum _LibraryView { playlist, album, song }

enum _QueueTab { queue, history }

class _HomePageState extends State<HomePage> {
  List<Tag> _tags = [];
  List<Album> _albums = [];
  List<Playlist> _playlists = [];
  List<Track> _songs = [];
  String _query = '';
  bool _loading = false;
  String _error = '';
  _LibraryView _libraryView = _LibraryView.song;
  _QueueTab _queueTab = _QueueTab.queue;
  int _lastLibGen = 0; // track library generation for event-driven refresh
  double? _draggingPosition;
  double? _draggingVolume;
  Timer? _volumeThrottleTimer;
  DateTime? _lastVolumeSendTime;
  double? _pendingVolumeValue;
  double? _draggingLatency;
  Timer? _latencyThrottleTimer;
  DateTime? _lastLatencySendTime;
  double? _pendingLatencyValue;

  @override
  void initState() {
    super.initState();
    widget.state.addListener(_onStateChanged);
    if (widget.state.backendOnline) {
      _loadLibrary();
    }
  }

  @override
  void dispose() {
    widget.state.removeListener(_onStateChanged);
    _volumeThrottleTimer?.cancel();
    _latencyThrottleTimer?.cancel();
    super.dispose();
  }

  void _onStateChanged() {
    if (!mounted) return;
    // Event-driven library reload: when modules load/unload or playlist updates
    if (widget.state.backendOnline &&
        widget.state.libraryGeneration != _lastLibGen) {
      _lastLibGen = widget.state.libraryGeneration;
      _loadLibrary();
    }
    if (widget.state.backendOnline && _songs.isEmpty && !_loading) {
      _loadLibrary();
    }
    setState(() {});
  }

  void _updateVolumeThrottled(double val) {
    setState(() {
      _draggingVolume = val;
    });

    _pendingVolumeValue = val;

    final now = DateTime.now();
    final elapsed = _lastVolumeSendTime == null
        ? 1000
        : now.difference(_lastVolumeSendTime!).inMilliseconds;

    if (elapsed >= 150) {
      _executeSendVolume();
    } else {
      _volumeThrottleTimer ??= Timer(Duration(milliseconds: 150 - elapsed), () {
        _volumeThrottleTimer = null;
        _executeSendVolume();
      });
    }
  }

  void _executeSendVolume() {
    if (_pendingVolumeValue == null) return;
    final val = _pendingVolumeValue!;
    _pendingVolumeValue = null;
    _lastVolumeSendTime = DateTime.now();
    widget.state.setVolumeActive(val);
  }

  void _sendVolume(double val) {
    _pendingVolumeValue = null;
    _volumeThrottleTimer?.cancel();
    _volumeThrottleTimer = null;
    _lastVolumeSendTime = DateTime.now();
    widget.state.setVolumeActive(val);
  }

  void _updateLatencyThrottled(double val) {
    setState(() {
      _draggingLatency = val;
    });

    _pendingLatencyValue = val;

    final now = DateTime.now();
    final elapsed = _lastLatencySendTime == null
        ? 1000
        : now.difference(_lastLatencySendTime!).inMilliseconds;

    if (elapsed >= 150) {
      _executeSendLatency();
    } else {
      _latencyThrottleTimer ??= Timer(
        Duration(milliseconds: 150 - elapsed),
        () {
          _latencyThrottleTimer = null;
          _executeSendLatency();
        },
      );
    }
  }

  void _executeSendLatency() {
    if (_pendingLatencyValue == null) return;
    final val = _pendingLatencyValue!;
    _pendingLatencyValue = null;
    _lastLatencySendTime = DateTime.now();
    widget.state.setTargetLatencyActive(val);
  }

  void _sendLatency(double val) {
    _pendingLatencyValue = null;
    _latencyThrottleTimer?.cancel();
    _latencyThrottleTimer = null;
    _lastLatencySendTime = DateTime.now();
    widget.state.setTargetLatencyActive(val);
  }

  Future<void> _loadLibrary() async {
    if (!widget.state.backendOnline) return;
    setState(() {
      _loading = true;
      _error = '';
    });
    try {
      final results = await Future.wait([
        widget.state.api.getTags(),
        widget.state.api.getAlbums(),
        widget.state.api.getSongs(),
        widget.state.api.getPlaylists(),
      ]);
      if (!mounted) return;
      setState(() {
        _tags = results[0] as List<Tag>;
        _albums = results[1] as List<Album>;
        _songs = results[2] as List<Track>;
        _playlists = results[3] as List<Playlist>;
      });
    } catch (e) {
      if (!mounted) return;
      final l10n = AppLocalizations.of(context);
      setState(() => _error = l10n.failedToLoadLibrary('$e'));
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final busy = widget.state.backendBusy || widget.state.serviceBusy;
    if (busy) {
      return _LoadingHome(
        icon: Icons.settings_input_component_rounded,
        title: l10n.serviceStarting,
        message: l10n.serviceStartingMessage,
      );
    }

    if (!widget.state.backendOnline) {
      return _LoadingHome(
        icon: Icons.cloud_off_rounded,
        title: l10n.serviceNotConnected,
        message: l10n.waitingForBackend,
      );
    }

    return RefreshIndicator(
      onRefresh: () async {
        await widget.state.refreshPlayback();
        await _loadLibrary();
      },
      child: LayoutBuilder(
        builder: (_, c) {
          final canControl = widget.state.canControlActiveInstance;
          final compact = c.maxWidth < 1080;
          return ListView(
            padding: const EdgeInsets.all(16),
            children: [
              compact
                  ? Column(
                      children: [
                        _buildNowPlaying(minimalClientMode: !canControl),
                        const SizedBox(height: 12),
                        SizedBox(height: 430, child: _buildQueuePanel()),
                        const SizedBox(height: 12),
                        SizedBox(height: 520, child: _buildLibraryPanel()),
                      ],
                    )
                  : Row(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Expanded(
                          flex: 10,
                          child: _buildNowPlaying(
                            minimalClientMode: !canControl,
                          ),
                        ),
                        const SizedBox(width: 12),
                        Expanded(
                          flex: 11,
                          child: SizedBox(
                            height: 760,
                            child: _buildQueuePanel(),
                          ),
                        ),
                        const SizedBox(width: 12),
                        Expanded(
                          flex: 15,
                          child: SizedBox(
                            height: 760,
                            child: _buildLibraryPanel(),
                          ),
                        ),
                      ],
                    ),
            ],
          );
        },
      ),
    );
  }

  Widget _buildNowPlaying({bool minimalClientMode = false}) {
    final l10n = AppLocalizations.of(context);
    final cs = Theme.of(context).colorScheme;
    final instance = widget.state.activeInstance;
    final trackUuid = instance?.currentTrackUuid ?? '';
    final canControl = widget.state.canControlActiveInstance;
    final canPlayPause = widget.state.canPlayPauseActiveInstance;

    final title = (widget.state.currentTrackTitle.isNotEmpty)
        ? widget.state.currentTrackTitle
        : (trackUuid.isNotEmpty ? trackUuid : l10n.noSongPlaying);
    final artist = widget.state.currentTrackArtist;
    final duration = widget.state.currentTrackDuration;
    final isPlaying = widget.state.isPlaying;
    final position = _draggingPosition ?? widget.state.currentTrackPosition;
    final canSeek = widget.state.canSeekActiveInstance;
    final canSetVolume = widget.state.canSetVolumeActiveInstance;
    final canSetLatency = widget.state.canSetLatencyActiveInstance;

    return _Panel(
      child: Column(
        children: [
          const SizedBox(height: 8),
          SizedBox(
            width: 230,
            height: 230,
            child: ClipRRect(
              borderRadius: BorderRadius.circular(12),
              child: _Cover(uuid: trackUuid, baseUrl: widget.state.apiBaseUrl),
            ),
          ),
          const SizedBox(height: 12),
          Text(
            title,
            maxLines: 2,
            overflow: TextOverflow.ellipsis,
            textAlign: TextAlign.center,
            style: const TextStyle(fontSize: 20, fontWeight: FontWeight.w700),
          ),
          const SizedBox(height: 6),
          Text(
            artist,
            maxLines: 1,
            overflow: TextOverflow.ellipsis,
            textAlign: TextAlign.center,
            style: TextStyle(color: cs.onSurfaceVariant),
          ),
          if (!minimalClientMode) ...[
            const SizedBox(height: 10),
            Wrap(
              alignment: WrapAlignment.center,
              spacing: 6,
              runSpacing: 6,
              children: [
                IconButton(
                  onPressed: canControl
                      ? () => widget.state.setShuffle(
                          !widget.state.shuffleEnabled,
                        )
                      : null,
                  icon: Icon(
                    Icons.shuffle_rounded,
                    color: widget.state.shuffleEnabled ? cs.primary : null,
                  ),
                  tooltip: l10n.shuffle,
                ),
                IconButton(
                  onPressed: canControl ? widget.state.previousTrack : null,
                  icon: const Icon(Icons.skip_previous_rounded),
                  tooltip: l10n.previous,
                ),
                IconButton(
                  onPressed: canPlayPause ? widget.state.togglePlayback : null,
                  icon: Icon(
                    isPlaying ? Icons.pause_rounded : Icons.play_arrow_rounded,
                  ),
                  tooltip: l10n.playPause,
                ),
                IconButton(
                  onPressed: canControl ? widget.state.nextTrack : null,
                  icon: const Icon(Icons.skip_next_rounded),
                  tooltip: l10n.next,
                ),
                IconButton(
                  onPressed: canControl
                      ? () {
                          final current = widget.state.repeatModeStr;
                          final next = current == 'REPEAT_MODE_ONE'
                              ? 'all'
                              : 'one';
                          widget.state.setRepeatMode(next);
                        }
                      : null,
                  icon: Icon(
                    Icons.repeat_one_rounded,
                    color: widget.state.repeatModeStr == 'REPEAT_MODE_ONE'
                        ? cs.primary
                        : null,
                  ),
                  tooltip: l10n.repeatOne,
                ),
              ],
            ),
          ],
          const SizedBox(height: 8),
          Row(
            children: [
              SizedBox(
                width: 42,
                child: Text(
                  _fmt(position),
                  style: TextStyle(fontSize: 12, color: cs.onSurfaceVariant),
                ),
              ),
              Expanded(
                child: Slider(
                  value: position,
                  min: 0,
                  max: duration > 0 ? duration : 1,
                  onChanged: canSeek && duration > 0
                      ? (val) {
                          setState(() {
                            _draggingPosition = val;
                          });
                        }
                      : null,
                  onChangeEnd: canSeek && duration > 0
                      ? (val) async {
                          await widget.state.seekActive(val);
                          if (mounted) {
                            setState(() {
                              _draggingPosition = null;
                            });
                          }
                        }
                      : null,
                ),
              ),
              SizedBox(
                width: 42,
                child: Text(
                  _fmt(duration),
                  style: TextStyle(fontSize: 12, color: cs.onSurfaceVariant),
                ),
              ),
            ],
          ),
          const SizedBox(height: 8),
          Row(
            children: [
              const Icon(Icons.volume_up, size: 20),
              const SizedBox(width: 8),
              Expanded(
                child: Slider(
                  value: _draggingVolume ?? widget.state.lastVolume,
                  min: 0.0,
                  max: 1.0,
                  onChanged: canSetVolume
                      ? (val) {
                          _updateVolumeThrottled(val);
                        }
                      : null,
                  onChangeEnd: canSetVolume
                      ? (val) {
                          _volumeThrottleTimer?.cancel();
                          _sendVolume(val);
                          setState(() {
                            _draggingVolume = null;
                          });
                        }
                      : null,
                ),
              ),
              SizedBox(
                width: 42,
                child: Text(
                  "${((_draggingVolume ?? widget.state.lastVolume) * 100).round()}%",
                  style: TextStyle(fontSize: 12, color: cs.onSurfaceVariant),
                ),
              ),
            ],
          ),
          const SizedBox(height: 8),
          Row(
            children: [
              Tooltip(
                message: l10n.audioBufferLatencyTip,
                child: const Icon(Icons.av_timer_rounded, size: 20),
              ),
              const SizedBox(width: 8),
              Expanded(
                child: Slider(
                  value: _draggingLatency ?? widget.state.lastTargetLatency,
                  min: 0.03,
                  max: 1.0,
                  onChanged: canSetLatency
                      ? (val) {
                          _updateLatencyThrottled(val);
                        }
                      : null,
                  onChangeEnd: canSetLatency
                      ? (val) {
                          _latencyThrottleTimer?.cancel();
                          _sendLatency(val);
                          setState(() {
                            _draggingLatency = null;
                          });
                        }
                      : null,
                ),
              ),
              SizedBox(
                width: 42,
                child: Text(
                  "${(_draggingLatency ?? widget.state.lastTargetLatency).toStringAsFixed(2)}s",
                  style: TextStyle(fontSize: 12, color: cs.onSurfaceVariant),
                ),
              ),
            ],
          ),
          const SizedBox(height: 6),
          Text(
            canPlayPause
                ? l10n.serverControlMode
                : l10n.clientModeControlsDisabled,
            style: TextStyle(fontSize: 12, color: cs.onSurfaceVariant),
          ),
        ],
      ),
    );
  }

  Widget _buildQueuePanel() {
    final l10n = AppLocalizations.of(context);
    final canQueue = widget.state.canControlActiveInstance;
    return _Panel(
      child: Column(
        children: [
          Row(
            children: [
              SegmentedButton<_QueueTab>(
                segments: [
                  ButtonSegment(
                    value: _QueueTab.queue,
                    label: Text(l10n.queue),
                  ),
                  ButtonSegment(
                    value: _QueueTab.history,
                    label: Text(l10n.history),
                  ),
                ],
                selected: {_queueTab},
                onSelectionChanged: (s) => setState(() => _queueTab = s.first),
              ),
              const Spacer(),
              if (canQueue)
                TextButton(
                  onPressed: _queueTab == _QueueTab.queue
                      ? widget.state.clearActiveQueue
                      : widget.state.clearActiveHistory,
                  child: Text(l10n.clear),
                ),
            ],
          ),
          const SizedBox(height: 8),
          Expanded(
            child: _queueTab == _QueueTab.queue
                ? _buildReorderList(widget.state.activeQueue, isQueue: true)
                : _buildReorderList(widget.state.activeHistory, isQueue: false),
          ),
        ],
      ),
    );
  }

  Widget _buildReorderList(List<QueueTrack> items, {required bool isQueue}) {
    final l10n = AppLocalizations.of(context);
    final canQueue = widget.state.canControlActiveInstance;
    final canPlayback = widget.state.canPlayPauseActiveInstance;
    if (items.isEmpty) return Center(child: Text(l10n.empty));

    return ReorderableListView.builder(
      buildDefaultDragHandles: canQueue,
      itemCount: items.length,
      onReorder: canQueue
          ? (oldIndex, newIndex) async {
              var to = newIndex;
              if (newIndex > oldIndex) to -= 1;
              if (isQueue) {
                await widget.state.moveQueueItem(oldIndex, to);
              } else {
                await widget.state.moveHistoryItem(oldIndex, to);
              }
            }
          : (_, _) {},
      itemBuilder: (_, i) {
        final s = items[i];
        return _SongRow(
          key: ValueKey('${isQueue ? 'q' : 'h'}_${s.uuid}_$i'),
          song: _songWithFallback(s),
          canControl: canQueue || canPlayback,
          playOnlyButton: true,
          onPlay: canPlayback
              ? () => widget.state.playSongOnActive(s.uuid)
              : null,
          onAddTail: canQueue
              ? () => widget.state.addSongToActiveQueue(s.uuid)
              : null,
          onNext: canQueue
              ? () => widget.state.addSongNextOnActive(s.uuid)
              : null,
          onExcludeToggle: canPlayback
              ? () => widget.state.setSongExcluded(s.uuid, !_isExcluded(s.uuid))
              : null,
          excluded: _isExcluded(s.uuid),
          onDelete: canQueue
              ? () => isQueue
                    ? widget.state.removeQueueItem(i)
                    : widget.state.removeHistoryItem(i)
              : null,
          baseUrl: widget.state.apiBaseUrl,
        );
      },
    );
  }

  Widget _buildLibraryPanel() {
    final l10n = AppLocalizations.of(context);
    if (_loading) {
      return const _Panel(child: Center(child: CircularProgressIndicator()));
    }
    if (_error.isNotEmpty) {
      return _Panel(child: Center(child: Text(l10n.errorWithMessage(_error))));
    }

    final canManagePlaylist = widget.state.canManageActiveLibrary;
    final canAddSources = canManagePlaylist;
    final sourceLimit = widget.state.activePlaylistSourceLimit;
    final sourceCount = widget.state.playlistSources.length;
    final sourceLimitLabel = sourceLimit == null
        ? '$sourceCount'
        : '$sourceCount/$sourceLimit';
    return _Panel(
      child: Column(
        children: [
          Row(
            children: [
              SegmentedButton<_LibraryView>(
                segments: [
                  ButtonSegment(
                    value: _LibraryView.playlist,
                    label: Text(l10n.byPlaylist),
                  ),
                  ButtonSegment(
                    value: _LibraryView.album,
                    label: Text(l10n.byAlbum),
                  ),
                  ButtonSegment(
                    value: _LibraryView.song,
                    label: Text(l10n.bySong),
                  ),
                ],
                selected: {_libraryView},
                onSelectionChanged: (s) =>
                    setState(() => _libraryView = s.first),
              ),
              const Spacer(),
              if (canManagePlaylist) ...[
                Tooltip(
                  message: l10n.selectedCount(sourceCount),
                  child: Text(
                    sourceLimitLabel,
                    style: TextStyle(
                      color: widget.state.activePlaylistSourceLimitReached
                          ? Theme.of(context).colorScheme.error
                          : Theme.of(context).colorScheme.onSurfaceVariant,
                      fontWeight: FontWeight.w600,
                    ),
                  ),
                ),
                const SizedBox(width: 6),
              ],
              if (canAddSources)
                IconButton(
                  onPressed: _showAddSourceSheet,
                  tooltip: l10n.addSource,
                  icon: const Icon(Icons.add_rounded),
                ),
            ],
          ),
          const SizedBox(height: 8),
          TextField(
            onChanged: (v) => setState(() => _query = v.trim().toLowerCase()),
            decoration: InputDecoration(
              prefixIcon: const Icon(Icons.search_rounded),
              hintText: l10n.searchHint,
              isDense: true,
            ),
          ),
          const SizedBox(height: 8),
          Expanded(child: _buildLibraryList(canManagePlaylist)),
        ],
      ),
    );
  }

  Widget _buildLibraryList(bool canControl) {
    final l10n = AppLocalizations.of(context);
    final songs = _filteredSongs();
    final canPlayback = widget.state.canPlayPauseActiveInstance;
    final canQueue = widget.state.canControlActiveInstance;
    final selectedSourceIds = widget.state.playlistSources
        .map((s) => s.id)
        .toSet();
    if (_libraryView == _LibraryView.song) {
      if (songs.isEmpty) return Center(child: Text(l10n.noSongs));
      return ListView.builder(
        itemCount: songs.length,
        itemBuilder: (_, i) {
          final s = songs[i];
          final trackSourceId = 'track_${s.uuid}';
          return _SongRow(
            song: s,
            canControl: canControl || canPlayback || canQueue,
            playOnlyButton: true,
            onPlay: canPlayback
                ? () => widget.state.playSongOnActive(s.uuid)
                : null,
            onNext: canQueue
                ? () => widget.state.addSongNextOnActive(s.uuid)
                : null,
            onAddTail: canQueue
                ? () => widget.state.addSongToActiveQueue(s.uuid)
                : null,
            onExcludeToggle: canPlayback
                ? () => widget.state.setSongExcluded(s.uuid, !(s.isExcluded))
                : null,
            onAddToLibrary:
                canControl &&
                    !selectedSourceIds.contains(trackSourceId) &&
                    widget.state.canAddOrReplacePlaylistSource(trackSourceId)
                ? () => widget.state.addTrackToActivePlaylist(s)
                : null,
            excluded: s.isExcluded,
            baseUrl: widget.state.apiBaseUrl,
          );
        },
      );
    }

    if (widget.state.activePlaylist.isEmpty) {
      return Center(child: Text(l10n.noActivePlaylist));
    }

    if (_libraryView == _LibraryView.album) {
      // Collect album IDs from songs in the active playlist (via full SongInfo)
      final playlistUuids = widget.state.activePlaylist
          .map((s) => s.uuid)
          .toSet();
      final albumIdsFromSongs = _songs
          .where((s) => playlistUuids.contains(s.uuid))
          .map((s) => s.albumId)
          .where((id) => id.isNotEmpty)
          .toSet();
      // Also try active playlist items directly (offline fallback)
      albumIdsFromSongs.addAll(
        widget.state.activePlaylist
            .map((s) => s.albumId)
            .where((id) => id.isNotEmpty),
      );
      final selectedAlbumIds = {
        ...selectedSourceIds
            .where((id) => id.startsWith('album_'))
            .map((id) => id.substring('album_'.length)),
        ...albumIdsFromSongs,
      };
      final filteredAlbums = _albums.where((a) {
        if (!selectedAlbumIds.contains(a.id)) return false;
        if (_query.isEmpty) return true;
        return a.title.toLowerCase().contains(_query) ||
            a.moduleId.toLowerCase().contains(_query);
      }).toList();
      if (filteredAlbums.isEmpty) {
        return Center(child: Text(l10n.noAlbumsAdded));
      }
      return ListView.builder(
        itemCount: filteredAlbums.length,
        itemBuilder: (_, i) {
          final a = filteredAlbums[i];
          return ListTile(
            leading: SizedBox(
              width: 40,
              height: 40,
              child: _AlbumCover(album: a, baseUrl: widget.state.apiBaseUrl),
            ),
            title: Text(a.title, maxLines: 1, overflow: TextOverflow.ellipsis),
            subtitle: Text(
              l10n.songCountWithModule(_albumSongCount(a.id), a.moduleId),
            ),
            trailing: canControl && selectedSourceIds.contains('album_${a.id}')
                ? PopupMenuButton<String>(
                    onSelected: (v) {
                      if (v == 'remove') {
                        widget.state.removePlaylistSource('album_${a.id}');
                      }
                    },
                    itemBuilder: (_) => [
                      PopupMenuItem(
                        value: 'remove',
                        child: Text(l10n.removeFromLibrary),
                      ),
                    ],
                  )
                : null,
          );
        },
      );
    }

    final sourceRows = <ListTile>[];
    for (final p in _playlists) {
      final sourceId = 'playlist_${p.id}';
      if (!selectedSourceIds.contains(sourceId)) continue;
      if (_query.isNotEmpty &&
          !p.name.toLowerCase().contains(_query) &&
          !p.moduleId.toLowerCase().contains(_query)) {
        continue;
      }
      sourceRows.add(
        ListTile(
          leading: const Icon(Icons.queue_music_rounded),
          title: Text(p.name, maxLines: 1, overflow: TextOverflow.ellipsis),
          subtitle: Text(p.moduleId),
          trailing: canControl
              ? PopupMenuButton<String>(
                  onSelected: (v) {
                    if (v == 'remove') {
                      widget.state.removePlaylistSource(sourceId);
                    }
                  },
                  itemBuilder: (_) => [
                    PopupMenuItem(
                      value: 'remove',
                      child: Text(l10n.removeFromLibrary),
                    ),
                  ],
                )
              : null,
        ),
      );
    }
    for (final t in _tags) {
      final sourceId = 'tag_${t.id}';
      if (!selectedSourceIds.contains(sourceId)) continue;
      if (_query.isNotEmpty &&
          !t.name.toLowerCase().contains(_query) &&
          !t.moduleId.toLowerCase().contains(_query)) {
        continue;
      }
      sourceRows.add(
        ListTile(
          leading: const Icon(Icons.folder_rounded),
          title: Text(t.name, maxLines: 1, overflow: TextOverflow.ellipsis),
          subtitle: Text(t.moduleId),
          trailing: canControl
              ? PopupMenuButton<String>(
                  onSelected: (v) {
                    if (v == 'remove') {
                      widget.state.removePlaylistSource(sourceId);
                    }
                  },
                  itemBuilder: (_) => [
                    PopupMenuItem(
                      value: 'remove',
                      child: Text(l10n.removeFromLibrary),
                    ),
                  ],
                )
              : null,
        ),
      );
    }
    if (sourceRows.isEmpty) {
      return Center(child: Text(l10n.noPlaylistsAdded));
    }
    return ListView(children: sourceRows);
  }

  List<Track> _filteredSongs() {
    // activePlaylist is the canonical playback order. Filtering the global
    // library list by UUID keeps the library/database iteration order instead
    // and makes an ordered source playlist look shuffled after it is assigned
    // to an instance. Resolve metadata while iterating activePlaylist so both
    // the UI and the playback timeline use the same order.
    final songsByUuid = <String, Track>{
      for (final song in _songs) song.uuid: song,
    };
    var list = widget.state.activePlaylist
        .map((queued) => songsByUuid[queued.uuid] ?? _songWithFallback(queued))
        .toList();
    if (_query.isNotEmpty) {
      list = list.where((s) {
        final album = _albumName(s.albumId).toLowerCase();
        return s.title.toLowerCase().contains(_query) ||
            s.artist.toLowerCase().contains(_query) ||
            album.contains(_query);
      }).toList();
    }
    return list;
  }

  Track _songWithFallback(QueueTrack s) {
    final found = _songs.where((e) => e.uuid == s.uuid);
    if (found.isNotEmpty) return found.first;
    return Track(
      uuid: s.uuid,
      title: s.title,
      artist: s.artist,
      albumId: s.albumId,
      duration: s.duration,
      moduleId: s.moduleId,
    );
  }

  bool _isExcluded(String uuid) {
    final found = _songs.where((e) => e.uuid == uuid);
    return found.isNotEmpty && found.first.isExcluded;
  }

  String _albumName(String albumId) {
    final m = _albums.where((a) => a.id == albumId);
    return m.isEmpty ? '' : m.first.title;
  }

  int _albumSongCount(String albumId) =>
      _songs.where((s) => s.albumId == albumId).length;

  void _showAddSourceSheet() {
    final l10n = AppLocalizations.of(context);
    showModalBottomSheet<void>(
      context: context,
      isScrollControlled: true,
      builder: (_) => StatefulBuilder(
        builder: (ctx, setSheetState) {
          final selectedIds = widget.state.playlistSources
              .map((s) => s.id)
              .toSet();
          final canUseSources = widget.state.canManageActiveLibrary;
          final sourceLimit = widget.state.activePlaylistSourceLimit;
          final selectedLabel = sourceLimit == null
              ? l10n.selectedCount(selectedIds.length)
              : '${selectedIds.length}/$sourceLimit';
          return SizedBox(
            height: MediaQuery.of(context).size.height * 0.82,
            child: DefaultTabController(
              length: 3,
              child: Column(
                children: [
                  const SizedBox(height: 8),
                  Container(
                    width: 36,
                    height: 4,
                    decoration: BoxDecoration(
                      color: Colors.grey,
                      borderRadius: BorderRadius.circular(4),
                    ),
                  ),
                  Padding(
                    padding: const EdgeInsets.fromLTRB(12, 10, 12, 2),
                    child: Row(
                      children: [
                        Text(
                          l10n.selectLibrarySource,
                          style: const TextStyle(fontWeight: FontWeight.w600),
                        ),
                        const Spacer(),
                        Text(selectedLabel),
                      ],
                    ),
                  ),
                  TabBar(
                    tabs: [
                      Tab(text: l10n.playlistsTab),
                      Tab(text: l10n.albumsTab),
                      const Tab(text: 'Tags'),
                    ],
                  ),
                  Expanded(
                    child: TabBarView(
                      children: [
                        ListView.builder(
                          itemCount: _playlists.length,
                          itemBuilder: (_, i) {
                            final p = _playlists[i];
                            final sourceId = 'playlist_${p.id}';
                            final checked = selectedIds.contains(sourceId);
                            final canToggle =
                                canUseSources &&
                                (checked ||
                                    widget.state.canAddOrReplacePlaylistSource(
                                      sourceId,
                                    ));
                            return CheckboxListTile(
                              value: checked,
                              controlAffinity: ListTileControlAffinity.leading,
                              secondary: const Icon(Icons.queue_music_rounded),
                              title: Text(p.name),
                              subtitle: Text(p.moduleId),
                              onChanged: canToggle
                                  ? (v) async {
                                      if (v == null) return;
                                      if (v) {
                                        await widget.state
                                            .addPlaylistToActivePlaylist(p);
                                      } else {
                                        await widget.state.removePlaylistSource(
                                          sourceId,
                                        );
                                      }
                                      if (mounted) setState(() {});
                                      setSheetState(() {});
                                    }
                                  : null,
                            );
                          },
                        ),
                        ListView.builder(
                          itemCount: _albums.length,
                          itemBuilder: (_, i) {
                            final a = _albums[i];
                            final sourceId = 'album_${a.id}';
                            final playlistUuids = widget.state.activePlaylist
                                .map((s) => s.uuid)
                                .toSet();
                            final checked =
                                selectedIds.contains(sourceId) ||
                                _songs.any(
                                  (s) =>
                                      s.albumId == a.id &&
                                      playlistUuids.contains(s.uuid),
                                ) ||
                                widget.state.activePlaylist.any(
                                  (s) => s.albumId == a.id,
                                );
                            final canToggle =
                                canUseSources &&
                                (selectedIds.contains(sourceId) ||
                                    widget.state.canAddOrReplacePlaylistSource(
                                      sourceId,
                                    ));
                            return CheckboxListTile(
                              value: checked,
                              controlAffinity: ListTileControlAffinity.leading,
                              secondary: SizedBox(
                                width: 36,
                                height: 36,
                                child: _AlbumCover(
                                  album: a,
                                  baseUrl: widget.state.apiBaseUrl,
                                ),
                              ),
                              title: Text(a.title),
                              subtitle: Text(
                                l10n.songCountWithModule(
                                  _albumSongCount(a.id),
                                  a.moduleId,
                                ),
                              ),
                              onChanged: canToggle
                                  ? (v) async {
                                      if (v == null) return;
                                      if (v) {
                                        await widget.state
                                            .addAlbumToActivePlaylist(a);
                                      } else {
                                        await widget.state.removePlaylistSource(
                                          sourceId,
                                        );
                                      }
                                      if (mounted) setState(() {});
                                      setSheetState(() {});
                                    }
                                  : null,
                            );
                          },
                        ),
                        ListView.builder(
                          itemCount: _tags.length,
                          itemBuilder: (_, i) {
                            final t = _tags[i];
                            final sourceId = 'tag_${t.id}';
                            final checked = selectedIds.contains(sourceId);
                            final canToggle =
                                canUseSources &&
                                (checked ||
                                    widget.state.canAddOrReplacePlaylistSource(
                                      sourceId,
                                    ));
                            return CheckboxListTile(
                              value: checked,
                              controlAffinity: ListTileControlAffinity.leading,
                              secondary: const Icon(Icons.folder_rounded),
                              title: Text(t.name),
                              subtitle: Text(t.moduleId),
                              onChanged: canToggle
                                  ? (v) async {
                                      if (v == null) return;
                                      if (v) {
                                        await widget.state
                                            .addTagToActivePlaylist(t);
                                      } else {
                                        await widget.state.removePlaylistSource(
                                          sourceId,
                                        );
                                      }
                                      if (mounted) setState(() {});
                                      setSheetState(() {});
                                    }
                                  : null,
                            );
                          },
                        ),
                      ],
                    ),
                  ),
                ],
              ),
            ),
          );
        },
      ),
    );
  }

  String _fmt(double sec) {
    if (sec <= 0) return '0:00';
    final m = (sec ~/ 60).toString();
    final s = (sec % 60).toInt().toString().padLeft(2, '0');
    return '$m:$s';
  }
}

class _LoadingHome extends StatelessWidget {
  final IconData icon;
  final String title;
  final String message;

  const _LoadingHome({
    required this.icon,
    required this.title,
    required this.message,
  });

  @override
  Widget build(BuildContext context) {
    final cs = Theme.of(context).colorScheme;
    return Center(
      child: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          Icon(icon, size: 72, color: cs.primary.withAlpha(180)),
          const SizedBox(height: 16),
          Text(
            title,
            style: const TextStyle(fontSize: 22, fontWeight: FontWeight.w700),
          ),
          const SizedBox(height: 8),
          Text(message, style: TextStyle(color: cs.onSurfaceVariant)),
          const SizedBox(height: 20),
          const SizedBox(
            width: 28,
            height: 28,
            child: CircularProgressIndicator(strokeWidth: 2.5),
          ),
        ],
      ),
    );
  }
}

class _Panel extends StatelessWidget {
  final Widget child;

  const _Panel({required this.child});

  @override
  Widget build(BuildContext context) {
    final cs = Theme.of(context).colorScheme;
    return Container(
      decoration: BoxDecoration(
        color: cs.surfaceContainerLow,
        borderRadius: BorderRadius.circular(14),
        border: Border.all(color: cs.outlineVariant.withAlpha(120)),
      ),
      padding: const EdgeInsets.all(12),
      child: child,
    );
  }
}

class _Cover extends StatelessWidget {
  final String uuid;
  final String baseUrl;

  const _Cover({required this.uuid, required this.baseUrl});

  @override
  Widget build(BuildContext context) {
    final cs = Theme.of(context).colorScheme;
    if (uuid.isEmpty) {
      return Container(
        color: cs.surfaceContainerHighest,
        child: const Icon(Icons.music_note_rounded),
      );
    }
    return Image.network(
      '$baseUrl/api/track/cover?uuid=$uuid',
      fit: BoxFit.cover,
      errorBuilder: (_, _, _) => Container(
        color: cs.surfaceContainerHighest,
        child: const Icon(Icons.broken_image_rounded),
      ),
    );
  }
}

class _AlbumCover extends StatelessWidget {
  final Album album;
  final String baseUrl;

  const _AlbumCover({required this.album, required this.baseUrl});

  @override
  Widget build(BuildContext context) {
    final cs = Theme.of(context).colorScheme;
    if (album.coverUri.isEmpty) {
      return DecoratedBox(
        decoration: BoxDecoration(
          color: cs.surfaceContainerHighest,
          borderRadius: BorderRadius.circular(6),
        ),
        child: const Icon(Icons.album_rounded),
      );
    }
    return ClipRRect(
      borderRadius: BorderRadius.circular(6),
      child: Image.network(
        '$baseUrl/${album.coverUri}',
        fit: BoxFit.cover,
        errorBuilder: (_, _, _) => const Icon(Icons.album_rounded),
      ),
    );
  }
}

class _SongRow extends StatelessWidget {
  final Track song;
  final bool canControl;
  final bool playOnlyButton;
  final VoidCallback? onPlay;
  final VoidCallback? onNext;
  final VoidCallback? onAddTail;
  final VoidCallback? onAddToLibrary;
  final VoidCallback? onExcludeToggle;
  final VoidCallback? onDelete;
  final bool excluded;
  final String baseUrl;

  const _SongRow({
    super.key,
    required this.song,
    required this.canControl,
    required this.playOnlyButton,
    required this.baseUrl,
    this.onPlay,
    this.onNext,
    this.onAddTail,
    this.onAddToLibrary,
    this.onExcludeToggle,
    this.onDelete,
    this.excluded = false,
  });

  @override
  Widget build(BuildContext context) {
    return ListTile(
      key: key,
      onTap: null,
      leading: SizedBox(
        width: 40,
        height: 40,
        child: ClipRRect(
          borderRadius: BorderRadius.circular(6),
          child: _Cover(uuid: song.uuid, baseUrl: baseUrl),
        ),
      ),
      title: Text(song.title, maxLines: 1, overflow: TextOverflow.ellipsis),
      subtitle: Text(
        '${song.artist.isEmpty ? AppLocalizations.of(context).unknownArtist : song.artist} · ${song.albumId}',
      ),
      trailing: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          Text(
            _fmt(song.duration),
            style: Theme.of(context).textTheme.bodySmall,
          ),
          const SizedBox(width: 6),
          Text(song.moduleId, style: Theme.of(context).textTheme.bodySmall),
          IconButton(
            onPressed: onPlay,
            icon: const Icon(Icons.play_circle_fill_rounded),
            tooltip: AppLocalizations.of(context).playTooltip,
          ),
          PopupMenuButton<String>(
            enabled: canControl,
            onSelected: (v) {
              if (v == 'next') onNext?.call();
              if (v == 'tail') onAddTail?.call();
              if (v == 'library') onAddToLibrary?.call();
              if (v == 'exclude') onExcludeToggle?.call();
              if (v == 'remove') onDelete?.call();
            },
            itemBuilder: (_) {
              final l10n = AppLocalizations.of(context);
              return [
                PopupMenuItem(value: 'next', child: Text(l10n.playNext)),
                PopupMenuItem(value: 'tail', child: Text(l10n.addToQueueTail)),
                if (onAddToLibrary != null)
                  PopupMenuItem(value: 'library', child: Text(l10n.addSource)),
                PopupMenuItem(
                  value: 'exclude',
                  child: Text(excluded ? l10n.removeExclusion : l10n.exclude),
                ),
                if (onDelete != null)
                  PopupMenuItem(value: 'remove', child: Text(l10n.removeShort)),
              ];
            },
          ),
        ],
      ),
    );
  }

  static String _fmt(double sec) {
    if (sec <= 0) return '0:00';
    final m = (sec ~/ 60).toString();
    final s = (sec % 60).toInt().toString().padLeft(2, '0');
    return '$m:$s';
  }
}
