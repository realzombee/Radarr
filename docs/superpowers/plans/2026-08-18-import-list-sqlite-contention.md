# Import-list SQLite Contention Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Eliminate the unnecessary SQLite write pressure that prevents all Radarr4K import lists from coexisting, without changing import-list or library-cleanup semantics.

**Architecture:** Keep the existing serial per-list pipeline and make its persistence delta-based: insert new list mappings, update only mappings whose metadata identity changes, and delete removed mappings. Keep `keepAndUnmonitor`, but write only movies that transition from monitored to unmonitored. Use the current upstream Radarr SQLite busy timeout of one second so short WAL conflicts are waited out rather than returned as errors.

**Tech Stack:** C#, .NET, System.Data.SQLite, NUnit, Moq, FluentAssertions.

---

### Task 1: Test and tune the SQLite connection timeout

**Files:**
- Create: `src/NzbDrone.Core.Test/Datastore/ConnectionStringFactoryFixture.cs`
- Modify: `src/NzbDrone.Core/Datastore/ConnectionStringFactory.cs:44-53`

- [ ] **Step 1: Write the failing test**

```csharp
[Test]
public void should_configure_sqlite_busy_timeout_for_one_second()
{
    var connection = Mocker.Resolve<IConnectionStringFactory>().MainDbConnection.ConnectionString;
    var builder = new SQLiteConnectionStringBuilder(connection);

    builder.BusyTimeout.Should().Be(1000);
}
```

Create the fixture as `CoreTest`, import `System.Data.SQLite`, `FluentAssertions`, `NUnit.Framework`, `NzbDrone.Core.Datastore`, and `NzbDrone.Core.Test.Framework`.

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test src/NzbDrone.Core.Test/NzbDrone.Core.Test.csproj --filter FullyQualifiedName~ConnectionStringFactoryFixture --no-restore`

Expected: the assertion reports the current `BusyTimeout` of `100` instead of `1000`.

- [ ] **Step 3: Make the minimal implementation change**

In `ConnectionStringFactory.GetConnectionString`, change only:

```csharp
BusyTimeout = 100
```

to:

```csharp
BusyTimeout = 1000
```

- [ ] **Step 4: Run the focused test to verify it passes**

Run: `dotnet test src/NzbDrone.Core.Test/NzbDrone.Core.Test.csproj --filter FullyQualifiedName~ConnectionStringFactoryFixture --no-restore`

Expected: one passing test, zero failures.

- [ ] **Step 5: Commit the completed task**

```bash
git add src/NzbDrone.Core/Datastore/ConnectionStringFactory.cs src/NzbDrone.Core.Test/Datastore/ConnectionStringFactoryFixture.cs
git commit -m "fix: wait for transient sqlite locks"
```

### Task 2: Persist import-list mappings only when they change

**Files:**
- Create: `src/NzbDrone.Core.Test/ImportListTests/ImportListMovieServiceFixture.cs`
- Modify: `src/NzbDrone.Core/ImportLists/ImportListMovies/ImportListMovieService.cs:43-57`

- [ ] **Step 1: Write failing unit tests**

Create a `CoreTest<ImportListMovieService>` fixture. Build `ImportListMovie` helpers with a valid `Id`, `ListId`, `MovieMetadataId`, and lazy `MovieMetadata` containing a `TmdbId`. Configure `IImportListMovieRepository.GetAllForLists` for the target list. Add these tests:

```csharp
[Test]
public void should_not_update_an_existing_mapping_with_the_same_metadata()
{
    GivenExisting(1, 101, 5001);
    var incoming = GivenIncoming(101, 5001);

    Subject.SyncMoviesForList(new List<ImportListMovie> { incoming }, 1);

    Mocker.GetMock<IImportListMovieRepository>()
          .Verify(x => x.UpdateMany(It.IsAny<IList<ImportListMovie>>()), Times.Never());
}

