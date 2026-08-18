# Import-list SQLite Contention Design

## Goal

Allow all three Radarr4K import lists to remain enabled without recurring SQLite `Busy` failures, while preserving the configured `keepAndUnmonitor` library policy.

## Evidence

- `FetchAndParseImportListService` now processes import lists sequentially, so the original parallel per-list writer collision is removed.
- `ImportListMovieService.SyncMoviesForList` still calls `UpdateMany` for every existing list movie on every refresh. The live lists contain 1,322 Indian, 3,241 Movies, and 609 Kids records: 5,172 unchanged records would be rewritten every five minutes.
- With `keepAndUnmonitor`, `ImportListSyncService.CleanLibrary` currently includes movies that are already unmonitored. The live database has 40 such movies outside all lists, so every sync re-saves unchanged records.
- The deployed version configures SQLite with a 100 ms busy timeout. Current upstream Radarr commit `520bf4215a13` increases it to 1,000 ms specifically for SQLite busy handling.

## Design

### SQLite tuning

Set the SQLite connection busy timeout to 1,000 ms, matching upstream Radarr. This lets a legitimate short WAL write complete instead of failing an unrelated reader immediately; it does not conceal a long-running transaction.

### Import-list persistence

Treat the existing import-list record as authoritative when its `TmdbId` already maps to the current metadata row. During a normal refresh, only:

- insert records newly returned by a list;
- delete records removed from a list; and
- update an existing row if its persisted `MovieMetadataId` differs from the current metadata row.

The operation must not issue an update for an unchanged row. The existing sequential list-worker behavior remains unchanged.

### Library cleanup

For `keepAndUnmonitor`, collect only movies that are both absent from all synced lists and currently monitored. Skip the bulk update entirely when the collection is empty. This preserves the policy: any movie requiring an unmonitor transition is still updated exactly once.

## Validation

Automated tests will prove that unchanged list rows do not reach `UpdateMany`, unchanged already-unmonitored movies do not reach `UpdateMovie`, and changed metadata mappings do still update. The source test suite must pass before deployment.

After deployment, leave Indian enabled and enable Kids, then Movies. For each step, observe two scheduled import cycles, verify each command completes before a backlog forms, and require zero new SQLite `Busy` entries. Disable only the list under test if a gate fails.

## Non-goals

- No migration to PostgreSQL.
- No change to `keepAndUnmonitor` semantics.
- No increase to the five-minute scheduler interval.
- No retry loop or global lock around unrelated API traffic.
