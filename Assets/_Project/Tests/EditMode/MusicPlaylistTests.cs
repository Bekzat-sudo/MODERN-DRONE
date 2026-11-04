using System.Linq;
using ModernDrone.Core;
using NUnit.Framework;

namespace ModernDrone.Tests
{
    public class MusicPlaylistTests
    {
        const string Id1 = "dQw4w9WgXcQ", Id2 = "abcDEF_-123";

        [TestCase("https://www.youtube.com/playlist?list=PL123", "https://www.youtube.com/playlist?list=PL123")]
        [TestCase("  http://youtube.com/playlist?list=PL123 ", "https://youtube.com/playlist?list=PL123")]
        [TestCase("www.youtube.com/watch?v=dQw4w9WgXcQ&list=PL1", "https://www.youtube.com/watch?v=dQw4w9WgXcQ&list=PL1")]
        [TestCase("https://music.youtube.com/playlist?list=PL1", "https://music.youtube.com/playlist?list=PL1")]
        [TestCase("https://youtu.be/dQw4w9WgXcQ", "https://youtu.be/dQw4w9WgXcQ")]
        public void Links_YouTubeIsAcceptedAsHttps(string text, string expected)
        {
            Assert.IsTrue(YouTubeLinks.TryNormalize(text, out var url));
            Assert.AreEqual(expected, url);
        }

        [TestCase("")]
        [TestCase("not a link")]
        [TestCase("https://example.com/playlist?list=PL1")]
        [TestCase("https://youtube.com.example.com/playlist")]
        [TestCase("https://youtube.com@example.com/playlist")]
        [TestCase("https://www.youtube.com:8443/playlist")]
        [TestCase("ftp://www.youtube.com/playlist")]
        [TestCase("file:///C:/Windows/win.ini")]
        [TestCase("--exec calc")]
        [TestCase("https://www.youtube.com/watch?v=x\" --exec \"calc")]
        [TestCase("https://www.youtube.com/watch?v=x --exec calc")]
        public void Links_AnythingElseIsRejected(string text) =>
            Assert.IsFalse(YouTubeLinks.TryNormalize(text, out _));

        [Test]
        public void Links_MixOfASongIsAValidLink()
        {
            string mix = YouTubeLinks.MixUrl(Id1);
            Assert.AreEqual($"https://www.youtube.com/watch?v={Id1}&list=RD{Id1}", mix);
            Assert.IsTrue(YouTubeLinks.TryNormalize(mix, out var url));
            Assert.AreEqual(mix, url);
        }

        [Test]
        public void Parse_ReturnsSongsInOrder()
        {
            string json = "{\"id\":\"PL1\",\"title\":\"Мой плейлист\",\"_type\":\"playlist\",\"entries\":[" +
                $"{{\"_type\":\"url\",\"id\":\"{Id1}\",\"title\":\"Песня один\",\"duration\":212.0}}," +
                $"{{\"_type\":\"url\",\"id\":\"{Id2}\",\"title\":\"Two\",\"duration\":null}}]}}";
            var tracks = YtDlpPlaylist.Parse(json, out var title);
            Assert.AreEqual("Мой плейлист", title);
            CollectionAssert.AreEqual(new[] { Id1, Id2 }, tracks.Select(t => t.id).ToArray());
            Assert.AreEqual("Песня один", tracks[0].title);
            Assert.AreEqual("3:32", tracks[0].DurationText);
            Assert.AreEqual("", tracks[1].DurationText);
        }

