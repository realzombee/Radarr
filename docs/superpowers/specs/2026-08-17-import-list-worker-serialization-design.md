# Import-list Worker Serialization Design

## Goal

Prevent a single `ImportListSync` command from running multiple enabled import-list pipelines concurrently and contending for Radarr's SQLite database.

## Decision

`FetchAndParseImportListService.Fetch()` will process enabled lists in their existing factory order. A list's fetch, TMDb mapping, import-list-movie persistence, and sync-status update must finish before the next list begins.

The existing `lock (result)` serializes only the result-update block after independently scheduled long-running workers have started. Replacing the worker task fan-out with direct loop execution makes the no-overlap invariant explicit for the entire pipeline, including fetch-start timing and any writes reached from mapping or provider callbacks.

## Scope

Only the all-lists `Fetch()` path changes. `FetchSingleList()` retains its current behavior. No list settings, command-queue behavior, database schema, or provider behavior changes.

## Failure Handling

An exception in one list remains logged and does not prevent the next enabled list from being considered, matching the current worker-body behavior. Failed reports are not persisted; blocked lists continue to set `AnyFailure`.

## Regression Evidence

The focused fixture will make the first list's `Fetch()` wait on a test gate. While it is blocked, the test verifies the second list's `Fetch()` has not begun. This fails on the existing task-fan-out implementation and passes only when the next list is not started until the first pipeline returns.

## Release Validation

Build and run focused import-list and command-queue tests, push the follow-up commit to PR #5, build a production-version-matched overlay, and deploy only Radarr4K. Keep the verified SQLite backup. Re-enable RadarrIndian (4), RadarrKids (13), and Radarr-Movies (12), in that order, one at a time; observe two five-minute scheduled cycles per list before enabling the next. On any fresh SQLite busy/locked entry, over-cadence command, or command accumulation, disable the current list and stop the rollout.
