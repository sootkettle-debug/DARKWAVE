using Microsoft.Win32.SafeHandles;
using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;

namespace NEONWAVE;

/// <summary>
/// Reads CD-DA sectors directly through the Windows CD-ROM driver.
/// No MCI analog CD playback, mounted filesystem or drive.IsReady is required.
/// Raw CD-DA is 44.1 kHz, signed 16-bit little-endian stereo PCM.
/// </summary>
internal sealed class CdDigitalAudio : WaveStream
{
    private const uint TocEx = 0x00024054;         // IOCTL_CDROM_READ_TOC_EX
    private const uint RawRead = 0x0002403E;       // IOCTL_CDROM_RAW_READ
    private const int SectorBytes = 2352;
    private const int DiskSectorBytes = 2048;
    private const int SectorsPerSecond = 75;
    private const int ReadSectorsAtOnce = 16;

    private const uint GenericRead = 0x80000000;
    private const uint ShareRead = 1;
    private const uint ShareWrite = 2;
    private const uint OpenExisting = 3;

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode,
        SetLastError = true)]
    private static extern SafeFileHandle OpenDrive(string path, uint access, uint share,
        IntPtr security, uint creation, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle device, uint code,
        byte[] input, int inputLength, byte[] output, int outputLength,
        out int returned, IntPtr overlapped);

    private readonly SafeFileHandle device;
    private readonly List<CdTrackEntry> tracks = new();
    private readonly byte[] cachedSectors = new byte[ReadSectorsAtOnce * SectorBytes];
    private long relativePosition;
    private int cachedBytes;
    private int cacheIndex;
    private int firstSector;
    private int lastSector;

    public readonly record struct CdTrackEntry(int Number, int StartSector, int EndSector, bool IsAudio);

    // CD-TEXT is optional; ordinary Red Book audio discs generally do not
    // contain titles. Missing titles are represented by numbered track names.
    public IReadOnlyDictionary<int, string> TrackTitles { get; private set; } =
        new Dictionary<int, string>();

    public IReadOnlyList<CdTrackEntry> Tracks => tracks;
    public WaveFormat Format { get; } = new WaveFormat(44100, 16, 2);
    public override WaveFormat WaveFormat => Format;
    public int TrackNumber { get; private set; }

    public static bool TryOpen(string driveLetter, out CdDigitalAudio? disc)
    {
        disc = null;
        try
        {
            var candidate = new CdDigitalAudio(driveLetter);
            disc = candidate;
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
        catch (Win32Exception) { return false; }
    }

    public CdDigitalAudio(string driveLetter)
    {
        var letter = driveLetter.TrimEnd('\\');
        device = OpenDrive(@"\\.\" + letter, GenericRead,
            ShareRead | ShareWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
        if (device.IsInvalid)
        {
            int code = Marshal.GetLastWin32Error();
            device.Dispose();
            throw new IOException($"Cannot access CD drive {letter}: Windows error {code}.");
        }
        try
        {
            // Request the TOC in logical-block-address form (MSF flag = 0).
            var toc = new byte[804];
            var command = new byte[4];  // Format=0 (TOC), Msf=0 (LBA)
            if (!DeviceIoControl(device, TocEx, command, command.Length,
                toc, toc.Length, out int returned, IntPtr.Zero) ||
                returned < 20)
                throw new IOException($"Cannot read audio CD table of contents: {Marshal.GetLastWin32Error()}");

            int first = toc[2], last = toc[3];
            if (first < 1 || last < first || last > 99 ||
                returned < 4 + (last - first + 2) * 8)
                throw new IOException("No valid CD audio track listing.");

            var entries = new List<(int number, int sector, bool audio)>();
            for (int i = 0; i <= last - first + 1; i++)
            {
                int o = 4 + i * 8;
                int trackNumber = toc[o + 2];
                int sector = (toc[o + 4] << 24) | (toc[o + 5] << 16) |
                             (toc[o + 6] << 8) | toc[o + 7];
                bool isAudio = (toc[o + 1] & 0x04) == 0;
                entries.Add((trackNumber, sector, isAudio));
            }

            for (int i = 0; i < entries.Count - 1; i++)
            {
                var a = entries[i];
                var b = entries[i + 1];
                if (a.number >= 1 && a.number <= 99 && b.sector > a.sector)
                    tracks.Add(new CdTrackEntry(a.number, a.sector, b.sector, a.audio));
            }
            if (!tracks.Exists(t => t.IsAudio))
                throw new IOException("This disc has no digital audio tracks.");

            SelectTrack(tracks.Find(t => t.IsAudio).Number);
            TrackTitles = ReadCdTextTitles();
        }
        catch
        {
            device.Dispose();
            throw;
        }
    }

    private IReadOnlyDictionary<int, string> ReadCdTextTitles()
    {
        // CDROM_READ_TOC_EX_FORMAT_CDTEXT (5), 4-byte request.
        // Unsupported discs/drives simply return no metadata.
        var query = new byte[] { 5, 0, 0, 0 };
        var buffer = new byte[32768];
        if (!DeviceIoControl(device, TocEx, query, query.Length,
            buffer, buffer.Length, out int returned, IntPtr.Zero) ||
            returned < 22) return new Dictionary<int, string>();

        int advertised = 2 + (buffer[0] << 8) + buffer[1];
        int usable = Math.Min(returned, advertised);
        var titles = new Dictionary<int, System.Text.StringBuilder>();
        int currentTrack = -1;
        bool ended = false;

        // Each descriptor has a 4-byte header, a 12-byte text payload,
        // and a 2-byte checksum. Keep the first CD-TEXT language block.
        for (int pos = 4; pos + 18 <= usable; pos += 18)
        {
            if (buffer[pos] != 0x80 || ((buffer[pos + 3] >> 4) & 7) != 0)
                continue; // track/album title packets in block 0 only
            int track = buffer[pos + 1] & 0x7f;
            bool unicode = (buffer[pos + 3] & 0x80) != 0;
            int dataStart = pos + 4;
            int unit = unicode ? 2 : 1;
            if (track != currentTrack)
            {
                currentTrack = track;
                ended = false;
            }
            for (int p = dataStart; p + unit <= dataStart + 12; p += unit)
            {
                int ch = unicode ? (buffer[p] << 8) | buffer[p + 1] : buffer[p];
                if (ch == 0)
                {
                    // A packet can contain more than one null-terminated title.
                    // The following title belongs to the next track.
                    ended = true;
                    currentTrack++;
                    continue;
                }
                if (ended) ended = false;
                if (currentTrack < 1 || currentTrack > 99) continue;
                if (char.IsControl((char)ch)) continue;
                if (!titles.TryGetValue(currentTrack, out var title))
                {
                    title = new System.Text.StringBuilder();
                    titles[currentTrack] = title;
                }
                title.Append((char)ch);
            }
        }
        return titles.Where(p => p.Value.Length > 0)
            .ToDictionary(p => p.Key, p => p.Value.ToString().Trim());
    }

    public bool IsDriveAvailable()
    {
        if (device.IsClosed || device.IsInvalid) return false;
        var toc = new byte[804];
        return DeviceIoControl(device, TocEx, new byte[4], 4,
            toc, toc.Length, out int returned, IntPtr.Zero) &&
            returned >= 20;
    }

    public bool SelectTrack(int number)
    {
        foreach (var track in tracks)
        {
            if (!track.IsAudio || track.Number != number) continue;
            TrackNumber = number;
            firstSector = track.StartSector;
            lastSector = track.EndSector;
            relativePosition = 0;
            cachedBytes = cacheIndex = 0;
            return true;
        }
        return false;
    }

    public override long Length => (long)(lastSector - firstSector) * SectorBytes;
    public override long Position
    {
        get => relativePosition;
        set
        {
            relativePosition = Math.Clamp(value, 0L, Length);
            // Whole sector reads; cache is invalidated after any seek.
            cachedBytes = cacheIndex = 0;
        }
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (device.IsClosed || device.IsInvalid) return 0;
        int written = 0;
        while (count > 0 && relativePosition < Length)
        {
            if (cacheIndex >= cachedBytes)
            {
                long sector = firstSector + relativePosition / SectorBytes;
                int sectorOffset = (int)(relativePosition % SectorBytes);
                int sectors = (int)Math.Min(ReadSectorsAtOnce, lastSector - sector);
                var request = new byte[16]; // RAW_READ_INFO: LONGLONG, ULONG, ULONG (CDDA=2)
                BitConverter.GetBytes(sector * DiskSectorBytes).CopyTo(request, 0);
                BitConverter.GetBytes(sectors).CopyTo(request, 8);
                BitConverter.GetBytes(2).CopyTo(request, 12);
                if (!DeviceIoControl(device, RawRead, request, request.Length,
                    cachedSectors, sectors * SectorBytes, out int bytes, IntPtr.Zero) ||
                    bytes < SectorBytes)
                    throw new IOException($"CD audio read failed: Windows error {Marshal.GetLastWin32Error()}");
                cachedBytes = bytes;
                cacheIndex = sectorOffset;
            }
            int available = Math.Min(cacheIndex < cachedBytes ? cachedBytes - cacheIndex : 0,
                (int)Math.Min(count, Length - relativePosition));
            if (available <= 0) break;
            Buffer.BlockCopy(cachedSectors, cacheIndex, buffer, offset + written, available);
            cacheIndex += available;
            relativePosition += available;
            written += available;
            count -= available;
        }
        return written;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) device.Dispose();
        base.Dispose(disposing);
    }
}