[Test]
public void should_update_an_existing_mapping_when_its_metadata_identity_changes()
{
    GivenExisting(1, 101, 5001);
    var incoming = GivenIncoming(101, 5002);

    Subject.SyncMoviesForList(new List<ImportListMovie> { incoming }, 1);

    Mocker.GetMock<IImportListMovieRepository>()
          .Verify(x => x.UpdateMany(It.Is<IList<ImportListMovie>>(x => x.Single().Id == 1 && x.Single().MovieMetadataId == 5002)), Times.Once());
}

[Test]
public void should_insert_new_and_delete_removed_mappings()
{
    GivenExisting(1, 101, 5001);
    var incoming = GivenIncoming(202, 6001);

    Subject.SyncMoviesForList(new List<ImportListMovie> { incoming }, 1);

    Mocker.GetMock<IImportListMovieRepository>().Verify(x => x.InsertMany(It.Is<IList<ImportListMovie>>(m => m.Single().TmdbId == 202)), Times.Once());
    Mocker.GetMock<IImportListMovieRepository>().Verify(x => x.DeleteMany(It.Is<IEnumerable<int>>(ids => ids.Single() == 1)), Times.Once());
}
```

- [ ] **Step 2: Run the fixture to verify the no-op test fails**

Run: `dotnet test src/NzbDrone.Core.Test/NzbDrone.Core.Test.csproj --filter FullyQualifiedName~ImportListMovieServiceFixture --no-restore`

Expected: `should_not_update_an_existing_mapping_with_the_same_metadata` fails because the current implementation calls `UpdateMany` for every existing mapping.

- [ ] **Step 3: Implement delta-based persistence**

In `SyncMoviesForList`:

```csharp
var existingListMovies = GetAllForLists(new List<int> { listId });
var existingByTmdbId = existingListMovies.ToDictionary(x => x.TmdbId);

var inserts = listMovies.Where(x => !existingByTmdbId.TryGetValue(x.TmdbId, out _)).ToList();
var updates = new List<ImportListMovie>();

foreach (var listMovie in listMovies.Where(x => existingByTmdbId.TryGetValue(x.TmdbId, out _)))
{
    var existing = existingByTmdbId[listMovie.TmdbId];
    listMovie.Id = existing.Id;

    if (listMovie.MovieMetadataId != existing.MovieMetadataId)
    {
        updates.Add(listMovie);
    }
}

var deletes = existingListMovies.Where(x => listMovies.All(y => y.TmdbId != x.TmdbId)).Select(x => x.Id).ToList();

if (inserts.Any()) { _importListMovieRepository.InsertMany(inserts); }
if (updates.Any()) { _importListMovieRepository.UpdateMany(updates); }
if (deletes.Any()) { _importListMovieRepository.DeleteMany(deletes); }
```

Keep the returned list and the existing `TmdbId` identity behavior unchanged.

- [ ] **Step 4: Run the fixture to verify all delta cases pass**

Run: `dotnet test src/NzbDrone.Core.Test/NzbDrone.Core.Test.csproj --filter FullyQualifiedName~ImportListMovieServiceFixture --no-restore`

Expected: three passing tests, zero failures.

- [ ] **Step 5: Commit the completed task**

```bash
git add src/NzbDrone.Core/ImportLists/ImportListMovies/ImportListMovieService.cs src/NzbDrone.Core.Test/ImportListTests/ImportListMovieServiceFixture.cs
git commit -m "fix: skip unchanged import-list writes"
```

### Task 3: Update only movies that need the unmonitor transition

**Files:**
- Modify: `src/NzbDrone.Core/ImportLists/ImportListSyncService.cs:201-227`
- Modify: `src/NzbDrone.Core.Test/ImportListTests/ImportListSyncServiceFixture.cs:202-257`

- [ ] **Step 1: Write a failing regression test**

Add this test to `ImportListSyncServiceFixture`:

```csharp
[Test]
public void should_not_update_already_unmonitored_movies_when_cleaning_library()
{
    _importListFetch.Movies.ForEach(m => m.ListId = 1);
    GivenList(1, true);
    GivenCleanLevel("keepAndUnmonitor");

    var alreadyUnmonitored = _existingMovies.Select(x =>
    {
        x.Monitored = false;
        return x;
    }).ToList();

    Mocker.GetMock<IMovieService>().Setup(v => v.GetAllMovies()).Returns(alreadyUnmonitored);
    Mocker.GetMock<IImportListMovieService>().Setup(v => v.GetAllListMovies()).Returns(_list1Movies);

    Subject.Execute(_commandAll);

    Mocker.GetMock<IMovieService>().Verify(v => v.UpdateMovie(It.IsAny<List<Movie>>(), true), Times.Never());
}
```

- [ ] **Step 2: Run the fixture to verify the test fails**

Run: `dotnet test src/NzbDrone.Core.Test/NzbDrone.Core.Test.csproj --filter FullyQualifiedName~ImportListSyncServiceFixture --no-restore`

Expected: the new test fails because the current code sends already-unmonitored movies to `UpdateMovie`.

- [ ] **Step 3: Implement the minimal cleanup guard**

In the `keepAndUnmonitor` branch, require `movie.Monitored` before logging and adding the movie:

```csharp
case "keepAndUnmonitor" when movie.Monitored:
    _logger.Info("{0} was in your library, but not found in your lists --> Keeping in library but Unmonitoring it", movie);
    movie.Monitored = false;
    moviesToUpdate.Add(movie);
    break;
