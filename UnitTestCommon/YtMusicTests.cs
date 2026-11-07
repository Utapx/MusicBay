using System.IO;
using System.Linq;
using Common;
using NUnit.Framework;

namespace UnitTestCommon
{
    /// <summary>Parses saved YouTube Music responses (trimmed to three items each) from TestData/YtMusic.</summary>
    [TestFixture]
    public class YtMusicTests
    {
        static string Data(string name) => File.ReadAllText(Path.Combine(TestContext.CurrentContext.TestDirectory, "TestData", "YtMusic", name + ".json"));

        [Test]
        public void SearchSongs()
        {
            var songs = YtMusic.ParseSearch(Data("search_songs"), YtKind.Song);
            Assert.That(songs, Has.Count.EqualTo(3));
            var first = songs[0];
            Assert.That(first.Title, Is.EqualTo("Get Lucky (feat. Pharrell Williams and Nile Rodgers)"));
            Assert.That(first.VideoId, Is.EqualTo("4D7u5KF7SP8"));
            Assert.That(first.Artist, Is.EqualTo("Daft Punk, Pharrell Williams & Nile Rodgers"));
            Assert.That(first.Album, Is.EqualTo("Random Access Memories"));
            Assert.That(first.DurationMs, Is.EqualTo((6 * 60 + 10) * 1000));
            Assert.That(first.ThumbUrl, Does.Contain("=w544-h544"));
        }

        [Test]
        public void SearchAlbumsAndArtists()
        {
            var album = YtMusic.ParseSearch(Data("search_albums"), YtKind.Album)[0];
            Assert.That(album.Title, Is.EqualTo("Random Access Memories"));
            Assert.That(album.Artist, Is.EqualTo("Daft Punk"));
            Assert.That(album.BrowseId, Is.EqualTo("MPREb_K8qWMWVqXGi"));

            var artist = YtMusic.ParseSearch(Data("search_artists"), YtKind.Artist)[0];
            Assert.That(artist.Title, Is.EqualTo("Daft Punk"));
            Assert.That(artist.BrowseId, Is.EqualTo("UCRr1xG_2WIDs18a6cIiCxeA"));
        }

        [Test]
        public void AlbumTracksTakeAlbumNameAndArt()
        {
            var tracks = YtMusic.ParseSongs(Data("album"), out string? allSongs);
            Assert.That(allSongs, Is.Null);
            Assert.That(tracks, Has.Count.EqualTo(3));
            Assert.That(tracks[0].Title, Is.EqualTo("Give Life Back to Music"));
            Assert.That(tracks[0].VideoId, Is.EqualTo("IluRBvnYMoY"));
            Assert.That(tracks[0].DurationMs, Is.EqualTo((4 * 60 + 36) * 1000));
            Assert.That(tracks.All(t => t.Album == "Random Access Memories" && t.ThumbUrl != null), Is.True);
        }

        [Test]
        public void ArtistLinksToAllSongs()
        {
            var top = YtMusic.ParseSongs(Data("artist"), out string? allSongs);
            Assert.That(allSongs, Is.EqualTo("VLOLAK5uy_lNVBcjNtiCwq-n95uoxJ-Gd4vsfMkyZNs"));
            Assert.That(top[0].Title, Is.EqualTo("Instant Crush (feat. Julian Casablancas)"));

            var all = YtMusic.ParseSongs(Data("artist_songs"), out string? next);
            Assert.That(next, Is.Null);
            Assert.That(all, Has.Count.EqualTo(3));
            Assert.That(all.All(s => s.VideoId != null && s.Title != "" && s.Artist != "" && s.DurationMs > 0 && s.ThumbUrl != null), Is.True);
            Assert.That(all.Select(s => s.Album), Has.None.EqualTo("Top songs")); // the playlist's title is not an album
        }

        [Test]
        public void PlaylistPagesChain()
        {
            var first = YtMusic.ParseSongs(Data("playlist_page"), out _);
            Assert.That(first, Has.Count.EqualTo(2));
            Assert.That(YtMusic.NextPage(Data("playlist_page")), Does.StartWith("4qmFsg"));

            var more = YtMusic.ParseSongs(Data("playlist_continuation"), out _);
            Assert.That(more, Has.Count.EqualTo(3));
            Assert.That(more.All(s => s.VideoId != null && s.DurationMs > 0), Is.True);
            Assert.That(YtMusic.NextPage(Data("playlist_continuation")), Is.Null);
            Assert.That(YtMusic.NextPage(Data("album")), Is.Null);
        }

        [Test]
        public void Cards()
        {
            var cards = YtMusic.ParseCards(Data("cards"));
            Assert.That(cards, Has.Count.EqualTo(3));
            Assert.That(cards[0].Title, Is.EqualTo("Random Access Memories (Drumless Edition)"));
            Assert.That(cards[0].BrowseId, Is.EqualTo("MPREb_OgeEnoHTsCm"));
            Assert.That(cards[0].ThumbUrl, Does.Contain("=w544-h544"));
        }

        [Test]
        public void SignInHeader()
        {
            const string cookie = "PREF=x; SAPISID=abcDEF/123; SID=y";
            Assert.That(YtMusic.IsSession(cookie), Is.True);
            Assert.That(YtMusic.IsSession("PREF=x"), Is.False);
            Assert.That(YtMusic.IsSession(null), Is.False);
            // expected value computed separately: sha1("1700000000 abcDEF/123 https://music.youtube.com")
            Assert.That(YtMusic.AuthHeader(cookie, 1700000000), Is.EqualTo("SAPISIDHASH 1700000000_a7ca805bfb7d44212f4eb3f5e51d74ed968bb718"));
            Assert.That(YtMusic.AuthHeader("__Secure-3PAPISID=abcDEF/123", 1700000000), Is.EqualTo("SAPISIDHASH 1700000000_a7ca805bfb7d44212f4eb3f5e51d74ed968bb718"));
        }

        [TestCase("4:36", 276000)]
        [TestCase("1:02:03", 3723000)]
        [TestCase("55M plays", 0)]
        [TestCase(null, 0)]
        public void ParseDuration(string? text, long expected)
        {
            Assert.That(YtMusic.ParseDuration(text), Is.EqualTo(expected));
        }
    }
}
