# Import-list Worker Serialization Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ensure one `ImportListSync` command cannot overlap enabled import-list pipelines.

**Architecture:** `FetchAndParseImportListService.Fetch()` will execute each accepted list directly in the existing loop rather than submitting all lists to a long-running `TaskFactory`. Existing result aggregation, failure handling, TMDb mapping, list-movie synchronization, and status updates remain unchanged.

**Tech Stack:** C#, .NET 8, NUnit, FluentAssertions, Moq.

---

### Task 1: Prove that a later list does not start while the first list is blocked

**Files:**
- Modify: `src/NzbDrone.Core.Test/ImportListTests/FetchAndParseImportListServiceFixture.cs`
- Test: `src/NzbDrone.Core.Test/ImportListTests/FetchAndParseImportListServiceFixture.cs`

- [ ] **Step 1: Write the failing regression test**

```csharp
[Test]
public void should_not_start_the_next_list_until_the_current_list_finishes()
{
    using var firstListStarted = new ManualResetEventSlim();
    using var releaseFirstList = new ManualResetEventSlim();
    var first = CreateListResult(1, true, true, new ImportListFetchResult());
    var second = CreateListResult(2, true, true, new ImportListFetchResult());

    first.Setup(x => x.Fetch()).Returns(() =>
    {
        firstListStarted.Set();
        releaseFirstList.Wait();
        return new ImportListFetchResult();
    });

    var fetch = Task.Run(() => Subject.Fetch());
    firstListStarted.Wait(TimeSpan.FromSeconds(1)).Should().BeTrue();
    second.Verify(x => x.Fetch(), Times.Never());

    releaseFirstList.Set();
    fetch.Wait(TimeSpan.FromSeconds(1)).Should().BeTrue();
    second.Verify(x => x.Fetch(), Times.Once());
}
```

- [ ] **Step 2: Verify the test is red**

Run:

```bash
dotnet test src/NzbDrone.Core.Test/Radarr.Core.Test.csproj /p:RunAnalyzers=false --filter 'FullyQualifiedName~FetchAndParseImportListServiceFixture.should_not_start_the_next_list_until_the_current_list_finishes'
```

Expected: FAIL because the current `TaskFactory.StartNew` immediately invokes the second list's `Fetch()`.

### Task 2: Make all-list processing sequential

**Files:**
- Modify: `src/NzbDrone.Core/ImportLists/FetchAndParseImportListService.cs`
- Test: `src/NzbDrone.Core.Test/ImportListTests/FetchAndParseImportListServiceFixture.cs`

- [ ] **Step 1: Replace worker creation with direct execution**

Remove `System.Threading.Tasks`, the task list, and `TaskFactory`. Execute the existing try/catch and `lock (result)` body directly after the blocked-list checks, then remove `Task.WaitAll(...)`.

- [ ] **Step 2: Verify the focused fixture is green**

Run:

```bash
dotnet test src/NzbDrone.Core.Test/Radarr.Core.Test.csproj /p:RunAnalyzers=false --filter 'FullyQualifiedName~FetchAndParseImportListServiceFixture'
```

Expected: all fixture tests pass, including the new gating regression.

### Task 3: Verify, publish, and stage production

**Files:**
- Modify: `docs/superpowers/specs/2026-08-17-import-list-worker-serialization-design.md`
- Modify: `docs/superpowers/plans/2026-08-17-import-list-worker-serialization.md`

- [ ] **Step 1: Run the focused import-list and queue persistence suites**

```bash
dotnet test src/NzbDrone.Core.Test/Radarr.Core.Test.csproj /p:RunAnalyzers=false --filter 'FullyQualifiedName~FetchAndParseImportListServiceFixture|FullyQualifiedName~CommandQueueManagerFixture|FullyQualifiedName~CommandQueueFixture'
```

Expected: all selected tests pass.

- [ ] **Step 2: Build the version-matched Core assembly and update PR #5**

Commit the test, implementation, design, and plan; push `fix/import-list-sync-db-lock` to `johoja12/Radarr`. Confirm PR #5 contains both `7df4bfa` and the new serialization commit.

- [ ] **Step 3: Deploy only Radarr4K and validate staged scheduler cycles**

Create a fresh verified SQLite backup, build the overlay image from the pinned 6.5.1.2032 production image, recreate only `radarr4k`, then check `/ping`, `PRAGMA quick_check`, no active commands, and no fresh locked/busy logs. Enable IDs 4, 13, and 12 one at a time, observing two scheduled cycles for each before proceeding. Disable the active list and stop if any rollback condition occurs.
