using SpotifyAPI.Web;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MusicleBot.services
{
    public class SpotifyService
    {
        private readonly string _clientId;
        private readonly string _clientSecret;

        private SpotifyClient _client;
        private string _accessToken;
        private DateTime _tokenExpiry;

        public SpotifyService(string clientId, string clientSecret)
        {
            _clientId = clientId;
            _clientSecret = clientSecret;
        }

        // Get SpotifyClient with valid access token
        private async Task<SpotifyClient> GetClient()
        {

            if (_client != null && DateTime.UtcNow < _tokenExpiry)
                return _client;


            var oauth = new OAuthClient();
            var tokenResponse = await oauth.RequestToken(
                new ClientCredentialsRequest(_clientId, _clientSecret)
            );

            _accessToken = tokenResponse.AccessToken;
            _tokenExpiry = DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn - 60);

            _client = new SpotifyClient(_accessToken);
            return _client;
        }

        // Get tracks from a Spotify playlist URL
        public async Task<List<TrackData>> GetTracksFromPlaylist(string playlistUrl)
        {
            var client = await GetClient();

            string id = playlistUrl
                .Split("/playlist/")
                .Last()
                .Split("?")
                .First();

            var tracks = new List<TrackData>();
            int offset = 0;

            while (true)
            {
                var page = await client.Playlists.GetItems(id, new PlaylistGetItemsRequest
                {
                    Offset = offset,
                    Limit = 100
                });

                foreach (var item in page.Items)
                {
                    if (item.Track is FullTrack track)
                    {
                        int year = 0;
                        if (!string.IsNullOrWhiteSpace(track.Album?.ReleaseDate))
                        {
                            var date = track.Album.ReleaseDate;
                            if (date.Length >= 4 && int.TryParse(date.Substring(0, 4), out var y))
                                year = y;
                        }

                        tracks.Add(new TrackData
                        {
                            Name = track.Name,
                            Artist = track.Artists.First().Name,
                            DurationSeconds = track.DurationMs / 1000,
                            Year = year,
                            AlbumName = track.Album.Name,


                            TrackPopularity = 0,
                            ArtistPopularity = 0,
                            ArtistGenres = new List<string>()
                        });
                    }
                }


                if (page.Items.Count < 100)
                    break;

                offset += 100;
            }

            return tracks;
        }

        // Get playlist name from URL
        public async Task<string> GetPlaylistName(string playlistUrl)
        {
            var client = await GetClient();

            string id = playlistUrl
                .Split("/playlist/")
                .Last()
                .Split("?")
                .First();

            try
            {
                var playlist = await client.Playlists.Get(id);
                return playlist.Name;
            }
            catch
            {
                return "Unknown Playlist";
            }
        }

        // Load extra info: track popularity, artist popularity, genres etc.
        public async Task<(int trackPop, int artistPop, List<string> genres)>
    LoadExtraInfo(string trackName, string artistName)
        {
            var client = await GetClient();

            var query = $"track:\"{trackName}\" artist:\"{artistName}\"";

            var search = await client.Search.Item(
                new SearchRequest(SearchRequest.Types.Track, query)
                {
                    Limit = 1,
                    Market = "US"
                });

            var track = search.Tracks.Items.FirstOrDefault();
            if (track == null)
                return (0, 0, new List<string>());

            int trackPop = track.Popularity;

            int artistPop = 0;
            List<string> genres = new();

            var artist = await client.Artists.Get(track.Artists.First().Id);
            if (artist != null)
            {
                artistPop = artist.Popularity;
                genres = artist.Genres.ToList();
            }

            return (trackPop, artistPop, genres);
        }



        // Data structure for track info
        public class TrackData
        {
            public string Name { get; set; }
            public string Artist { get; set; }
            public int Year { get; set; }
            public int DurationSeconds { get; set; }
            public int TrackPopularity { get; set; }
            public int ArtistPopularity { get; set; }
            public List<string> ArtistGenres { get; set; } = new();
            public string AlbumName { get; set; }

        }
    }
}
