using System.Collections.Generic;
using ModernDrone.Core;
using ModernDrone.Drone;
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

        public void Draw(Rect screen)
        {
            var t = UiTheme.T;
            float width = Mathf.Min(screen.width - 2f * UiTheme.Px(UiTheme.Margin), UiTheme.Px(1320f));
            var panel = new Rect(screen.center.x - width * 0.5f, screen.y + UiTheme.Px(UiTheme.S7), width, screen.height - 2f * UiTheme.Px(UiTheme.S7));
            UiTheme.Panel(panel);
            var inner = UiTheme.Inset(panel, UiTheme.Px(40f), UiTheme.Px(UiTheme.S6));

            var header = new Rect(inner.x, inner.y, inner.width, UiTheme.Px(56f));
            UiTheme.Text(header, "МУЗЫКА", t.title, UiTheme.TextPrimary);
            var track = P.CurrentTrack;
            UiTheme.Text(header, track != null ? track.title : "ничего не играет", t.captionRight, UiTheme.TextSecondary);

            var prompts = new Rect(inner.x, inner.yMax - UiTheme.Px(36f), inner.width, UiTheme.Px(36f));
            var note = new Rect(inner.x, prompts.y - UiTheme.Px(UiTheme.S4) - UiTheme.Px(60f), inner.width, UiTheme.Px(60f));
            // Rows reach into the panel padding so their text lines up with the title.
            float rowPad = UiTheme.Px(UiTheme.S5);
            float top = header.yMax + UiTheme.Px(UiTheme.S4);
            var body = new Rect(inner.x - rowPad, top, inner.width + 2f * rowPad - UiTheme.Px(UiTheme.S4), note.y - top - UiTheme.Px(UiTheme.S5));
            _gui.DrawRows(body, _rows, ref _selected);

            if (!string.IsNullOrEmpty(P.Status))
            {
                UiTheme.Fill(new Rect(note.x, note.y, note.width, 1f), UiTheme.Border);
                UiTheme.Text(new Rect(note.x, note.y + UiTheme.Px(UiTheme.S3), note.width, note.height - UiTheme.Px(UiTheme.S3)), P.Status, t.body, UiTheme.TextBody);
            }

            if (UiTheme.Prompts(prompts, false, ("↑↓", "пункт"), ("←→", "значение"), (ControlInput.Key("Enter", "A"), "выбрать"), ("Ctrl+V", "вставить в поле"), (ControlInput.Key("Esc", "B"), "назад")) == 4)
                Close();
        }
    }
}