```

Then invoke `_movieService.UpdateMovie(moviesToUpdate, true)` only when `moviesToUpdate.Any()`.

- [ ] **Step 4: Run the focused fixture and preserve the transition behavior**

Run: `dotnet test src/NzbDrone.Core.Test/NzbDrone.Core.Test.csproj --filter FullyQualifiedName~ImportListSyncServiceFixture --no-restore`

Expected: all existing cleanup tests and the new already-unmonitored regression pass.

- [ ] **Step 5: Commit the completed task**

```bash
git add src/NzbDrone.Core/ImportLists/ImportListSyncService.cs src/NzbDrone.Core.Test/ImportListTests/ImportListSyncServiceFixture.cs
git commit -m "fix: skip unchanged library cleanup writes"
```

### Task 4: Verify, publish, deploy, and stage-enable lists

**Files:**
- Modify: no source files

- [ ] **Step 1: Run the complete targeted regression suite**

Run:

```bash
dotnet test src/NzbDrone.Core.Test/NzbDrone.Core.Test.csproj --filter 'FullyQualifiedName~ConnectionStringFactoryFixture|FullyQualifiedName~ImportListMovieServiceFixture|FullyQualifiedName~ImportListSyncServiceFixture|FullyQualifiedName~FetchAndParseImportListServiceFixture|FullyQualifiedName~CommandQueueManagerFixture|FullyQualifiedName~CommandQueueFixture' --no-restore
```

Expected: zero test failures.

- [ ] **Step 2: Build the production image and preserve a database backup**

Build the Radarr image using the established `radarr4k:6.5.1.2032-import-list-delta-persistence-<short-sha>` tag. Before recreating the container, create a timestamped, verified SQLite backup on Synology; do not modify list state during backup.

- [ ] **Step 3: Deploy only Radarr4K and verify readiness**

Replace only the Radarr4K image/container. Verify `/ping`, `PRAGMA quick_check`, WAL journal mode, the running image tag, no started commands, and zero new `Busy` entries after startup.

- [ ] **Step 4: Stage-enable remaining lists without a manual sync**

Keep Indian enabled. Enable Kids through Radarr’s API, then observe two natural scheduler cycles. If both finish without a command backlog or `Busy`, enable Movies and observe two more natural cycles. Do not enable a subsequent list if the current gate fails; immediately re-pause only the list under test.

- [ ] **Step 5: Create the PR and report live verification separately**

Push the branch and update the existing pull request with the design, source changes, targeted test output, image tag, backup confirmation, and each scheduler-cycle result. Do not claim all-list success unless all four staged cycles complete with no fresh lock entries.
