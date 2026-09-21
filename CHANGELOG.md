# Changelog

## 0.9.7

- The window buttons can now sit on the left of the title bar. The application draws that bar
  itself and inherits nothing from the desktop, so whoever has close, maximize and minimize on
  the left stood here without them. The setting is in Darstellung and reads Systemvorgabe, Links
  or Rechts; by default it asks the desktop - KDE, GNOME and its relatives, Xfce - and follows
  both the side and the order it names. The wordmark moves to whichever side stays free.
- The title-bar wordmark and the application icon have been revised. The wordmark now uses the
  same spacing as FerrumPix, while the icon artwork and all of its packaged sizes were refreshed
  to keep the identity clear at small task-bar sizes as well as in the window.
- An inserted audio CD now opens its own playlist automatically, rather than merely appearing in
  the sidebar until it is selected by hand. When MusicBrainz finds several editions with the same
  table of contents, the choice list reliably transfers the clicked edition to the lookup.
- The converter fixes the output mode to one result per track for an audio CD and disables the
  other modes. A CD is physically read one track at a time; joining those reads was never a valid
  rip and the service consequently rejected it only after the job was started.
- The context menu of an audio-CD album now offers Eject CD. It opens the drive tray and immediately
  removes the temporary CD playlist, including a running playback or rip. The new labels and the
  error message are available in every application language.


## 0.9.6

- The application is now called FerrumKix. The old name was already taken by other products, and a
  name that has to share its search results is worth changing before 1.0 rather than after it.
  Everything that carried the name moved with it: the binary, the application id
  `io.github.Bitpainter75.FerrumKix`, the D-Bus name behind single-instance and MPRIS, the AUR
  package `ferrumkix-bin` and the GitHub repository.
- Settings, playlist and cover art come along by themselves. The first start renames
  `~/.config/FerrumPlay` to `~/.config/FerrumKix` and `~/.cache/FerrumPlay` to
  `~/.cache/FerrumKix`. It is a rename and not a copy: old and new sit side by side in the same
  parent, so even a cache of a few hundred megabytes moves in no time. An existing FerrumKix
  folder is never touched - whoever has both has used the new name already, and that state is the
  younger one. A move that fails is swallowed; the application then starts on factory settings and
  the old folder lies there untouched.
- The AppStream metadata declares the old id under `<provides>` and `<replaces>`, and the AUR
  package replaces `ferrumplay-bin`, so software centres and package managers see one application
  that was renamed rather than two that compete.
- The icon is new as well. The old one carried the play triangle of the old name; the record with
  the level bars beside it stands on its own and survives being scaled down to 16 pixels, which is
  where a window list and a task bar show it.

## 0.9.5

- The converter names its files from a pattern of its own, set in the Audio-Konverter section of
  the settings with the placeholders already known from tagging and a sample name below the field.
  The name of a converted file used to come out of the code - number, artist, title - and only a CD
  rip took a pattern, the one meant for tagging. The pattern now holds for every conversion: files
  from the list, a CD rip and the tracks cut out of a CUE. The extension comes from the chosen
  format, not from the .mp3 the tag editor appends.
- The zero padding of the track number is a switch of its own beside it. Whoever renames while
  tagging does not necessarily mean the names a conversion creates, and one setting for both would
  drag the two along with each other. A CD rip to MP3 still goes through the tag writer afterwards,
  because it is the only place that knows the whole ID3v2 ruleset, but it no longer renames what
  the converter has just named.
- A missing number is no longer written as a zero. %track%, %totaltracks% and %disc% stay empty
  where there is nothing, the way %year% has always done, so a file without a number is not called
  "00 - Title"; what is left of the separators around an empty placeholder is removed, and a file
  that carries no title at all falls back to its own name, as the playlist shows it.
- The conversion queue reads like the playlist: a check box, the position within its group and the
  file details below the title, with the row styles moved into the theme so the two lists cannot
  drift apart. A title without its check stays in the list but is left out of the job. This
  uncovered a mismatch that was there before - the service sorts the tracks itself and reports
  positions in that order, so a status could land in the wrong row whenever the selection was
  sorted differently. The status bar only appears once it has something to say, and the settings
  make room for the list while a run is going.
