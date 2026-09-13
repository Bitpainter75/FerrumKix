Imports System
Imports System.Collections.Generic
Imports System.Linq
Imports System.Threading.Tasks
Imports Avalonia.Threading
Imports FerrumPlay.Services

Namespace ViewModels

    ''' <summary>Erkennt eine eingelegte Audio-CD und traegt die gefundenen Angaben in die Titel ein.
    '''
    ''' <para>Eine CD bringt selbst nichts mit: das Inhaltsverzeichnis kennt nur Anfang und Ende
    ''' jeder Spur. Ohne Erkennung heissen die Titel "Titel 01" und das Album "Audio-CD" - und
    ''' genau das landete bisher auch in den MP3-Kennzeichen beim Konvertieren.</para>
    '''
    ''' <para>Gefragt wird bei MusicBrainz ueber die Disc-Kennung, die sich allein aus den
    ''' Spurlaengen berechnet (<see cref="MusicBrainzDiscService.DiscIdOf"/>). Die CD wird dafuer
    ''' nicht gelesen; uebertragen werden nur diese Laengen.</para></summary>
    Partial Public NotInheritable Class MainWindowViewModel

        ''' <summary>Die Kennung der zuletzt nachgeschlagenen CD. Der Ueberwacher fragt das Laufwerk
        ''' in kleinen Abstaenden ab - ohne diese Merkung fragte er bei MusicBrainz im selben Takt
        ''' nach und riebe dem Nutzer alle paar Sekunden dieselbe Auswahl unter die Nase.</summary>
        Private _identifiedDiscId As String = String.Empty

        ''' <summary>Das Inhaltsverzeichnis der eingelegten CD. Wird gebraucht, um die Erkennung
        ''' auf Wunsch noch einmal anzustossen - etwa um eine andere Ausgabe zu waehlen.</summary>
        Private _audioCdToc As AudioCdService.DiscToc

        ''' <summary>Ob sich die eingelegte CD nachschlagen laesst. Der Knopf steht nur da, wenn
        ''' wirklich eine CD im Laufwerk ist.</summary>
        Public ReadOnly Property CanIdentifyAudioCd As Boolean
            Get
                Return _audioCdToc IsNot Nothing AndAlso _audioCdTracks.Count > 0
            End Get
        End Property

        ''' <summary>Schlaegt die CD noch einmal nach, auch wenn sie schon erkannt wurde. Dafuer ist
        ''' der Knopf da: bei mehreren Ausgaben laesst sich so eine andere waehlen.</summary>
        Private Sub IdentifyAudioCdAgain()
            If Not CanIdentifyAudioCd Then Return
            _identifiedDiscId = String.Empty
            IdentifyDisc(_audioCdToc)
        End Sub

        ''' <summary>Merkt sich das Inhaltsverzeichnis und meldet, dass sich der Knopf geaendert
        ''' haben koennte.</summary>
        Private Sub SetAudioCdToc(toc As AudioCdService.DiscToc)
            _audioCdToc = toc
            RaisePropertyChanged(NameOf(CanIdentifyAudioCd))
        End Sub

        ''' <summary>Nimmt das Inhaltsverzeichnis der zuletzt gelesenen CD entgegen und schlaegt sie
        ''' nach, sofern es eine andere ist als beim letzten Mal.</summary>
        Private Sub IdentifyDisc(toc As AudioCdService.DiscToc)
            Dim discId = MusicBrainzDiscService.DiscIdOf(toc)
            If discId.Length = 0 Then
                ' Kein Inhaltsverzeichnis heisst: die CD ist ausgeworfen. Alles Erkannte gehoert
                ' dann weg - die Titel sind ohnehin schon aus der Liste, und beim naechsten
                ' Einlegen soll erneut gefragt werden, auch wenn es dieselbe CD ist.
                _identifiedDiscId = String.Empty
                Return
            End If
            If String.Equals(discId, _identifiedDiscId, StringComparison.Ordinal) Then Return
            _identifiedDiscId = discId

            ' Eine ANDERE CD: alles Erkannte weg, bevor gefragt wird.
            '
            ' Zwei CDs mit gleicher Titelzahl ergeben dieselben Adressen in der Wiedergabeliste
            ' (Laufwerk, Spurnummer, letzte Spur). SetAudioCdTracks haelt die Liste dann fuer
            ' unveraendert und behaelt die alten Titelobjekte - mitsamt den Namen der vorigen CD.
            ' Stuende die neue nicht bei MusicBrainz, blieben die falschen Namen fuer immer stehen.
            ResetAudioCdTags()
            Dim running = IdentifyDiscAsync(discId)
        End Sub

        ''' <summary>Setzt die CD-Titel auf das zurueck, was das Inhaltsverzeichnis allein hergibt:
        ''' eine Nummer und sonst nichts. Dieselben Ersatztexte wie in
        ''' <see cref="AudioCdService"/> - und der Konverter kennt sie als Platzhalter und schreibt
        ''' sie NICHT in die Kennzeichen.</summary>
        Private Sub ResetAudioCdTags()
            If _audioCdTracks.Count = 0 Then Return
            For Each track In _audioCdTracks
                ' NICHT uebersetzt, genau wie in AudioCdService: es ist kein Text fuer den
                ' Nutzer, sondern eine Marke, an der der Konverter erkennt, dass hier nichts
                ' steht, was in die Kennzeichen gehoert.
                track.Title = "Titel " & track.TrackNumber.ToString("00", Globalization.CultureInfo.InvariantCulture)
                track.Artist = String.Empty
                track.Album = "Audio-CD"
                track.AlbumArtist = String.Empty
                track.Year = 0
                track.RemoteCoverUrl = String.Empty
            Next
            RebuildRows()
        End Sub

        Private Async Function IdentifyDiscAsync(discId As String) As Task
            Try
                StatusText = LocalizationService.T("Audio-CD wird erkannt …")
                Dim releases = Await MusicBrainzDiscService.LookupAsync(discId, _shutdown.Token)
                If _shutdown.IsCancellationRequested Then Return

                If releases.Count = 0 Then
                    StatusText = LocalizationService.T("Die CD ist bei MusicBrainz nicht verzeichnet.")
                    Return
                End If

                Dim chosen = 0
                If releases.Count > 1 Then
                    ' Die Disc-Kennung ist ein Fingerabdruck des Inhaltsverzeichnisses, nicht der
                    ' Pressung: gleiche Spurlaengen, gleiche Kennung. Welche Ausgabe es ist, weiss
                    ' nur der, der sie in der Hand haelt.
                    chosen = Await ShowChoiceAsync(
                        LocalizationService.T("Welche Ausgabe ist es?"),
                        LocalizationService.Format("Zu dieser CD passen {0} Ausgaben. Sie haben dieselben Spurlängen und lassen sich daran nicht unterscheiden.", releases.Count),
                        releases.Select(Function(entry) entry.Label),
                        LocalizationService.T("Übernehmen"),
                        LocalizationService.T("Abbrechen"))
                    If chosen < 0 OrElse chosen >= releases.Count Then
                        StatusText = LocalizationService.T("Die Audio-CD bleibt ohne Angaben.")
                        Return
                    End If
                End If

                ApplyDiscRelease(releases(chosen))
            Catch ex As OperationCanceledException
            Catch ex As Exception
                DiagnosticLogService.LogException("AudioCd.Identify", ex)
                StatusText = LocalizationService.T("Die CD konnte nicht nachgeschlagen werden.")
            End Try
        End Function

        ''' <summary>Traegt die gefundenen Angaben in die CD-Titel ein. Zugeordnet wird ueber die
        ''' SPURNUMMER: die Reihenfolge in der Antwort muss nicht die der Wiedergabeliste sein, und
        ''' bei einer CD mit Datenspur zaehlt unsere Liste nicht luckenlos.</summary>
        Private Sub ApplyDiscRelease(release As MusicBrainzDiscService.DiscRelease)
            If release Is Nothing OrElse _audioCdTracks.Count = 0 Then Return
            Dim byNumber = New Dictionary(Of Integer, MusicBrainzDiscService.DiscTrack)()
            For Each track In release.Tracks
                byNumber(track.Number) = track
            Next

            ' Das Titelbild kommt aus derselben Quelle wie die Angaben, in der Groesse, die in den
            ' MP3-Einstellungen steht. Geholt wird es erst, wenn es gebraucht wird - hier steht nur
            ' die Adresse.
            Dim coverUrl = MusicBrainzDiscService.CoverUrlFor(release.Id, AppSettingsService.Current.TagCoverSize)

            Dim applied = 0
            For Each track In _audioCdTracks
                Dim found As MusicBrainzDiscService.DiscTrack = Nothing
                If Not byNumber.TryGetValue(track.TrackNumber, found) Then Continue For
                track.Title = found.Title
                track.Artist = found.Artist
                track.Album = release.Title
                track.AlbumArtist = release.Artist
                If release.Year > 0 Then track.Year = release.Year
                track.RemoteCoverUrl = coverUrl
                applied += 1
            Next

            ' Die Zeilen tragen die Beschriftung, nicht der Titel - ohne Neuaufbau bliebe
            ' "Titel 01" stehen, obwohl im Titel schon der richtige Name steht.
            RebuildRows()
            If _currentTrack IsNot Nothing AndAlso _audioCdTracks.Contains(_currentTrack) Then
                RaiseCurrentTrackChanged()
                ' Laeuft die CD schon, waehrend die Erkennung eintrifft, braucht die Coverspalte
                ' einen Anstoss - sonst bliebe sie bis zum naechsten Titel leer.
                LoadCoverAsync(_currentTrack)
            End If
            StatusText = LocalizationService.Format("Audio-CD erkannt: {0} – {1} ({2} Titel).",
                                                    release.Artist, release.Title, applied)
        End Sub

    End Class

End Namespace
