using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using Debug = UnityEngine.Debug;

namespace ModernDrone.Core
{
    /// <summary>
    /// The player's own music, kept across scenes. A YouTube link becomes a song list through yt-dlp (installed by the
    /// player, not shipped with the game); songs are converted to MP3 on first play and cached in persistentDataPath.
    /// Plays flat 2D and keeps playing while the game is paused. Saved as JSON at persistentDataPath/music.json.
    /// </summary>
    public sealed class MusicPlayer : MonoBehaviour
    {
        [Serializable]
        sealed class Saved
        {
            public string url = "";
            public string playlistTitle = "";
            public string ytDlpPath = "";
            public float volume = 0.5f;
            public bool shuffle;
            public List<MusicTrack> tracks = new List<MusicTrack>();
        }

        sealed class ProcessResult
        {
            public bool Started, TimedOut;
            public int ExitCode;
            public string Output, Errors;
        }

        enum State { Idle, Fetching, Decoding, Playing, Paused }

        const string InstallHint = "yt-dlp не найден. Установите: winget install yt-dlp.yt-dlp";
        const int ListTimeoutMs = 120000;
        const int DownloadTimeoutMs = 600000;
        /// <summary>Several songs failing in a row means a broken setup, not bad songs: stop before downloading the whole list.</summary>
        const int MaxFailuresInARow = 3;

        static MusicPlayer _instance;

        /// <summary>Created on first use and kept across scene loads.</summary>
        public static MusicPlayer Instance
        {
            get
            {
                if (_instance != null) return _instance;
                var go = new GameObject("MusicPlayer");
                DontDestroyOnLoad(go);
                return _instance = go.AddComponent<MusicPlayer>();
            }
        }

        readonly CancellationTokenSource _cancel = new CancellationTokenSource();
        readonly Dictionary<string, Task<ProcessResult>> _downloads = new Dictionary<string, Task<ProcessResult>>();
        readonly MusicQueue _queue = new MusicQueue(Environment.TickCount);
        AudioSource _source;
        Saved _saved = new Saved();
        string _savePath, _cacheDirectory, _workDirectory;
        State _state;
        Task<ProcessResult> _listing;
        string _listingUrl, _fetching;
        UnityWebRequest _decoding;
        int _failures;

        public IReadOnlyList<MusicTrack> Tracks => _saved.tracks;
        public string PlaylistTitle => _saved.playlistTitle;
        /// <summary>Changes whenever the song list is replaced, so menus know to rebuild.</summary>
        public int TracksVersion { get; private set; }
        public string Status { get; private set; } = "";
        public bool IsListing => _listing != null;
        public bool IsPlaying => _state == State.Playing;
        public bool IsActive => _state != State.Idle;
        public int CurrentIndex => _state == State.Idle ? -1 : _queue.Current;
        public MusicTrack CurrentTrack => CurrentIndex >= 0 && CurrentIndex < Tracks.Count ? Tracks[CurrentIndex] : null;

        public string Url { get => _saved.url; set => _saved.url = value; }
        public string YtDlpPath { get => _saved.ytDlpPath; set => _saved.ytDlpPath = value; }

        public float Volume
        {
            get => _saved.volume;
            set
            {
                _saved.volume = Mathf.Clamp01(value);
                _source.volume = _saved.volume;
            }
        }

        public bool Shuffle
        {
            get => _saved.shuffle;
            set
            {
                _saved.shuffle = value;
                _queue.Shuffle = value;
            }
        }

        void Awake()
        {
            _savePath = Path.Combine(Application.persistentDataPath, "music.json");
            _cacheDirectory = Path.Combine(Application.persistentDataPath, "MusicCache");
            _workDirectory = Path.Combine(_cacheDirectory, "work");
            _source = gameObject.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.spatialBlend = 0f;
            _source.ignoreListenerPause = true;
            _source.bypassEffects = _source.bypassListenerEffects = _source.bypassReverbZones = true;
            Load();
            _source.volume = _saved.volume;
            _queue.Shuffle = _saved.shuffle;
            _queue.Reset(_saved.tracks.Count);
        }

        void OnDestroy()
        {
            _cancel.Cancel();
            _decoding?.Dispose();
            Save();
        }