- The application scale moves in single percent, under the pointer and at the wheel. Five percent
  was too coarse when a screen sits exactly between two steps, and the factor that fits could not
  be reached at all as long as the wheel kept its five.
- "Apply changes" in the tag editor carries dark text again. It used the class accent, which
  belongs to the Fluent theme and brings its own white text per state; both filled buttons share a
  class of our own now, one that Fluent does not know.

## 0.9.4

- The interface comes in three appearances, chosen in the settings the same way FerrumPix offers
  them: dark, gray dark and gray light. The colours are not four stylesheets but one set of keys
  that is rewritten at runtime, so a change takes effect at once, without a restart. A genuinely
  light appearance - dark text on a white ground - was built alongside them and dropped again: it
  looked wrong in this player.
- The player is part of the appearance now. The footer deck, its edge and the seek track, the veil
  over the playlist and over the converter, tag and Lyrion areas, and the scrim over the blurred
  cover used to stand as fixed dark values inside the views; they come from the chosen appearance
  instead, so the two gray ones are not a dark player with a gray window around it.
- Drop-down lists, menus and list rows follow a change of appearance. They read the keys of the
  Fluent theme and not ours, and those are now mirrored after every change - otherwise they stayed
  in the colours of the previous appearance.

## 0.9.3

- The MP3 tag editor fills in empty fields one by one instead of all or nothing. Until now the
  selection decided as a whole: if a single file carried any tag at all, every empty field of
  every other file stayed empty and had to be typed by hand. What is in the file still wins
  wherever there is something; only what is missing gets a proposal - the file name without its
  extension as the title, the name of the parent folder as artist and album. A field holding
  nothing but spaces counts as empty, because that is what it looks like in the form and it would
  be worth as little as a tag.
- A missing track number is proposed from the order of the file names. The list is sorted by file
  name as soon as any number is missing, so the rows stand in the order they are numbered in.
  Numbers that are already in the files are left alone, and where every file has one the
  selection keeps its order - there is nothing to guess there.
- The genre box completes what is typed from the genre defaults in the settings. After "Ro" the
  field reads "Rock" with the added part selected: the next character replaces it, backspace
  throws it away, and the typed characters keep their own capitalisation. Completion only happens
  at the end of the text, a genre that is in no default can still be typed freely, and the
  drop-down list is unchanged.
- While playback runs and nothing has been done in the application for a minute, the view
  returns to the track that is playing and stays with it from then on: the list scrolls along with
  every following track. Any movement of the mouse or a key press postpones it again. The areas
  that are worked in are left out - the audio CD, the converter and the tag editor - because a
  view that jumps away on its own would be in the way there. The Lyrion area follows only while it
  is the one playing, so it is never closed unasked.
- Page up, page down, home and end scroll the playlist even when the focus sits elsewhere, on the
  volume knob or one of the buttons in the bar. They scroll and do not select: what is selected
  decides what gets tagged or converted, and a key pressed from outside the list must not overturn
  it. With the list focused, its own handling including the selection is unchanged.
- The filled accent buttons - "Start conversion" and "Apply changes" - carry dark text again
  instead of white, in the very dark shade of the accent colour that the play symbol also uses.
  Both now take their surface from the same keys as well, so they follow a changed accent colour
  alike.

## 0.9.2

- Lyrion can keep up to three independent server profiles, each with its own tab name, address,
  favorites-sync target and remote-player selection. The tab uses the configured name or, when
  that is empty, the host name of the server address.
- The Lyrion overview now reports album and favorite counts. Filtering to favorites also calculates
  the number of tracks and their total size, so the required sync space is visible before starting
  the transfer. An opened album shows its total running time and size as supplied by the server.
- Album artwork is fetched only for tiles actually inside the visible Lyrion viewport; prepared
  layout-buffer tiles no longer start network requests.
- All album favorites can be removed in one confirmed action. The list is refreshed afterwards,
  rather than continuing to show stale stars until the next application start.
- Local playlists can be saved and loaded as portable M3U/M3U8 files. They preserve an optional
  FerrumKix sync target in a comment, resolve relative paths against the playlist, and can also be
  opened through the command line, file manager, or drag and drop.
