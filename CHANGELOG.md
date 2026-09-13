# Changelog

## 0.7.0

- The Lyrion album overview can now be sorted: recently added, artist/year, album, or year/album,
  each ascending or descending. The server sorts wherever it can — it knows the library's sort
  names and files “The Beatles” under B. Two of its limits are worked around: it cannot sort by
  album title at all, so that order is applied locally, and “recently added” only ever returns as
  many albums as the server's own `browseagelimit` allows, which the status line says out loud
  instead of pretending it is the whole library. Descending is not a server option either and is
  applied to the fetched list.
- Added a favorites filter to the Lyrion overview and a star badge on every album tile that also
  changes the status. Favorites live on the server, so the change is there immediately and in
  every other Lyrion client.
- Added a favorites sync: one button in the Lyrion view mirrors the albums marked as favorites
  into a folder of your choosing, and the target folder is the only thing to configure. The files
  come from the server over HTTP, so the library itself is never touched — not even read — and no
  mount or rsync is needed. Only what is missing or has changed is transferred, compared by size
  *and* modification time, and anything in the folder that is no longer a favorite is removed,
  empty directories along with it.
- The favorites sync now belongs to the application instead of the Lyrion view, and it says what
  it is doing. The view is rebuilt every time it is opened, so a running sync used to become
  invisible the moment you glanced at the playlist — and the next click started a *second* run on
  the same folder, with both runs fetching into the same `.part` file and deleting by the plan each
  had made for itself. The run now survives the view: reopening it shows the running sync, its
  progress and its result. Progress and result have their own line under the status line, so
  re-sorting, filtering or opening an album no longer wipes them, and the result stays readable
  until it is dismissed.
- Fixed: a sync that had never been cancelled reported “Sync cancelled”. `HttpClient` reports its
  own timeout as a cancelled operation, and the whole library listing — some 20 MB — went through
  the short timeout meant for library queries. The bulk queries now have their own long timeout,
  and a timeout is told apart from a cancellation and named as such.
- Fixed: the sync did its planning on the UI thread — reading the details of thousands of files,
  walking the whole target folder and parsing 20 MB of JSON — so the window stood still for the
  duration. It now runs on a background thread.
- Cancelling the sync is acknowledged straight away instead of leaving the last progress line
  standing, and a cancelled run reports what it had already fetched and removed.
- "Refresh library" now asks the server to look for new and changed music first, and only then
  fetches the list. Refetching alone showed the same state: a record added a minute ago is one the
  server does not know about yet. Progress is reported per scan step, the run can be cancelled -
  which tells the server to stop as well, rather than merely looking away - and the album list is
  refetched once the server is done. The step description comes from the server and is therefore
  in the server's language; the sentence around it is translated.
- The Lyrion favorites sync, the favorites cleanup and the library scan now share one slot: at most
  one of them runs at a time. This is not tidiness but necessity - while the server is scanning,
  its album and track lists are in motion, and a sync reading into that would fetch a state that
  never existed. The button belonging to the running task cancels it; the other is disabled while
  it runs, so a refused click cannot paint over a running task's progress.
- Favorite entries with no matching album — renamed, re-tagged or deleted since they were
  bookmarked — can now be taken out of the server's favorites. The button appears on the sync line
  once a run found any, and asks first, listing every entry by name. The music itself is never
  touched.
- Fixed: “N failed, see log” and the note about unresolvable favorites pointed at a log that is off
  unless the application is started with `--debug`. Sync failures are now always written.
- The Lyrion overview now loads the library in one go instead of page by page. The server hands
  over 6500 albums in a fraction of a second, the scrollbar is honest from the start, and sorting
  or filtering no longer has to ask the server again.
- Fixed: while a Lyrion track was playing, MPRIS sent no cover at all, so Waybar and notifications
  showed none. The artwork of a streamed track is now cached as a file like an embedded cover.
- Fixed: `xesam:url` mangled the address of a Lyrion stream into an unusable
  `file:///https%3A///…` instead of passing the stream URL through.
- The Lyrion settings no longer show the client-name field. It never had any effect: FerrumPlay
  plays the server's files itself and does not register as a player.

## 0.6.0

- Added a Lyrion Media Server browser with instant album search, cover grid, album tracks, and
  direct local playback through the server's HTTP stream.
- Lyrion albums act as temporary playlists: next, previous, shuffle, repeat, cover art, and
  “jump to current track” stay within the active Lyrion album.
- Added the integrated MP3 tag editor for common album metadata, cover replacement, tag cleanup,
  configurable defaults, and track-by-track titles and numbers.
- The file-name pattern used when tagging MP3 files is now configurable in the settings
  (%artist%, %albumartist%, %album%, %title%, %track%, %totaltracks%, %disc%, %year%, %genre%)
  with a live example of the resulting name.