        // ---- list

        /// <summary>Starts turning a link into a song list; <see cref="Status"/> reports the outcome.</summary>
        public void LoadPlaylist(string text)
        {
            if (IsListing) return;
            if (!YouTubeLinks.TryNormalize(text, out var url))
            {
                Status = "Это не ссылка на YouTube";
                return;
            }
            _saved.url = url;
            _listingUrl = url;
            Status = "Читаю плейлист…";
            string exe = ResolveExecutable(), args = YtDlpPlaylist.ListArguments(url);
            var token = _cancel.Token;
            _listing = Task.Run(() => RunProcess(exe, args, ListTimeoutMs, token));
        }

        void FinishListing()
        {
            var task = _listing;
            _listing = null;
            var result = task.Result;
            if (!result.Started)
            {
                Status = InstallHint;
                return;
            }
            var tracks = YtDlpPlaylist.Parse(result.Output, out var title);
            if (tracks.Count == 0)
            {
                Status = result.TimedOut ? "YouTube не ответил вовремя" : "Песни не найдены. " + LastError(result);
                return;
            }
            // The song playing right now stays on if the new list has it (a mix starts with the song it was made from).
            string currentId = CurrentTrack?.id;
            int kept = currentId == null ? -1 : tracks.FindIndex(t => t.id == currentId);
            if (kept < 0) Stop();
            _saved.tracks = tracks;
            _saved.playlistTitle = title;
            _saved.url = _listingUrl;
            _queue.Reset(tracks.Count);
            if (kept >= 0) _queue.Jump(kept);
            TracksVersion++;
            Save();
            Status = $"Загружен список: {tracks.Count}";
        }

        /// <summary>Makes a YouTube mix from the current song, or from the first one when nothing is playing.</summary>
        public void LoadMix()
        {
            var seed = CurrentTrack ?? (Tracks.Count > 0 ? Tracks[0] : null);
            if (seed == null)
            {
                Status = "Сначала загрузите ссылку: микс строится от песни";
                return;
            }
            LoadPlaylist(YouTubeLinks.MixUrl(seed.id));
        }

        // ---- transport

        public void Play(int index)
        {
            if (index < 0 || index >= Tracks.Count) return;
            _failures = 0;
            _queue.Jump(index);
            StartCurrent();
        }

        public void TogglePause()
        {
            switch (_state)
            {
                case State.Idle:
                    if (Tracks.Count > 0)
                    {
                        _failures = 0;
                        StartCurrent();
                    }
                    break;
                case State.Playing:
                    _source.Pause();
                    _state = State.Paused;
                    break;
                case State.Paused:
                    _source.UnPause();
                    _state = State.Playing;
                    break;
            }
        }

        public void Step(int direction)
        {
            if (Tracks.Count == 0) return;
            _failures = 0;
            _queue.Advance(direction);
            StartCurrent();
        }

        public void Stop()
        {
            ReleaseClip();
            _fetching = null;
            _state = State.Idle;
            Status = "";
        }

        void StartCurrent()
        {
            ReleaseClip();
            var track = Tracks[_queue.Current];
            _fetching = track.id;
            _state = State.Fetching;
            Status = "Загрузка: " + track.title;
            EnsureDownload(track.id);
        }

        void ReleaseClip()
        {
            _decoding?.Dispose();
            _decoding = null;
            var clip = _source.clip;
            _source.Stop();
            _source.clip = null;
            if (clip != null) Destroy(clip);
        }

        void Update()
        {
            if (_listing != null && _listing.IsCompleted) FinishListing();
            switch (_state)
            {
                case State.Fetching: PollDownload(); break;
                case State.Decoding: PollDecode(); break;
                case State.Playing:
                    // Lost focus without runInBackground also stops the source: that must not count as the song's end.
                    if (!_source.isPlaying && (Application.isFocused || Application.runInBackground))
                    {
                        _queue.Advance(1);
                        StartCurrent();
                    }
                    break;
            }
        }

        // ---- cache

        string CachePath(string id) => Path.Combine(_cacheDirectory, id + ".mp3");

