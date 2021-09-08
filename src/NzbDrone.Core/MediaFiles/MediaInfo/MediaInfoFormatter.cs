using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using NLog;
using NLog.Fluent;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Instrumentation;
using NzbDrone.Common.Instrumentation.Extensions;
using NzbDrone.Common.Serializer;

namespace NzbDrone.Core.MediaFiles.MediaInfo
{
    public static class MediaInfoFormatter
    {
        private const string ValidHdrColourPrimaries = "bt2020";
        private const string VideoDynamicRangeHdr = "HDR";
        private static readonly string[] ValidHdrTransferFunctions = { "PQ", "HLG", "smpte2084" };
        private static readonly string[] DolbyVisionCodecIds = { "dvhe", "dvh1" };

        private static readonly Regex PositionRegex = new Regex(@"(?<position>^\d\.\d)", RegexOptions.Compiled);

        private static readonly Logger Logger = NzbDroneLogger.GetLogger(typeof(MediaInfoFormatter));

        public static decimal FormatAudioChannels(MediaInfoModel mediaInfo)
        {
            var audioChannels = FormatAudioChannelsFromAudioChannelPositions(mediaInfo);

            if (audioChannels == null || audioChannels == 0.0m)
            {
                audioChannels = mediaInfo.AudioChannels;
            }

            return audioChannels ?? 0;
        }

        public static string FormatAudioCodec(MediaInfoModel mediaInfo, string sceneName)
        {
            if (mediaInfo.AudioFormat == null)
            {
                return null;
            }

            var audioFormat = mediaInfo.AudioFormat;
            var audioCodecID = mediaInfo.AudioCodecID ?? string.Empty;
            var audioProfile = mediaInfo.AudioProfile ?? string.Empty;

            if (audioFormat.Empty())
            {
                return string.Empty;
            }

            // see definitions here https://github.com/FFmpeg/FFmpeg/blob/master/libavcodec/codec_desc.c
            if (audioCodecID == "thd+")
            {
                return "TrueHD Atmos";
            }

            if (audioFormat == "truehd")
            {
                return "TrueHD";
            }

            if (audioFormat == "flac")
            {
                return "FLAC";
            }

            if (audioFormat == "dts")
            {
                if (audioProfile == "DTS:X")
                {
                    return "DTS-X";
                }

                if (audioProfile == "DTS-HD MA")
                {
                    return "DTS-HD MA";
                }

                if (audioProfile == "DTS-ES")
                {
                    return "DTS-ES";
                }

                if (audioProfile == "DTS-HD HRA")
                {
                    return "DTS-HD HRA";
                }

                if (audioProfile == "DTS Express")
                {
                    return "DTS Express";
                }

                if (audioProfile == "DTS 96/24")
                {
                    return "DTS 96/24";
                }

                return "DTS";
            }

            if (audioCodecID == "ec+3")
            {
                return "EAC3 Atmos";
            }

            if (audioFormat == "eac3")
            {
                return "EAC3";
            }

            if (audioFormat == "ac3")
            {
                return "AC3";
            }

            if (audioFormat == "aac")
            {
                if (audioCodecID == "A_AAC/MPEG4/LC/SBR")
                {
                    return "HE-AAC";
                }

                return "AAC";
            }

            if (audioFormat == "mp3")
            {
                return "MP3";
            }

            if (audioFormat == "mp2")
            {
                return "MP2";
            }

            if (audioFormat == "opus")
            {
                return "Opus";
            }

            if (audioFormat.StartsWith("pcm_"))
            {
                return "PCM";
            }

            if (audioFormat == "vorbis")
            {
                return "Vorbis";
            }

            if (audioFormat == "wmav1" ||
                audioFormat == "wmav2")
            {
                return "WMA";
            }

            Logger.Debug()
                  .Message("Unknown audio format: '{0}' in '{1}'.", mediaInfo.RawData, sceneName)
                  .WriteSentryWarn("UnknownAudioFormatFFProbe", mediaInfo.Analysis.Format?.FormatLongName, mediaInfo.AudioFormat, audioCodecID)
                  .Write();

            return mediaInfo.AudioFormat;
        }