- Entering the Audio-CD tab stops playback from another source and uses the first CD track as the
  display context, so identified title, album, and cover appear in the cover column immediately.
- CD-rip folder patterns can create album subfolders using the familiar tag placeholders. The
  converter shows the complete resulting CD output path in its editable output field.
- Converting or ripping now asks once before overwriting existing output files. It also refuses to
  convert into a source folder when the selected target format matches a source file there.

## 0.9.1

- Scrolling through the Lyrion albums no longer crashes the application. The scale of 0.9.0 is a
  render transform on a frame that is sized down by the same factor, and that frame clipped its
  contents as well. Avalonia works out the viewport it hands to every virtualising control by
  clipping at the frame's bounds first and taking the scale out afterwards - so the division
  happened twice. At factor 1.68 the album grid was told it had 660x375 to fill instead of
  1108x630, and once scrolled the window sat in the wrong place too; the repeater's bookkeeping of
  the tiles it had built ran apart and the layout pass threw. The rounded corners are cut by a
  frame below the scale now, where clipping is harmless.
  Every list in the application was given the same wrong viewport - the playlist above all. Those
  did not crash, but they built too few rows and filled in late while scrolling. That is gone with
  the same fix.
- Context menus, tooltips and drop-down lists grow with the application again. They are drawn into
  a window of their own and are therefore no children of the frame that the scale enlarges, so
  since 0.9.0 they stayed at their original size while everything around them grew; up to 0.8.0 an
  environment variable had scaled the whole windowing system. The factor is applied in the popup
  template rather than to each menu, which takes submenus and drop-downs along with it.
- The update check no longer announces an update that is none. The number after the hyphen is the
  package revision, not a program version - 0.9.1-1 and 0.9.1-2 are the same FerrumKix - but it
  went into the comparison, so a repackaged release looked like a new one.

## 0.9.0

### MP3 tags

- Files without any tags get a proposal instead of an empty form. Sorted by file name, the track
  numbers run through from one, the file name becomes the title, and the name of the parent folder
  is offered as album and artist. It stays a proposal: nothing is written before "Apply changes",
  and every field can still be edited.
- The hint line reports the size of the cover that is actually in the files. It used to show the
  configured edge length and JPEG quality, which describe what an image would be brought to and
  said nothing about the one that is there - and that is the number that decides whether an image
  has enough to give.
- Ripping an audio CD writes the MP3 tags through the same writer as the tag editor. FFmpeg alone
  knew nothing of the ID3v2 conventions the editor follows - a track number padded against the
  total, an album sort order, and old tags cleared out when that setting is on - so a ripped track
  and a tagged one ended up carrying different tags for the same album. The file name now comes
  from the same tag values as well, so the identified title reaches the file name too.
- Padding the track number with zeros is applied everywhere the setting promises. It only ever
  reached the tag editor: the converter wrote the tag unpadded outside CD rips, and both the
  converted and the CUE-split file names used a hardcoded two digits - wrong in both directions,
  padding with the setting off and stopping at two digits on an album of a hundred. The number of
  digits comes from the album, determined per folder for the whole run.

### Cover

- The cover column can be given an image through a file picker. Dragging one in remains, but it
  depends on the desktop: under XWayland the bridge between the two worlds hands a drop the
  contents of the clipboard instead of the dragged file, and no application-side fallback reaches
  around that.
- The drop zone says what it is. Passing over the column marks it with an accent border, and
  during a drag a layer with an icon and a prompt lies over the cover - a border alone drowns in
  the artwork underneath. A drop that carries nothing usable is no longer swallowed in silence.

### Window and settings

- The application scale no longer needs a restart. It went through an environment variable that
  only Avalonia's X11 path reads; it is a transform on the window itself now, takes effect the
  moment the slider is released, and moves with the window when it goes to a screen with a
  different factor. One percent per notch instead of five.
- A switch for the diagnostic log sits in the settings under "Troubleshooting", with the path to
  the folder holding logs and settings and a button that opens it. The log could only be turned on
  with --debug before, which means restarting from a terminal - no use to anyone already looking
  at the thing that went wrong. Exceptions still go to errors.log regardless.
