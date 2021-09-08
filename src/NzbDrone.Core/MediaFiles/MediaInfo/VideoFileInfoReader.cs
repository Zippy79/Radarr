using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using FFMpegCore;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Extensions;

namespace NzbDrone.Core.MediaFiles.MediaInfo
{
    public interface IVideoFileInfoReader
    {
        MediaInfoModel GetMediaInfo(string filename);
        TimeSpan? GetRunTime(string filename);
    }

    public class VideoFileInfoReader : IVideoFileInfoReader
    {
        private readonly IDiskProvider _diskProvider;
        private readonly Logger _logger;

        public const int MINIMUM_MEDIA_INFO_SCHEMA_REVISION = 8;
        public const int CURRENT_MEDIA_INFO_SCHEMA_REVISION = 8;

        public VideoFileInfoReader(IDiskProvider diskProvider, Logger logger)
        {
            _diskProvider = diskProvider;
            _logger = logger;

            // We bundle ffprobe for windows and linux-x64 currently
            // TODO: move binaries into a nuget, provide for all platforms
            if (OsInfo.IsWindows || (OsInfo.Os == Os.Linux && RuntimeInformation.OSArchitecture == Architecture.X64))
            {
                GlobalFFOptions.Configure(options => options.BinaryFolder = AppDomain.CurrentDomain.BaseDirectory);
            }
        }

        public MediaInfoModel GetMediaInfo(string filename)
        {
            if (!_diskProvider.FileExists(filename))
            {
                throw new FileNotFoundException("Media file does not exist: " + filename);
            }

            // TODO: Cache media info by path, mtime and length so we don't need to read files multiple times
            try
            {
                _logger.Debug("Getting media info from {0}", filename);
                var mediaInfo = FFProbe.Analyse(filename);

                var audioRuntime = mediaInfo.PrimaryAudioStream?.Duration;
                var videoRuntime = mediaInfo.PrimaryVideoStream?.Duration;
                var generalRuntime = mediaInfo.Format.Duration;

                var mediaInfoModel = new MediaInfoModel
                {
                    Format = mediaInfo.Format,
                    VideoStreams = mediaInfo.VideoStreams,
                    AudioStreams = mediaInfo.AudioStreams,
                    SubtitleStreams = mediaInfo.SubtitleStreams,

                    VideoFormat = mediaInfo.PrimaryVideoStream?.CodecName,
                    VideoCodecID = mediaInfo.PrimaryVideoStream?.CodecTagString,
                    VideoProfile = mediaInfo.PrimaryVideoStream?.Profile,
                    VideoBitrate = mediaInfo.PrimaryVideoStream?.BitRate ?? 0,
                    VideoBitDepth = mediaInfo.PrimaryVideoStream?.BitsPerRawSample ?? 0,
                    VideoColourPrimaries = mediaInfo.PrimaryVideoStream?.ColorPrimaries,
                    VideoTransferCharacteristics = mediaInfo.PrimaryVideoStream?.ColorTransfer,
                    Height = mediaInfo.PrimaryVideoStream?.Height ?? 0,
                    Width = mediaInfo.PrimaryVideoStream?.Width ?? 0,
                    AudioFormat = mediaInfo.PrimaryAudioStream?.CodecName,
                    AudioCodecID = mediaInfo.PrimaryAudioStream?.CodecTagString,
                    AudioProfile = mediaInfo.PrimaryAudioStream?.Profile,
                    AudioBitrate = mediaInfo.PrimaryAudioStream?.BitRate ?? 0,
                    RunTime = GetBestRuntime(audioRuntime, videoRuntime, generalRuntime),
                    AudioStreamCount = mediaInfo.AudioStreams.Count,
                    AudioChannels = mediaInfo.PrimaryAudioStream?.Channels ?? 0,
                    AudioChannelPositions = mediaInfo.PrimaryAudioStream?.ChannelLayout,
                    VideoFps = mediaInfo.PrimaryVideoStream?.FrameRate ?? 0,
                    AudioLanguages = mediaInfo.AudioStreams?.Select(x => x.Language).Where(l => l.IsNotNullOrWhiteSpace()).ConcatToString("/") ?? string.Empty,
                    Subtitles = mediaInfo.SubtitleStreams?.Select(x => x.Language).Where(l => l.IsNotNullOrWhiteSpace()).ConcatToString("/") ?? string.Empty,
                    ScanType = "Progressive",
                    SchemaRevision = CURRENT_MEDIA_INFO_SCHEMA_REVISION
                };

                return mediaInfoModel;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Unable to parse media info from file: {0}", filename);
            }

            return null;
        }

        public TimeSpan? GetRunTime(string filename)
        {
            var info = GetMediaInfo(filename);

            return info?.RunTime;
        }

        private TimeSpan GetBestRuntime(TimeSpan? audio, TimeSpan? video, TimeSpan general)
        {
            if (!video.HasValue || video.Value.TotalMilliseconds == 0)
            {
                if (!audio.HasValue || audio.Value.TotalMilliseconds == 0)
                {
                    return general;
                }

                return audio.Value;
            }

            return video.Value;
        }
    }
}
