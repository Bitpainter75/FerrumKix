# Changelog

## 0.8.1

- Ripping an audio CD now writes the MP3 tags through the same writer as the tag editor. FFmpeg
  alone knew nothing of the ID3v2 conventions the editor follows - a track number padded against
  the total, an album sort order, and old tags cleared out when that setting is on - so a ripped
  track and a tagged one ended up carrying different tags for the same album. The file name now
  comes from the same tag values as well, so the identified title reaches the file name too.
- A conversion or rip can be stopped. The back arrow of the converter turns into "Cancel" while a
  run is going, and what is already written stays; only the file in flight is discarded.
- A run now covers the window while it lasts. Converting reads the CD and writes files in one long
  stretch, and an action taken meanwhile - switching the view, ejecting the disc, starting a second
  run - would have pulled the ground out from under it. The cover says what is happening and takes
  every pointer and key until the run ends.
- The check mark in a Lyrion track list does something now. It sat there looking like a switch and
  was decoration - fixed, checked and dead - so that the row lined up with the playlist next to it.
  Unchecking a track now takes it out of playback in FerrumPlay, just as in the playlist; the state
  is remembered per track of the server, so sorting or reopening the album keeps it. On a device
  the check marks are greyed out: there the server loads the whole album and runs the order itself,
  and a switch that cannot do anything should not pretend otherwise.
- The target folder for ripping sat in the Lyrion Media Server section of the settings, where it
  has nothing to do: it belongs to the audio converter, and that is where it now is.
- The version in the settings is the one from the VERSION file, the same file the build and the
  packages take their number from - including the package revision, which the three-part assembly
  number could not carry. Opening the settings asks GitHub once per session which version is
  published; if it differs from the running one, a link to the release page appears next to the
  version. Nothing but that file is fetched, and a failed request stays silent.
- The number field for the cover edge length was square on its right side and its arrows were
  black on a dark ground. Its styling had never taken effect: the two spinner buttons live in the
  template of the spinner *inside* the template of the number field, and the rules were written for
  one level only.

## 0.8.0

- FerrumPlay can now act as a remote control for the Lyrion server. A picker next to the album
  search chooses where playback runs: locally, as before, or on any player registered with the
  server. With a device chosen, the whole transport bar controls it - play, pause, next, previous,
  seek, volume, mute, shuffle and repeat - and title, cover, position and volume come from the
  server's own status, polled once a second because JSON-RPC has no subscription. Local playback
  stops when a device is picked; nobody wants two sources at once.
  This needs no SlimProto. The three routes sketched in the audit answer a different question -
  they were about FerrumPlay *appearing* as a player in the Lyrion interface. Controlling one that
  is already there is plain JSON-RPC with the player's id.
- MPRIS follows the device: Waybar and notifications show what plays on it, and the media keys
  control it. This needed no extra work - MPRIS builds its state from the same display fields.
- "Jump to current track" now opens the album playing on the device. The album id comes from the
  status query, and the track is matched by name rather than by position, because the device runs
  its own order and under shuffle the position would point elsewhere.
- Audio CDs are now identified. A CD carries nothing itself - its table of contents knows only
  where each track starts and ends - so the titles read "Track 01" and the album "Audio CD", and
  that is what ended up in the MP3 tags when ripping. FerrumPlay now computes the MusicBrainz disc
  id from the track lengths alone and looks the CD up; the CD itself is never read for this and
  nothing but those lengths is sent. freedb, the obvious candidate, was shut down in 2020.
  Where several editions share the same track lengths - the disc id is a fingerprint of the table
  of contents, not of the pressing - you are asked which one it is, with year, country and edition
  note. A button in the Audio CD toolbar looks the disc up again, so a different edition can be
  chosen afterwards. What is found goes into the MP3 tags when converting: title, artist, album,
  album artist, track number and year. Placeholders never do - an unidentified CD gets no tags
  rather than wrong ones.
- The audio CD identification now fetches the cover art as well, from the Cover Art Archive, at the
  size configured for MP3 tags. That setting existed (default 800) but could not be reached; it is
  now in the MP3 tags section and says that it governs this image too. The smallest offered size
  that is still large enough is taken - downscaling is possible, upscaling is not. Tagging already
  scaled to it and never upscales.