        [Test]
        public void Parse_SkipsDeletedPrivateAndNonVideoEntries()
        {
            string json = "{\"title\":\"P\",\"entries\":[" +
                "{\"id\":\"aaaaaaaaaaa\",\"title\":\"[Deleted video]\"}," +
                "{\"id\":\"bbbbbbbbbbb\",\"title\":\"[Private video]\"}," +
                "{\"id\":\"../../evil\",\"title\":\"Bad id\"}," +
                "{\"id\":\"UCshortchannelidlongerthan11\",\"title\":\"Channel tab\"}," +
                "null," +
                $"{{\"id\":\"{Id1}\",\"title\":\"Good\"}}]}}";
            var tracks = YtDlpPlaylist.Parse(json, out _);
            Assert.AreEqual(1, tracks.Count);
            Assert.AreEqual(Id1, tracks[0].id);
        }

        [Test]
        public void Parse_SingleVideoLinkGivesOneTrack()
        {
            var tracks = YtDlpPlaylist.Parse($"{{\"id\":\"{Id1}\",\"title\":\"Solo\",\"duration\":3700}}", out _);
            Assert.AreEqual(1, tracks.Count);
            Assert.AreEqual("1:01:40", tracks[0].DurationText);
        }

        [TestCase("")]
        [TestCase("ERROR: nope")]
        [TestCase("{\"title\":\"Empty\",\"entries\":[]}")]
        public void Parse_NothingUsableGivesEmptyList(string json) =>
            Assert.AreEqual(0, YtDlpPlaylist.Parse(json, out _).Count);

        [Test]
        public void CleanText_DisarmsRichTextAndControlCharacters() =>
            Assert.AreEqual("‹size=99›Big‹/size›  x", YtDlpPlaylist.CleanText("<size=99>Big</size>\n\tx"));

        [Test]
        public void Arguments_KeepTheUrlAfterTheOptionTerminator()
        {
            StringAssert.EndsWith("-- \"https://youtu.be/x\"", YtDlpPlaylist.ListArguments("https://youtu.be/x"));
            StringAssert.Contains($"-o \"{Id1}.%(ext)s\"", YtDlpPlaylist.DownloadArguments(Id1, "C:/cache"));
            StringAssert.EndsWith($"-- \"https://www.youtube.com/watch?v={Id1}\"", YtDlpPlaylist.DownloadArguments(Id1, "C:/cache"));
        }

        [Test]
        public void Queue_WrapsAtBothEnds()
        {
            var q = new MusicQueue();
            q.Reset(3);
            Assert.AreEqual(0, q.Current);
            Assert.AreEqual(2, q.Advance(-1));
            Assert.AreEqual(0, q.Advance(1));
            Assert.AreEqual(1, q.Peek(1));
            Assert.AreEqual(0, q.Current, "Peek must not move");
        }

        [Test]
        public void Queue_EmptyHasNoCurrent()
        {
            var q = new MusicQueue();
            q.Reset(0);
            Assert.AreEqual(-1, q.Current);
            Assert.AreEqual(-1, q.Advance(1));
            Assert.AreEqual(-1, q.Peek(1));
            q.Shuffle = true;
            Assert.AreEqual(-1, q.Current);
        }

        [Test]
        public void Queue_ShuffleKeepsCurrentAndPlaysEverySongOnce()
        {
            var q = new MusicQueue(42);
            q.Reset(50);
            q.Jump(17);
            q.Shuffle = true;
            Assert.AreEqual(17, q.Current);
            var played = Enumerable.Range(0, 50).Select(_ => q.Current == -1 ? -1 : Next(q)).ToArray();
            CollectionAssert.AreEquivalent(Enumerable.Range(0, 50).ToArray(), played);
            CollectionAssert.AreNotEqual(Enumerable.Range(0, 50).ToArray(), played);
        }

        [Test]
        public void Queue_ShuffleOffContinuesInListOrder()
        {
            var q = new MusicQueue(42);
            q.Reset(10);
            q.Shuffle = true;
            q.Jump(4);
            q.Shuffle = false;
            Assert.AreEqual(4, q.Current);
            Assert.AreEqual(5, q.Advance(1));
        }

        static int Next(MusicQueue q)
        {
            int current = q.Current;
            q.Advance(1);
            return current;
        }
    }
}
