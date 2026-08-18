using System.Collections.Generic;
using System.Linq;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.ImportLists.ImportListMovies;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.ImportListTests
{
    [TestFixture]
    public class ImportListMovieServiceFixture : CoreTest<ImportListMovieService>
    {
        private List<ImportListMovie> _existingListMovies;

        [SetUp]
        public void Setup()
        {
            _existingListMovies = new List<ImportListMovie>();

            Mocker.GetMock<IImportListMovieRepository>()
                  .Setup(v => v.GetAllForLists(It.IsAny<List<int>>()))
                  .Returns(_existingListMovies);
        }

        private ImportListMovie GivenExisting(int id, int tmdbId, int movieMetadataId)
        {
            var listMovie = GivenIncoming(tmdbId, movieMetadataId);
            listMovie.Id = id;
            _existingListMovies.Add(listMovie);

            return listMovie;
        }

        private ImportListMovie GivenIncoming(int tmdbId, int movieMetadataId)
        {
            return new ImportListMovie
            {
                ListId = 1,
                MovieMetadataId = movieMetadataId,
                MovieMetadata = new LazyLoaded<MovieMetadata>(new MovieMetadata
                {
                    Id = movieMetadataId,
                    TmdbId = tmdbId
                })
            };
        }

        [Test]
        public void should_not_update_an_existing_mapping_with_the_same_metadata()
        {
            GivenExisting(1, 101, 5001);
            var incoming = GivenIncoming(101, 5001);

            Subject.SyncMoviesForList(new List<ImportListMovie> { incoming }, 1);

            Mocker.GetMock<IImportListMovieRepository>()
                  .Verify(v => v.UpdateMany(It.IsAny<IList<ImportListMovie>>()), Times.Never());
        }

        [Test]
        public void should_update_an_existing_mapping_when_its_metadata_identity_changes()
        {
            GivenExisting(1, 101, 5001);
            var incoming = GivenIncoming(101, 5002);

            Subject.SyncMoviesForList(new List<ImportListMovie> { incoming }, 1);

            Mocker.GetMock<IImportListMovieRepository>()
                  .Verify(v => v.UpdateMany(It.Is<IList<ImportListMovie>>(x => x.Single().Id == 1 && x.Single().MovieMetadataId == 5002)), Times.Once());
        }

        [Test]
        public void should_insert_new_and_delete_removed_mappings()
        {
            GivenExisting(1, 101, 5001);
            var incoming = GivenIncoming(202, 6001);

            Subject.SyncMoviesForList(new List<ImportListMovie> { incoming }, 1);

            Mocker.GetMock<IImportListMovieRepository>()
                  .Verify(v => v.InsertMany(It.Is<IList<ImportListMovie>>(x => x.Single().TmdbId == 202)), Times.Once());
            Mocker.GetMock<IImportListMovieRepository>()
                  .Verify(v => v.DeleteMany(It.Is<List<ImportListMovie>>(x => x.Single().Id == 1)), Times.Once());
        }
    }
}
