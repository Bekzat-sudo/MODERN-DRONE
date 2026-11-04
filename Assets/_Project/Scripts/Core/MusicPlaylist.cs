using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace ModernDrone.Core
{
    /// <summary>One song of the player's list.</summary>
    [Serializable]
    public sealed class MusicTrack
    {
        public string id;
        public string title;
        public float duration;

        public string DurationText
        {
            get
            {
                if (duration <= 0f) return "";
                int s = Mathf.RoundToInt(duration);
                return s >= 3600 ? $"{s / 3600}:{s / 60 % 60:00}:{s % 60:00}" : $"{s / 60}:{s % 60:00}";
            }
        }
    }

    /// <summary>Validation of what the player types: only these strings ever reach the yt-dlp command line.</summary>
    public static class YouTubeLinks
    {
        static readonly HashSet<string> Hosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "youtube.com", "www.youtube.com", "m.youtube.com", "music.youtube.com", "youtu.be", "www.youtu.be" };
        static readonly Regex SafeUrl = new Regex(@"^[A-Za-z0-9\-._~:/?#\[\]@!$&()*+,;=%]+$", RegexOptions.CultureInvariant);
        static readonly Regex VideoId = new Regex(@"^[A-Za-z0-9_-]{11}$", RegexOptions.CultureInvariant);

        /// <summary>Accepts an http(s) YouTube link (the scheme may be omitted); returns it as https.</summary>
        public static bool TryNormalize(string text, out string url)
        {
            url = null;
            if (string.IsNullOrWhiteSpace(text)) return false;
            text = text.Trim();
            if (text.IndexOf("://", StringComparison.Ordinal) < 0) text = "https://" + text;
            if (!SafeUrl.IsMatch(text) || !Uri.TryCreate(text, UriKind.Absolute, out var uri)) return false;
            if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp) return false;
            if (!string.IsNullOrEmpty(uri.UserInfo) || !uri.IsDefaultPort || !Hosts.Contains(uri.Host)) return false;
            url = "https://" + uri.Host + uri.PathAndQuery;
            return true;
        }

        public static bool IsVideoId(string id) => id != null && VideoId.IsMatch(id);

        public static string WatchUrl(string id) => "https://www.youtube.com/watch?v=" + id;

        /// <summary>YouTube's automatic mix built from a song: its list id is "RD" plus the video id.</summary>
        public static string MixUrl(string id) => $"{WatchUrl(id)}&list=RD{id}";
    }

    /// <summary>Command lines for yt-dlp and parsing of its playlist dump (https://github.com/yt-dlp/yt-dlp#readme).</summary>
    public static class YtDlpPlaylist
    {
        public const int MaxTracks = 500;

        [Serializable]
        sealed class Entry
        {
            public string id;
            public string title;
            public float duration;
        }

        [Serializable]
        sealed class Dump
        {
            public string id;
            public string title;
            public float duration;
            public Entry[] entries;
        }

        /// <summary>The whole list as one JSON document, without resolving each video (takes seconds, not minutes).</summary>
        public static string ListArguments(string url) =>
            $"-J --flat-playlist --yes-playlist --ignore-errors --no-warnings --encoding utf-8 --playlist-end {MaxTracks} -- \"{url}\"";

        /// <summary>One song as MP3 (Unity cannot decode the Opus/M4A that YouTube serves); the conversion needs ffmpeg.</summary>
        public static string DownloadArguments(string id, string directory) =>
            $"-f bestaudio/best -x --audio-format mp3 --audio-quality 4 --no-playlist --no-progress --no-warnings --encoding utf-8 -P \"{directory}\" -o \"{id}.%(ext)s\" -- \"{YouTubeLinks.WatchUrl(id)}\"";

        /// <summary>Tracks of a <c>yt-dlp -J</c> dump. Deleted and private videos are left out; a plain video link gives one track.</summary>
        public static List<MusicTrack> Parse(string json, out string playlistTitle)
        {
            var tracks = new List<MusicTrack>();
            playlistTitle = "";
            if (string.IsNullOrWhiteSpace(json)) return tracks;
            Dump dump;
            try
            {
                dump = JsonUtility.FromJson<Dump>(json);
            }
            catch (ArgumentException)
            {
                return tracks;
            }
            if (dump == null) return tracks;
            playlistTitle = CleanText(dump.title);
            if (dump.entries == null || dump.entries.Length == 0)
            {
                if (YouTubeLinks.IsVideoId(dump.id)) tracks.Add(new MusicTrack { id = dump.id, title = CleanText(dump.title), duration = dump.duration });
                return tracks;
            }
            foreach (var e in dump.entries)
            {
                if (e == null || !YouTubeLinks.IsVideoId(e.id) || e.title == "[Deleted video]" || e.title == "[Private video]") continue;
                tracks.Add(new MusicTrack { id = e.id, title = CleanText(e.title), duration = e.duration });
                if (tracks.Count >= MaxTracks) break;
            }
            return tracks;
        }

        /// <summary>Titles are drawn by IMGUI with rich text on: tags and control characters must not take effect.</summary>
        public static string CleanText(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var sb = new StringBuilder(text.Length);
            foreach (char c in text)
            {
                if (char.IsControl(c)) sb.Append(' ');
                else sb.Append(c == '<' ? '‹' : c == '>' ? '›' : c);
            }
            return sb.ToString().Trim();
        }
    }

    /// <summary>Play order of a list: wraps around at both ends; shuffling keeps the current song playing.</summary>
    public sealed class MusicQueue
    {
        readonly System.Random _random;
        int[] _order = new int[0];
        int _position;
        bool _shuffle;

        public MusicQueue(int seed = 0) => _random = new System.Random(seed);

        public int Count => _order.Length;

        /// <summary>Index in the list of the current song, -1 when the list is empty.</summary>
        public int Current => Count == 0 ? -1 : _order[_position];

        public bool Shuffle
        {
            get => _shuffle;
            set
            {
                if (_shuffle == value) return;
                _shuffle = value;
                Rebuild(Current);
            }
        }

        public void Reset(int count)
        {
            _order = new int[Mathf.Max(0, count)];
            Rebuild(0);
        }

        /// <summary>Makes a song current. In shuffle mode the rest is dealt anew, so it starts a fresh round.</summary>
        public void Jump(int track)
        {
            if (track < 0 || track >= Count) return;
            Rebuild(track);
        }

        public int Advance(int direction)
        {
            if (Count == 0) return -1;
            _position = Wrap(_position + direction);
            return Current;
        }

        public int Peek(int direction) => Count == 0 ? -1 : _order[Wrap(_position + direction)];

        int Wrap(int position) => (position % Count + Count) % Count;

        void Rebuild(int current)
        {
            for (int i = 0; i < _order.Length; i++) _order[i] = i;
            _position = Mathf.Max(0, current);
            if (!_shuffle || Count == 0) return;
            (_order[0], _order[_position]) = (_order[_position], _order[0]);
            _position = 0;
            for (int i = _order.Length - 1; i > 1; i--)
            {
                int j = _random.Next(1, i + 1);
                (_order[i], _order[j]) = (_order[j], _order[i]);
            }
        }
    }
}