        /// <summary>Starts the download unless the song is cached or already on its way.</summary>
        void EnsureDownload(string id)
        {
            if (!YouTubeLinks.IsVideoId(id) || File.Exists(CachePath(id))) return;
            if (_downloads.TryGetValue(id, out var running) && !running.IsCompleted) return;
            string exe = ResolveExecutable(), work = _workDirectory, target = CachePath(id);
            var token = _cancel.Token;
            _downloads[id] = Task.Run(() => Download(exe, id, work, target, token));
        }

        /// <summary>Works in a side directory: an interrupted conversion must never look like a cached song.</summary>
        static ProcessResult Download(string exe, string id, string work, string target, CancellationToken token)
        {
            Directory.CreateDirectory(work);
            var result = RunProcess(exe, YtDlpPlaylist.DownloadArguments(id, work), DownloadTimeoutMs, token);
            string produced = Path.Combine(work, id + ".mp3");
            if (result.Started && result.ExitCode == 0 && File.Exists(produced))
            {
                if (File.Exists(target)) File.Delete(target);
                File.Move(produced, target);
            }
            else
            {
                foreach (var leftover in Directory.GetFiles(work, id + ".*")) File.Delete(leftover);
            }
            return result;
        }

        void PollDownload()
        {
            string id = _fetching;
            if (File.Exists(CachePath(id)))
            {
                StartDecode(id);
                return;
            }
            if (!_downloads.TryGetValue(id, out var task))
            {
                TrackFailed("нет файла");
                return;
            }
            if (!task.IsCompleted) return;
            _downloads.Remove(id);
            if (task.IsFaulted)
            {
                TrackFailed(task.Exception?.GetBaseException().Message);
                return;
            }
            if (!task.Result.Started)
            {
                Stop();
                Status = InstallHint;
                return;
            }
            TrackFailed(LastError(task.Result));
        }

        void StartDecode(string id)
        {
            _decoding = UnityWebRequestMultimedia.GetAudioClip(new Uri(CachePath(id)).AbsoluteUri, AudioType.MPEG);
            ((DownloadHandlerAudioClip)_decoding.downloadHandler).compressed = true;
            _decoding.SendWebRequest();
            _state = State.Decoding;
        }

        void PollDecode()
        {
            if (!_decoding.isDone) return;
            var request = _decoding;
            _decoding = null;
            AudioClip clip = request.result == UnityWebRequest.Result.Success ? DownloadHandlerAudioClip.GetContent(request) : null;
            string error = request.error;
            request.Dispose();
            if (clip == null || clip.length <= 0f)
            {
                TrackFailed(error ?? "файл не читается");
                return;
            }
            _source.clip = clip;
            _source.Play();
            _state = State.Playing;
            _failures = 0;
            Status = "";
            if (Tracks.Count > 1) EnsureDownload(Tracks[_queue.Peek(1)].id);
        }

        /// <summary>A song that cannot be fetched (region lock, age gate...) is skipped, unless they keep failing.</summary>
        void TrackFailed(string reason)
        {
            Debug.LogWarning($"[ModernDrone] Music: {_fetching}: {reason}");
            if (++_failures >= Mathf.Min(MaxFailuresInARow, Tracks.Count))
            {
                Stop();
                Status = "Не удалось загрузить: " + reason;
                return;
            }
            _queue.Advance(1);
            StartCurrent();
        }

        public long CacheBytes()
        {
            long total = 0;
            try
            {
                if (Directory.Exists(_cacheDirectory))
                    foreach (var f in Directory.GetFiles(_cacheDirectory, "*", SearchOption.AllDirectories)) total += new FileInfo(f).Length;
            }
            catch (IOException) { }
            return total;
        }

        public void ClearCache()
        {
            foreach (var pair in _downloads)
                if (!pair.Value.IsCompleted)
                {
                    Status = "Подождите конца загрузки";
                    return;
                }
            _downloads.Clear();
            try
            {
                if (Directory.Exists(_cacheDirectory))
                    foreach (var f in Directory.GetFiles(_cacheDirectory, "*.mp3")) File.Delete(f);
                if (Directory.Exists(_workDirectory)) Directory.Delete(_workDirectory, true);
                Status = "Кэш очищен";
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                Status = "Не удалось очистить кэш: " + e.Message;
            }
        }