- The zero-padding setting for track numbers now governs the tag, the track-number fields in the
  tag editor, and the file name alike, so the field shows exactly what gets written.
- In the tag editor, the album-artist and album-sort fields now follow the artist and year fields
  live whenever their automatic settings are on, instead of only at save time.
- The Lyrion album overview is no longer capped at 100 albums: it loads further pages while
  scrolling and draws its tiles through a virtualizing ItemsRepeater, as the FerrumPix grid does.
  Covers are fetched per visible tile as server-side thumbnails, cached, and released again when a
  tile scrolls out of view.
- While the MP3 tag editor is open, the cover column belongs to the files being tagged: it shows
  their cover and album details instead of the running playback, and it is where a new cover is
  dropped — the drop area highlights while an image hangs over it, and the pixel size of the cover
  currently shown is printed below it. Leaving the tag editor hands the column back to playback.
- Writing MP3 tags now drops the cached cover art for the affected files, so a newly set cover —
  and its size — shows up at once instead of after a restart; the playlist and the now-playing
  details are refreshed along with it.
- The cover column now shows the year of the current track below the album name.
- Lyrion tracks now carry year, format, sample rate, bitrate, and file size, so the cover column
  and the Lyrion track list show the same technical details as local files.
- The tag editor, the Lyrion browser and the converter now translate themselves: they are built
  after the window-wide translation pass and used to stay German in every other language.
- Filled the gaps left in the Chinese and Italian resources, where the whole converter was still
  showing English, and replaced the English option names that survived inside otherwise translated
  sentences in eight further languages.
- Translated the complete 0.6.0 interface — Lyrion browser, tag editor, converter and the new
  settings — into all supported languages, including the status and error messages the panels
  produce at runtime.
- Improved Audio CD playback: the temporary CD playlist is detected automatically, MP3-tag actions
  are hidden for CD tracks, and the player keeps per-track CD timings instead of disc timings.

## 0.5.0

- Added the complete FerrumPix language selection, including localized resource files for all
  supported languages.
- The About section now reports whether libmpv, FFmpeg, and cdparanoia are available.
- Aligned the custom window-control glyphs with FerrumPix.

## 0.4.2

- `Ctrl+A` selects every track in the playlist.
- Multiple selected tracks, albums, and folders can now be converted together from the context
  menu; selected album and folder headers include all of their tracks.
- The conversion queue now retains album and folder headers and displays each track's tagged
  track number.

## 0.4.1

- The converter context-menu action now respects a multi-selection: right-clicking one of the
  selected tracks converts every selected track instead of only the clicked one.

## 0.4.0

- Added the integrated Audio Converter. Convert selected tracks and albums to MP3 (CBR or VBR),
  FLAC, or Ogg Vorbis; a selection or each source folder can also become one ordered file.
- The conversion queue shows every source and keeps the item currently being processed visible.
- Audio-CD tracks can be ripped directly to files through cdparanoia.
- The playlist header has a button that jumps directly to the current track, including its album
  group, in long playlists.

## 0.3.0

- The playlist can now be reordered by dragging tracks. A context menu on a track or album can
  play it, include or exclude a track, collapse an album, or remove it from the list.
- When shuffle is on, the playlist itself now shows the shuffled playback order instead of the
  original order.
- On startup, the last selected track is brought into view in the playlist.

## 0.2.0

- MPRIS: Waybar, playerctl, system notifications, and multimedia keys can now control FerrumPlay
  while its window is in the background. The title, artist, album, duration, and cover art are
  published as metadata.
- FerrumPlay now runs as a single instance. Opening a file or folder while it is already running
  sends it to the existing instance for playback. The desktop entry accepts both files and folders.
- ReplayGain volume normalization: off, track, album, or automatic, with a configurable preamp.
- Missing tracks are detected on startup, dimmed, and skipped. They return when the file is
  available again, and a button removes all missing entries at once. If a track cannot be played,
  playback continues with the next one instead of stopping.
- Audio CDs on Linux: an inserted disc appears automatically as its own playlist beside the file
  playlist. Tracks are listed and played individually; the CD playlist disappears on eject and is
  never saved permanently.
- Sending a file to an already running instance now not only starts it, but also expands its group
  and scrolls the playlist directly to the requested track.

## 0.1.0

Initial release.

FerrumPlay plays music in MP3, FLAC, OGG, Opus, M4A, and other common formats. The playlist is
grouped by album; cover art and track details are shown on the left, with transport controls along
the bottom and a rotary volume control. Files and folders can be dropped onto the window or passed
on launch (`FerrumPlay ~/Music/Album`); the first track starts playing automatically.

Accent colour, font size, language (German or English), and application scaling are configurable.
The playlist and last played track are restored on the next launch.