- The play button no longer shows "pause" at startup. mpv reports the current value right after
  the pause property is observed, and idle means "not paused" - with nothing loaded there is no
  pause state to report.

### Conversion and Lyrion

- A conversion or rip can be stopped. The back arrow of the converter turns into "Cancel" while a
  run is going, and what is already written stays; only the file in flight is discarded.
- A run covers the window while it lasts. Converting reads the CD and writes files in one long
  stretch, and an action taken meanwhile - switching the view, ejecting the disc, starting a second
  run - would have pulled the ground out from under it. The cover says what is happening and takes
  every pointer and key until the run ends.
- The check mark in a Lyrion track list does something now. It sat there looking like a switch and
  was decoration - fixed, checked and dead - so that the row lined up with the playlist next to it.
  Unchecking a track now takes it out of playback in FerrumKix, just as in the playlist; the state
  is remembered per track of the server, so sorting or reopening the album keeps it. On a device
  the check marks are greyed out: there the server loads the whole album and runs the order itself,
  and a switch that cannot do anything should not pretend otherwise.
- The target folder for ripping sat in the Lyrion Media Server section of the settings, where it
  has nothing to do: it belongs to the audio converter, and that is where it now is.

### Version and packages

- The version in the settings is the one from the VERSION file, the same file the build and the
  packages take their number from - including the package revision, which the three-part assembly
  number could not carry. Opening the settings asks GitHub once per session which version is
  published; if it differs from the running one, a link to the release page appears next to the
  version. Nothing but that file is fetched, and a failed request stays silent.
- The install table in the README names every package that is built: AppImage, ZIP, DEB, RPM and
  the AUR one. Arch users had no instruction at all before - ferrumkix-bin was mentioned once, in
  passing, as something that declares dependencies.
- The number field for the cover edge length was square on its right side and its arrows were
  black on a dark ground. Its styling had never taken effect: the two spinner buttons live in the
  template of the spinner *inside* the template of the number field, and the rules were written for
  one level only.

## 0.8.0

- FerrumKix can now act as a remote control for the Lyrion server. A picker next to the album
  search chooses where playback runs: locally, as before, or on any player registered with the
  server. With a device chosen, the whole transport bar controls it - play, pause, next, previous,
  seek, volume, mute, shuffle and repeat - and title, cover, position and volume come from the
  server's own status, polled once a second because JSON-RPC has no subscription. Local playback
  stops when a device is picked; nobody wants two sources at once.
  This needs no SlimProto. The three routes sketched in the audit answer a different question -
  they were about FerrumKix *appearing* as a player in the Lyrion interface. Controlling one that
  is already there is plain JSON-RPC with the player's id.
- MPRIS follows the device: Waybar and notifications show what plays on it, and the media keys
  control it. This needed no extra work - MPRIS builds its state from the same display fields.
- "Jump to current track" now opens the album playing on the device. The album id comes from the
  status query, and the track is matched by name rather than by position, because the device runs
  its own order and under shuffle the position would point elsewhere.
- Audio CDs are now identified. A CD carries nothing itself - its table of contents knows only
  where each track starts and ends - so the titles read "Track 01" and the album "Audio CD", and
  that is what ended up in the MP3 tags when ripping. FerrumKix now computes the MusicBrainz disc
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
- The Lyrion settings no longer show the client-name field. It never had any effect: FerrumKix
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

- MPRIS: Waybar, playerctl, system notifications, and multimedia keys can now control FerrumKix
  while its window is in the background. The title, artist, album, duration, and cover art are
  published as metadata.
- FerrumKix now runs as a single instance. Opening a file or folder while it is already running
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

FerrumKix plays music in MP3, FLAC, OGG, Opus, M4A, and other common formats. The playlist is
grouped by album; cover art and track details are shown on the left, with transport controls along
the bottom and a rotary volume control. Files and folders can be dropped onto the window or passed
on launch (`FerrumKix ~/Music/Album`); the first track starts playing automatically.

Accent colour, font size, language (German or English), and application scaling are configurable.
The playlist and last played track are restored on the next launch.
