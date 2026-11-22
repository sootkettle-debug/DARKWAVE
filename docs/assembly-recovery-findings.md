# Verified DARKWAVE+ / Compact Dark assembly recovery

The two uploaded Windows-x64 ZIPs are .NET 10 single-file applications. Each bundle contains 449 entries. Both primary .NET IL assemblies were **successfully recovered** from the bundles.

| Build | Main managed assembly | Size | SHA-256 |
|---|---|---:|---|
| DARKWAVE+ | `DARKWAVE+.dll` | 428,544 bytes | `72759d01561a474432173c17b505e3d532821ba1c6a9f722cabc6c4eef6b98a1` |
| Compact Dark | `Compact Dark.dll` | 429,568 bytes | `ce707682da3c56a7795ab7538b1ebbf6cb75c96f699cdde567fc588bc50259f4` |

The libraries contain compiled intermediate language, **not yet editable C# source**. The extraction is reproducible from the two original uploaded Windows executable bundles. The recovered main DLLs have been packaged separately for further decompilation.

## Shared identifiable features
Both managed assemblies contain `FiveBandEqualizer`, `SetVolume`, `NextTrack`, `TryAdvanceEndedTrack`, `AddFolder_Click`, `LoadPlaylist_Click`, `SavePlaylist_Click`, `MusicFolder`, `MusicPlaylist`, `FolderList`, `PlaylistList` and `VolumeKnob` identifiers.

These establish evidence of overlapping internal implementations but do **not** by themselves establish behavioral equivalence or functional parity.

## Differences needing preservation
- Compact Dark has `EjectCd_Click`, `GetDrives`, `mciSendString`, and `Eject CD from optical drive` identifiers absent from DARKWAVE+ printable identifiers. This Compact Dark-only functionality must also be preserved.
- DARKWAVE+ has the label `CLOSE LIBRARY`; Compact Dark has `CLOSE`.
- Binary code is different: simply swapping assemblies is not a validated approach.

## To complete the merge
1. Decompile both assemblies into editable C# and WPF resource/XAML representations (e.g. ILSpy), create a compilable `net10.0-windows` solution.
2. Diff implementation and build a complete feature matrix from recovered methods/resources.
3. Integrate all DARKWAVE+ and Compact Dark-exclusive functions into the approved new orange/graphite visual design.
4. Build and perform Windows x64 functional tests (audio, playback, seeking, EQ, volume, library, playlists, CD drive if supported).

**Current milestone:** recovered managed assemblies and initial feature comparison. No functional code merger, Windows build, or runtime verification has yet been performed. The `main` branch remains untouched.
