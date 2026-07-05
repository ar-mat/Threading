# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Overview

`Armat.Threading` is a C# .NET 8.0 class library — an alternative to the .NET Task Parallel Library (TPL). `Job` / `Job<TResult>` mirror `Task` / `Task<T>`, adding support for multiple independent thread pools (`JobScheduler` instances) in one process and context propagation through async call chains (`JobRuntimeScope`). Published to NuGet as `armat.threading`.

## Commands

All commands run from the repository root. **Always build/test via the solution file, not the csproj** — the projects compute their output path from `$(SolutionDir)`, so building a csproj directly resolves output to the wrong location (e.g. `dotnet test` on the csproj looks for the test dll in `D:\bin` and fails).

```powershell
# Build (Debug is the default configuration)
dotnet build Solution/Armat.Threading/Armat.Threading.sln -c Debug

# Run all tests
dotnet test Solution/Armat.Threading/Armat.Threading.sln -c Debug

# Run a single test (or a class via -Class suffix filters)
dotnet test Solution/Armat.Threading/Armat.Threading.sln -c Debug --filter "FullyQualifiedName~RunJSL_010_VerifyJobsRunInScheduler"

# Pack / publish NuGet artifacts (must be run from BuildScripts/ — scripts cd relative to it)
cd BuildScripts; ./Pack.ps1      # -Configuration Release by default
cd BuildScripts; ./Publish.ps1
```

Build output for both projects goes to a flat, shared `bin/<Configuration>/` directory at the repo root. Tests use xUnit; many involve deliberate `Thread.Sleep` calls (~0.5–1s each) and TPL-vs-Job performance comparisons, so the full suite takes minutes — prefer `--filter` while iterating.

### Debug builds require the sibling Utilities repo

In `Debug` configuration both projects reference `armat.utils.dll` from a locally built sibling repository (`D:/Sources/GitHub/Utilities/bin/Debug/armat.utils.dll`); in `Release` they use the `armat.utils` NuGet package instead. If a Debug build fails on a missing `armat.utils` reference, build the `Utilities` repo (sibling of this repo) in Debug first.

## Architecture

Two projects in `Projects/`: `Threading` (the library, ~5k lines across 10 files) and `ThreadingTest` (xUnit tests). The solution lives in `Solution/Armat.Threading/`.

### Core execution model

- **`Job` / `Job<TResult>`** ([Job.cs](Projects/Threading/Job.cs)) — the central async unit, both classes in this one file. Holds the delegate, state, status, exceptions, continuations, and the parent/child (`Initiator`/`Root`) hierarchy. `Job.Current` is the job running on the calling thread. Status transitions use interlocked int fields, not locks.
- **`JobMethodBuilder`** ([JobMethodBuilder.cs](Projects/Threading/JobMethodBuilder.cs)) — `Job` is annotated with `[AsyncMethodBuilder(typeof(JobMethodBuilder))]`, which is what makes `async Job` methods compile. It wraps the TPL `AsyncTaskMethodBuilder` for state-machine stepping and reuses a single internal `ResultJob` across awaiter iterations (resetting its status) instead of allocating per step.
- **`IJobScheduler` → `JobSchedulerBase` → `JobScheduler`** — the scheduler hierarchy. `JobSchedulerBase` owns the static `Default`/`Current` semantics; `JobScheduler` ([JobScheduler.cs](Projects/Threading/JobScheduler.cs)) is the concrete implementation with two thread pools and two bounded queues: regular and long-running (`JobCreationOptions.LongRunning`). Configured via the `JobSchedulerConfiguration` record; exposes `Statistics` / `MethodBuilderStatistics`.
- **Scheduler resolution**: `IJobScheduler.Current` returns the scheduler set by an active `JobSchedulerScope` (see `EnterScope()`) or falls back to `Default`. Continuations inherit the antecedent's scheduler unless `HideScheduler` is set. `JobSchedulerBase.Default` may be assigned **only once per process** (via `JobScheduler.SetAsDefault()`); a second assignment throws.

### Context propagation

- **`JobRuntimeScope`** ([JobRuntimeScope.cs](Projects/Threading/JobRuntimeScope.cs)) — key/value scopes that flow through async chains. The same file contains the plumbing: `ThreadRuntimeContext` (per-thread scope stack), `JobRuntimeContext` (scopes captured into a `Job` at creation), and `JobMethodBuilderContext`. Lookup order is current job's context, then thread context. `Enter()` returns `JobRuntimeScope.Null` (check `IsNull`) if the key already exists — reentrant scopes are intentionally rejected so a nested `using` can't dispose an outer scope.
- **`CorrelationIdScope` / `CorrelationIdScope<T>`** ([CorrelationIDScope.cs](Projects/Threading/CorrelationIDScope.cs)) — auto-incrementing correlation IDs layered on `JobRuntimeScope`; the generic parameter creates independent ID sequences.

### Definitions and conventions that constrain changes

- **`JobDefinitions.cs`** — `JobStatus`, `JobCreationOptions`, `JobContinuationOptions`, `TransientAggregateException`. `JobCreationOptions` is deliberately a bit-compatible subset of `JobContinuationOptions` (creation options are stored in the same int as continuation options) — never add a flag to one enum without checking the bit is free in the other. `TransientAggregateException` gets flattened: its inner exceptions are appended to the job's exception list individually.
- **Continuations run on any antecedent outcome by default** — an earlier design auto-applied `OnlyOnRanToCompletion`, which swallowed failure propagation and was intentionally removed. Don't reintroduce it.
- **Awaiting**: parameterless `GetAwaiter()` is `[Obsolete]`; all awaits in library and test code use `.ConfigureAwait(...)`. Follow that pattern.
- **Code style**: explicit CLR type names (`Int32`, `String`, `Boolean`, `Object`) instead of C# keywords, tabs for indentation, file-scoped namespaces, `ImplicitUsings` disabled, `Nullable` enabled, `EnforceCodeStyleInBuild` on (style violations can fail the build).

### Tests

Test classes are named `JobSchedulerUnitTest_<Area>` (AsyncAwait, AwaiterConfig, ErrorHandling, MultipleSchedulers, Performance, RuntimeScope, WaitMany). Most tests run the same workload through both TPL `Task` and `Job` via the [Helpers/Executor.cs](Projects/ThreadingTest/Helpers/Executor.cs) harness (`WorkerType`: Void/Job/JobT/Task/TaskT) and assert equivalent behavior — when changing `Job` semantics, the TPL comparison defines the expected behavior.

## Releasing

Bump `<Version>` in [Threading.csproj](Projects/Threading/Threading.csproj) (`PackageVersion` derives from it; `_NugetVersionPostfix` adds a `-beta`-style suffix). `Projects/Threading/Readme.md` is packed into the NuGet package and is separate from the root [README.md](README.md).