        public static string FormatVideoCodec(MediaInfoModel mediaInfo, string sceneName)
        {
            if (mediaInfo.VideoFormat == null)
            {
                return null;
            }

            var videoFormat = mediaInfo.VideoFormat;
            var videoCodecID = mediaInfo.VideoCodecID ?? string.Empty;

            var result = videoFormat.Trim();

            if (videoFormat.Empty())
            {
                return result;
            }

            // see definitions here: https://github.com/FFmpeg/FFmpeg/blob/master/libavcodec/codec_desc.c
            if (videoCodecID == "x264")
            {
                return "x264";
            }

            if (videoFormat == "h264")
            {
                return GetSceneNameMatch(sceneName, "AVC", "x264", "h264");
            }

            if (videoCodecID == "x265")
            {
                return "x265";
            }

            if (videoFormat == "hevc")
            {
                return GetSceneNameMatch(sceneName, "HEVC", "x265", "h265");
            }

            if (videoFormat == "mpeg2video")
            {
                return "MPEG2";
            }

            if (videoFormat == "mpeg4")
            {
                if (videoCodecID == "XVID")
                {
                    return "XviD";
                }

                if (videoCodecID == "DIV3" ||
                    videoCodecID == "DIVX" ||
                    videoCodecID == "DX50")
                {
                    return "DivX";
                }

                return "";
            }

            if (videoFormat == "vc1")
            {
                return "VC1";
            }

            if (videoFormat == "av1")
            {
                return "AV1";
            }

            if (videoFormat == "vp6" ||
                videoFormat == "vp7" ||
                videoFormat == "vp8" ||
                videoFormat == "vp9")
            {
                return videoFormat.ToUpperInvariant();
            }

            if (videoFormat == "WMV1" ||
                videoFormat == "WMV2")
            {
                return "WMV";
            }

            if (videoFormat == "qtrle" ||
                videoFormat == "rpza" ||
                videoFormat == "rv10" ||
                videoFormat == "rv20" ||
                videoFormat == "rv30" ||
                videoFormat == "rv40")
            {
                return "";
            }

            Logger.Debug()
                  .Message("Unknown video format: '{0}' in '{1}'.", mediaInfo.RawData, sceneName)
                  .WriteSentryWarn("UnknownVideoFormatFFProbe", mediaInfo.Analysis.Format?.FormatLongName, videoFormat, videoCodecID)
                  .Write();

            return result;
        }

        private static decimal? FormatAudioChannelsFromAudioChannelPositions(MediaInfoModel mediaInfo)
        {
            if (mediaInfo.AudioChannelPositions == null)
            {
                return 0;
            }

            var match = PositionRegex.Match(mediaInfo.AudioChannelPositions);
            if (match.Success)
            {
                return decimal.Parse(match.Groups["position"].Value, NumberStyles.Number, CultureInfo.InvariantCulture);
            }

            return 0;
        }

        private static decimal? FormatAudioChannelsFromAudioChannels(MediaInfoModel mediaInfo)
        {
            var audioChannelsStream = mediaInfo.AudioChannels;

            // FLAC 6 channels is likely 5.1
            if (mediaInfo.AudioFormat == "flac" && audioChannelsStream == 6)
            {
                return 5.1m;
            }

            return audioChannelsStream;
        }

        private static string GetSceneNameMatch(string sceneName, params string[] tokens)
        {
            sceneName = sceneName.IsNotNullOrWhiteSpace() ? Parser.Parser.RemoveFileExtension(sceneName) : string.Empty;

            foreach (var token in tokens)
            {
                if (sceneName.ContainsIgnoreCase(token))
                {
                    return token;
                }
            }

            // Last token is the default.
            return tokens.Last();
        }

        public static string FormatVideoDynamicRange(MediaInfoModel mediaInfo)
        {
            if (DolbyVisionCodecIds.ContainsIgnoreCase(mediaInfo.VideoCodecID))
            {
                // Dolby vision
                return VideoDynamicRangeHdr;
            }

            if (mediaInfo.VideoBitDepth >= 10 &&
                mediaInfo.VideoColourPrimaries.IsNotNullOrWhiteSpace() &&
                mediaInfo.VideoTransferCharacteristics.IsNotNullOrWhiteSpace())
            {
                // Other HDR
                if (mediaInfo.VideoColourPrimaries.EqualsIgnoreCase(ValidHdrColourPrimaries) &&
                    ValidHdrTransferFunctions.Any(mediaInfo.VideoTransferCharacteristics.Contains))
                {
                    return VideoDynamicRangeHdr;
                }
            }

            return "";
        }
    }
}