        // ---- yt-dlp

        /// <summary>The player's path, else the first yt-dlp on PATH.</summary>
        string ResolveExecutable()
        {
            if (!string.IsNullOrWhiteSpace(_saved.ytDlpPath)) return _saved.ytDlpPath.Trim();
            foreach (string directory in CurrentPath().Split(Path.PathSeparator))
            {
                if (directory.Length == 0 || directory.IndexOfAny(Path.GetInvalidPathChars()) >= 0) continue;
                string candidate = Path.Combine(directory.Trim(), "yt-dlp.exe");
                if (File.Exists(candidate)) return candidate;
            }
            return "yt-dlp";
        }

        /// <summary>A running game keeps the PATH of its start; winget adds to the saved one, so read that as well.</summary>
        static string CurrentPath() => string.Join(Path.PathSeparator.ToString(),
            Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User),
            Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine),
            Environment.GetEnvironmentVariable("PATH"));

        static ProcessResult RunProcess(string exe, string arguments, int timeoutMs, CancellationToken token)
        {
            var result = new ProcessResult();
            var output = new StringBuilder();
            var errors = new StringBuilder();
            var info = new ProcessStartInfo(exe, arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            // yt-dlp looks for ffmpeg and its JavaScript runtime (deno) on PATH. Windows calls the variable "Path":
            // setting "PATH" alone left ffmpeg unfound, so any spelling of the old entry is removed first.
            string path = CurrentPath();
            foreach (var key in new List<string>(info.EnvironmentVariables.Keys.Cast<string>()))
                if (string.Equals(key, "PATH", StringComparison.OrdinalIgnoreCase)) info.EnvironmentVariables.Remove(key);
            info.EnvironmentVariables["PATH"] = path;
            using (var process = new Process { StartInfo = info })
            {
                process.OutputDataReceived += (_, e) => { if (e.Data != null) lock (output) output.AppendLine(e.Data); };
                process.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (errors) errors.AppendLine(e.Data); };
                try
                {
                    process.Start();
                }
                catch (Win32Exception)
                {
                    return result;
                }
                result.Started = true;
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                var timer = Stopwatch.StartNew();
                while (!process.WaitForExit(200))
                {
                    result.TimedOut = timer.ElapsedMilliseconds > timeoutMs;
                    if (!result.TimedOut && !token.IsCancellationRequested) continue;
                    KillTree(process.Id);
                    break;
                }
                process.WaitForExit();
                result.ExitCode = process.ExitCode;
            }
            lock (output) result.Output = output.ToString();
            lock (errors) result.Errors = errors.ToString();
            return result;
        }

        /// <summary>yt-dlp leaves ffmpeg running if only the parent is killed.</summary>
        static void KillTree(int pid)
        {
            using (var kill = Process.Start(new ProcessStartInfo("taskkill", $"/PID {pid} /T /F") { UseShellExecute = false, CreateNoWindow = true }))
                kill?.WaitForExit();
        }

        static string LastError(ProcessResult result)
        {
            var lines = (result.Errors ?? "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            string last = lines.Length > 0 ? YtDlpPlaylist.CleanText(lines[lines.Length - 1]) : $"код {result.ExitCode}";
            return last.Length > 160 ? last.Substring(0, 160) + "…" : last;
        }

        // ---- persistence

        void Load()
        {
            try
            {
                if (File.Exists(_savePath)) _saved = JsonUtility.FromJson<Saved>(File.ReadAllText(_savePath)) ?? new Saved();
            }
            catch (Exception e) when (e is IOException || e is ArgumentException)
            {
                Debug.LogWarning($"[ModernDrone] Could not load music settings: {e.Message}");
            }
            _saved.tracks ??= new List<MusicTrack>();
            _saved.tracks.RemoveAll(t => t == null || !YouTubeLinks.IsVideoId(t.id));
        }

        public void Save()
        {
            try
            {
                File.WriteAllText(_savePath, JsonUtility.ToJson(_saved, true));
            }
            catch (IOException e)
            {
                Debug.LogWarning($"[ModernDrone] Could not save music settings: {e.Message}");
            }
        }
    }
}
