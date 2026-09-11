using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Parser;
using NzbDrone.Core.ThingiProvider;
using SpotifyAPI.Web;
using SpotifyAPI.Web.Models;
using Tubifarry.ImportLists.Spotify;

namespace NzbDrone.Core.ImportLists.Spotify
{
    /// <summary>
    /// Imports the authenticated user's "Liked Songs".
    /// Spotify does not expose Liked Songs as a playlist - it has no playlist id and never appears
    /// in /v1/me/playlists - so it cannot be reached through SpotifyPlaylist. The only source is the
    /// saved-tracks endpoint behind GetSavedTracks.
    /// </summary>
    public class SpotifySavedTracks : SpotifyImportListBase<SpotifySavedTracksSettings>
    {
        private const int PageSize = 50;

        public SpotifySavedTracks(ISpotifyProxy spotifyProxy,
                                  IMetadataRequestBuilder requestBuilder,
                                  IImportListStatusService importListStatusService,
                                  IImportListRepository importListRepository,
                                  IConfigService configService,
                                  IParsingService parsingService,
                                  IHttpClient httpClient,
                                  Logger logger)
        : base(spotifyProxy, requestBuilder, importListStatusService, importListRepository, configService, parsingService, httpClient, logger)
        {
        }

        public override string Name => "Spotify Liked Songs";

        public override ProviderMessage Message => new(
            "Imports the albums behind the authenticated user's Liked Songs. Liked Songs is a library " +
            "collection rather than a playlist, so it cannot be selected in the Spotify Playlists list. " +
            "Each liked track is mapped to its album, and albums are de-duplicated, so a small number of " +
            "albums may be added from a large number of liked tracks.",
            ProviderMessageType.Info);

        public override IList<SpotifyImportListItemInfo> Fetch(SpotifyWebAPI api)
        {
            List<SpotifyImportListItemInfo> result = new();

            // Albums are de-duplicated here rather than downstream: liking ten tracks off one record
            // would otherwise queue that record ten times.
            HashSet<string> seenAlbums = new(StringComparer.OrdinalIgnoreCase);

            Paging<SavedTrack>? savedTracks = api.GetSavedTracks(PageSize);

            if (savedTracks?.HasError() ?? false)
            {
                _logger.Warn($"Could not fetch liked songs: {savedTracks.Error?.Status} {savedTracks.Error?.Message}");
                return result;
            }

            _logger.Trace($"Got {savedTracks?.Total ?? 0} liked songs");

            while (true)
            {
                if (savedTracks?.Items == null)
                {
                    return result;
                }

                foreach (SavedTrack savedTrack in savedTracks.Items)
                {
                    SpotifyImportListItemInfo? item = ParseSavedTrack(savedTrack, seenAlbums);
                    result.AddIfNotNull(item);
                }

                if (!savedTracks.HasNextPage())
                {
                    break;
                }

                savedTracks = _spotifyProxy.GetNextPage(this, api, savedTracks);
            }

            _logger.Trace($"Mapped liked songs to {result.Count} distinct albums");

            return result;
        }

        private SpotifyImportListItemInfo? ParseSavedTrack(SavedTrack savedTrack, HashSet<string> seenAlbums)
        {
            // A saved track can have a null track if it is no longer available in the user's market.
            SimpleAlbum? album = savedTrack?.Track?.Album;

            if (album == null)
            {
                return null;
            }

            if (!Settings.IncludeSingles && album.AlbumType.IsNotNullOrWhiteSpace()
                && !album.AlbumType.Equals("album", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            string? albumName = album.Name;
            string? artistName = album.Artists?.FirstOrDefault()?.Name ?? savedTrack?.Track?.Artists?.FirstOrDefault()?.Name;

            if (albumName.IsNullOrWhiteSpace() || artistName.IsNullOrWhiteSpace())
            {
                return null;
            }

            // Fall back to artist/album when Spotify gives us no album id, so untagged entries still de-duplicate.
            string key = album.Id.IsNotNullOrWhiteSpace() ? album.Id : $"{artistName}|{albumName}";

            if (!seenAlbums.Add(key))
            {
                return null;
            }

            _logger.Trace($"Adding {artistName} - {albumName}");

            return new SpotifyImportListItemInfo
            {
                Artist = artistName,
                Album = albumName,
                AlbumSpotifyId = album.Id,
                ReleaseDate = ParseSpotifyDate(album.ReleaseDate, album.ReleaseDatePrecision)
            };
        }
    }
}