- Ripped files now carry the cover art, embedded, at the size configured for MP3 tags. This is
  what MusicBrainz Picard, the project's own tagger, does with the same archive. OGG is left out
  deliberately: ffmpeg cannot attach a picture to an Ogg container, and attempting it produces a
  zero-byte file rather than an error.
- Ejecting a CD discards what was recognised, and so does swapping in a different one. Two CDs with
  the same number of tracks produce the same playlist entries, so the track objects survive the
  swap - without clearing them, the new CD would have shown the old CD's titles, and would have
  kept them if it was not listed at MusicBrainz.
- Added a target folder for ripping audio CDs to the settings, defaulting to the user's music
  folder. A CD has no folder of its own to suggest, so the converter used to fall back to the last
  browsed folder.
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
- The settings now name MusicBrainz and the Cover Art Archive under Technology, and say plainly
  what differs between them: the CD details are public domain, the cover art is not. Both are in
  THIRD-PARTY-NOTICES.txt as well.
- The MusicBrainz request identifies the application and its version, and nothing else. The usual
  contact address is deliberately left out: it would go to a third-party service on every lookup by
  every user. The price is known - without a contact, MusicBrainz throttles sooner.
- Switching areas now clears the status line. A message like "Audio CD identified: …" belongs to
  what was just done, not to whatever is looked at next - left standing it reads as if it belonged
  to the new view.
- Both search fields now carry a cross to clear the term, as in FerrumPix. It appears only when
  there is something to clear. The playlist quick search had the command for it all along but no
  button to reach it.
- The cover-size field follows FerrumPix's NumericUpDown styling, so it sits beside the text boxes
  and drop-downs instead of bringing Fluent's own frame and height along.
- Fixed: with a Lyrion device selected, the stop button went to the device even while an audio CD
  was playing locally, so the CD kept going. The transport bar now follows what is actually being
  heard: picking a device or starting an album on it hands the bar to the device, and any local
  playback takes it back.
- Fixed: after stopping, the play button restarted whatever had played last, even if you had
  meanwhile opened a different Lyrion album or switched to another list. It now starts what is on
  screen. Stopping in the middle of an album and pressing play still resumes that same track - the
  list you are looking at only takes over when the track you stopped is not part of it.
- Fixed: the album order was ignored whenever the list was filtered by a search term. The server
  accepts `sort:` together with `search:` and then returns its own full-text ranking regardless -
  measured against LMS 9.1.2, the same term gave the identical order line for line under
  artist/year, year/album and recently added. Search results are now ordered in the application.
  One limit stays and is stated in the status line: "recently added" cannot be reproduced, because
  the album query does not say when an album entered the library. A second, smaller one: the server
  knows the library's sort names and files "The Beatles" under B, while a locally ordered search
  result files it under T.
- Fixed: a dismissed message ("N favorites removed") came back as soon as the player views were
  switched. The dismissal was remembered by the Lyrion view, which is rebuilt every time it opens -
  the same mistake the sync state itself had. It now belongs to the message, in the service.
- Fixed: shuffle had no effect on an audio CD. Only the file playlist and, since 0.7.0, the Lyrion
  album were shuffled; the CD handed out its tracks in disc order no matter what the switch said.
  It now has a play order of its own, built the same way. Repeat was never affected - it does not
  depend on the order but on the wrap in FindNeighbour.
- Fixed: clicking into the seek bar on an audio CD left the time reading 0:00 and the bar empty.
  A CD runs in mpv as the whole disc with start=#N, so its clock counts from the start of the disc
  while the display counts within the track. The seek handed over the track-relative position
  unchanged, which landed at that many seconds into the *disc* - the display then subtracted the
  track's start, got a negative number, clamped it to zero, and stayed there.
- Fixed: checking for a CD every five seconds pulled the tray shut while you were trying to put a
  disc in. Opening an optical device is not a harmless file open; many drives close the tray on it.
  The drive is now opened non-blocking and asked for its state first, and the disc is only touched
  when there actually is a readable one.
- A throttled MusicBrainz request is retried rather than failed. The service allows one request per
  second and answers 503 otherwise, which means "try again shortly", not "no".

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
