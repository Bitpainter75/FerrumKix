# Changelog

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
