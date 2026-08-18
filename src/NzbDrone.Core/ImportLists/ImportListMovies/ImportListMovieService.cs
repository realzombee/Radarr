using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.ThingiProvider.Events;

namespace NzbDrone.Core.ImportLists.ImportListMovies
{
    public interface IImportListMovieService
    {
        List<ImportListMovie> GetAllListMovies();
        List<ImportListMovie> GetAllForLists(List<int> listIds);
        ImportListMovie AddListMovie(ImportListMovie listMovie);
        List<ImportListMovie> AddListMovies(List<ImportListMovie> listMovies);
        List<ImportListMovie> SyncMoviesForList(List<ImportListMovie> listMovies, int listId);
        void RemoveListMovie(ImportListMovie listMovie);
        ImportListMovie GetById(int id);
        bool ExistsByMetadataId(int metadataId);
    }

    public class ImportListMovieService : IImportListMovieService, IHandleAsync<ProviderDeletedEvent<IImportList>>
    {
        private readonly IImportListMovieRepository _importListMovieRepository;
        private readonly Logger _logger;

        public ImportListMovieService(IImportListMovieRepository importListMovieRepository,
                             Logger logger)
        {
            _importListMovieRepository = importListMovieRepository;
            _logger = logger;
        }

        public ImportListMovie AddListMovie(ImportListMovie exclusion)
        {
            return _importListMovieRepository.Insert(exclusion);
        }

        public List<ImportListMovie> AddListMovies(List<ImportListMovie> listMovies)
        {
            _importListMovieRepository.InsertMany(listMovies);

            return listMovies;
        }

        public List<ImportListMovie> SyncMoviesForList(List<ImportListMovie> listMovies, int listId)
        {
            var existingListMovies = GetAllForLists(new List<int> { listId });
            var existingByTmdbId = existingListMovies.ToDictionary(x => x.TmdbId);
            var listMoviesToInsert = new List<ImportListMovie>();
            var listMoviesToUpdate = new List<ImportListMovie>();

            foreach (var listMovie in listMovies)
            {
                if (existingByTmdbId.TryGetValue(listMovie.TmdbId, out var existingListMovie))
                {
                    listMovie.Id = existingListMovie.Id;

                    if (listMovie.MovieMetadataId != existingListMovie.MovieMetadataId)
                    {
                        listMoviesToUpdate.Add(listMovie);
                    }
                }
                else
                {
                    listMoviesToInsert.Add(listMovie);
                }
            }

            var listMoviesToDelete = existingListMovies.Where(l => listMovies.All(x => x.TmdbId != l.TmdbId)).ToList();

            if (listMoviesToInsert.Any())
            {
                _importListMovieRepository.InsertMany(listMoviesToInsert);
            }

            if (listMoviesToUpdate.Any())
            {
                _importListMovieRepository.UpdateMany(listMoviesToUpdate);
            }

            if (listMoviesToDelete.Any())
            {
                _importListMovieRepository.DeleteMany(listMoviesToDelete);
            }

            return listMovies;
        }

        public List<ImportListMovie> GetAllListMovies()
        {
            return _importListMovieRepository.All().ToList();
        }

        public List<ImportListMovie> GetAllForLists(List<int> listIds)
        {
            return _importListMovieRepository.GetAllForLists(listIds).ToList();
        }

        public void RemoveListMovie(ImportListMovie listMovie)
        {
            _importListMovieRepository.Delete(listMovie);
        }

        public ImportListMovie GetById(int id)
        {
            return _importListMovieRepository.Get(id);
        }

        public void HandleAsync(ProviderDeletedEvent<IImportList> message)
        {
            var moviesOnList = _importListMovieRepository.GetAllForLists(new List<int> { message.ProviderId });
            _importListMovieRepository.DeleteMany(moviesOnList);
        }

        public bool ExistsByMetadataId(int metadataId)
        {
            return _importListMovieRepository.ExistsByMetadataId(metadataId);
        }
    }
}
