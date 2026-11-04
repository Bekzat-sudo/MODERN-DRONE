using System.Collections.Generic;
using ModernDrone.Core;
using UnityEngine;

namespace ModernDrone.UI
{
    /// <summary>
    /// Music screen: paste a YouTube playlist link, get its songs as a list, play them with the built-in player.
    /// Plain class hosted by the main menu and the pause menu, like <see cref="SettingsMenu"/>.
    /// </summary>
    public sealed class MusicMenu
    {
        const int MaxUrlLength = 300;
        const int ShownUrlLength = 34;

        readonly MenuGui _gui;
        readonly List<MenuRow> _rows = new List<MenuRow>();
        int _selected, _version = -1;

        public bool Closed { get; private set; }

        public MusicMenu(MenuGui gui) => _gui = gui;

        static MusicPlayer P => MusicPlayer.Instance;

        public void Open()
        {
            Closed = false;
            _selected = 0;
            BuildRows();
        }

        public void Update()
        {
            if (_version != P.TracksVersion) BuildRows();
            var nav = _gui.ReadNav();
            // While a text field is being typed into, keys belong to the field (Esc only ends the edit).
            if (MenuRow.Editing != null) return;
            if (nav.back)
            {
                Close();
                return;
            }
            MenuGui.Navigate(_rows, ref _selected, nav);
        }

        void Close()
        {
            P.Save();
            Closed = true;
        }

        void BuildRows()
        {
            _version = P.TracksVersion;
            _rows.Clear();
            _rows.Add(MenuRow.Button("Вставить ссылку из буфера и загрузить", () =>
            {
                P.Url = GUIUtility.systemCopyBuffer.Trim();
                P.LoadPlaylist(P.Url);
            }));
            var url = MenuRow.Text("Ссылка на плейлист или микс", () => P.Url, v => P.Url = v, MaxUrlLength, AcceptUrlChar);
            var editable = url.Value;
            url.Value = () => Tail(editable());
            _rows.Add(url);
            _rows.Add(MenuRow.Button("Загрузить список песен", () => P.LoadPlaylist(P.Url), () => P.IsListing ? "читаю…" : ""));
            _rows.Add(MenuRow.Button("Микс от текущей песни", P.LoadMix));

            _rows.Add(MenuRow.Header("Плеер"));
            _rows.Add(MenuRow.Button("Играть / пауза", P.TogglePause, () => P.IsPlaying ? "▶ играет" : P.IsActive ? "пауза" : "стоп"));
            _rows.Add(MenuRow.Button("Следующая", () => P.Step(1)));
            _rows.Add(MenuRow.Button("Предыдущая", () => P.Step(-1)));
            _rows.Add(MenuRow.Button("Стоп", P.Stop));
            _rows.Add(MenuRow.Slider("Громкость музыки", () => P.Volume, v => P.Volume = v, 0f, 1f, 0.05f));
            _rows.Add(MenuRow.Toggle("Перемешать", () => P.Shuffle, v => P.Shuffle = v));

            _rows.Add(MenuRow.Header("Настройка"));
            _rows.Add(MenuRow.Text("Свой путь к yt-dlp (необязательно)", () => P.YtDlpPath, v => P.YtDlpPath = v, 260, c => c));
            _rows.Add(MenuRow.Button("Очистить кэш песен", P.ClearCache, () => $"{P.CacheBytes() / (1024 * 1024)} МБ"));
            _rows.Add(MenuRow.Button("← Назад (сохранить)", Close));

            var tracks = P.Tracks;
            _rows.Add(MenuRow.Header(tracks.Count > 0 ? $"Песни: {tracks.Count}  {P.PlaylistTitle}" : "Песни: список пуст"));
            for (int i = 0; i < tracks.Count; i++)
            {
                int index = i;
                string duration = tracks[i].DurationText;
                _rows.Add(MenuRow.Button($"{i + 1:00}  {tracks[i].title}", () => P.Play(index),
                    () => P.CurrentIndex == index ? "▶ " + duration : duration));
            }
        }

        static char AcceptUrlChar(char c) => c > ' ' && c < 127 ? c : '\0';

        static string Tail(string text) => text.Length <= ShownUrlLength ? text : "…" + text.Substring(text.Length - ShownUrlLength);

        public void Draw(Rect r)
        {
            _gui.EnsureStyles();
            MenuGui.Fill(r, MenuGui.PanelColor);
            float pad = r.height * 0.03f;
            GUI.Label(new Rect(r.x, r.y + pad, r.width, _gui.title.fontSize * 1.2f), "МУЗЫКА", _gui.title);

            float lineH = _gui.subtitle.fontSize * 1.6f, top = r.y + pad + _gui.title.fontSize * 1.4f;
            var track = P.CurrentTrack;
            GUI.Label(new Rect(r.x + pad, top, r.width - pad * 2, lineH), track != null ? track.title : "Ничего не играет", _gui.subtitle);
            GUI.Label(new Rect(r.x + pad, top + lineH, r.width - pad * 2, lineH), P.Status, _gui.hint);

            float bodyTop = top + lineH * 2f + pad * 0.5f;
            _gui.DrawRows(new Rect(r.x + pad, bodyTop, r.width - pad * 2, r.yMax - bodyTop - pad * 2 - _gui.hint.fontSize * 1.6f), _rows, ref _selected);
            GUI.Label(new Rect(r.x, r.yMax - pad - _gui.hint.fontSize * 1.6f, r.width, _gui.hint.fontSize * 1.6f),
                "↑↓ — выбор,  ←→ — значение,  Enter / A — действие,  Ctrl+V в поле — вставить,  Esc / B — назад", _gui.hint);
        }
    }
}
