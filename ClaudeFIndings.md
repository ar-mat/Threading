# Claude Code Review Findings — Armat.Threading

Review date: 2026-07-05. Reviewed version: 2.0.6 (branch `main`, commit `f46f595`).
Scope: full read of `Projects/Threading` (~5,200 lines) plus the `armat.utils` primitives it uses and the test suite.

Findings **F1–F6 were confirmed by running repro programs** against the built library — they are real, observed failures, not hypotheses. Findings F7–F11 are high-confidence code-read findings. F12+ are minor issues.

This document is written so that each finding can be fixed independently by following its own section. Read the "Ground rules" first, then work through findings in the order given in the priority table.

---

## Ground rules for whoever fixes these

1. **Build and test via the solution file only** (never the csproj directly — output paths break):
   ```powershell
   dotnet build Solution/Armat.Threading/Armat.Threading.sln -c Debug
   dotnet test  Solution/Armat.Threading/Armat.Threading.sln -c Debug
   dotnet test  Solution/Armat.Threading/Armat.Threading.sln -c Debug --filter "FullyQualifiedName~<TestName>"
   ```
   Debug builds need the sibling repo `D:/Sources/GitHub/Utilities` built in Debug first (provides `armat.utils.dll`).

2. **Code style is enforced at build time** (`EnforceCodeStyleInBuild`): use explicit CLR type names (`Int32`, `String`, `Boolean`, `Object` — not `int`, `string`, `bool`, `object`), tabs for indentation, file-scoped namespaces. Await only with `.ConfigureAwait(...)` — the parameterless `GetAwaiter()` is `[Obsolete]`.

3. **Line numbers in this document may drift.** Every finding includes a short *code anchor* — an exact code snippet to search for. Trust the anchor over the line number.

4. **Do not** re-introduce automatic `OnlyOnRanToCompletion` on continuations (that was removed deliberately — it swallowed failure propagation). Do not add flags to `JobCreationOptions` or `JobContinuationOptions` without checking the bit is unused in the *other* enum — they share one integer field.

5. Several findings interact. **F2, F3, F4 must be fixed for F1's fix to be fully effective** — fix them in the order F2 → F3 → F4 → F1, then add the verification tests, then continue with the rest.

6. Each finding below has a "Verify" section containing an xUnit test. Add these to a new file `Projects/ThreadingTest/Cancellation.cs` (or similar) in class `JobSchedulerUnitTest_Cancellation`. They must FAIL before the fix and PASS after. Required usings for the tests:
   ```cs
   using System;
   using System.Collections.Generic;
   using System.Linq;
   using System.Threading;
   using Xunit;
   using Armat.Threading;
   ```

### Priority table

| Order | Finding | Severity | Confidence | Effort |
|-------|---------|----------|------------|--------|
| 1 | F2 — awaiting a token-canceled Job never resumes | Critical | Confirmed | Trivial (1 line) |
| 2 | F3 — `WaitNoThrow(timeout, token)` misreports cancellation as completion | High | Confirmed | Trivial (1 line) |
| 3 | F4 — successful job throws `OperationCanceledException` after the fact | High | Confirmed | Small |
| 4 | F1 — `Cancel()` strands waiters and continuations forever | Critical | Confirmed | Medium |
| 5 | F5 — `WhenAll`/`WhenAny` enumerate lazy inputs 4 times | High | Confirmed | Small |
| 6 | F6 — `JobRuntimeScope.Enter` succeeds but value is shadowed | Medium | Confirmed | Small |
| 7 | F7 — scheduler queue priority is inverted vs. intent | Medium-High | Code-read | Trivial + decision |
| 8 | F8 — process-wide `SynchronizationContext.Send` kill-switch | Medium | Code-read | Small |
| 9 | F9 — useless finalizer on every `Job` (GC pressure) | Medium (perf) | Code-read | Trivial |
| 10 | F10 — `Initiator` chain roots all ancestor jobs | Medium | Code-read | Decision needed |
| 11 | F11 — user code runs while holding an internal lock | Low-Medium | Code-read | Small |
| 12+ | F12–F20 — minor issues | Low | Code-read | Trivial each |

---

## F1 — `Cancel()` of a pending job never signals waiters and never runs continuations

**Severity: Critical. Confidence: Confirmed by repro (a `Wait()`er hung until the process was killed).**

### Problem

When a job transitions to `JobStatus.Canceled` anywhere *other than* the normal execution path (`Job.ExecuteProcedureCore`), two things that must happen on completion never happen:

1. `SignalCompletion()` is never called → the `ManualResetEventSlim` behind `Wait()` / `AsyncWaitHandle` is never set → **any thread already blocked in `Wait()` sleeps forever**.
2. `ExecuteContinuations()` is never called → **continuations registered with `ContinueWith`, and awaiter continuations created by `await`, never fire** → awaiting code hangs forever.

Threads that call `Wait()` *after* the cancellation are fine (the `IsCompleted` fast path catches it). Only already-registered waiters/continuations are stranded.

### Affected code paths (all four must be fixed)

1. `Job.Cancel()`, scheduler-null branch — file `Projects/Threading/Job.cs`, anchor:
   ```cs
   if (UpdateStatus(JobStatus.Canceled, JobStatus.Created))
       canceled = true;
   ```
2. `JobScheduler.Cancel(Job)` — file `Projects/Threading/JobScheduler.cs`, anchor:
   ```cs
   // dequeue the job
   if (!UpdateJobStatus(job, JobStatus.Canceled, JobStatus.WaitingForActivation))
       return false;
   ```
3. Queue-drain loop in `JobScheduler.DedicatedThreadProc` — anchor (appears once in `DedicatedThreadProc`):
   ```cs
   while ((job = _jobsQueueLongRunning.Dequeue()) != null)
   {
       UpdateJobStatus(job, JobStatus.Canceled, JobStatus.WaitingForActivation);
   }
   ```
4. Queue-drain loop in `JobScheduler.ScheduleThreadProc` — same shape, over `_jobsQueue`.

### Fix (recommended)

Add one internal helper to `Job` (in `Job.cs`, near `SignalCompletion`) that finalizes an externally-imposed cancellation:

```cs
// completes a job that was canceled before it started executing:
// wakes up waiters and schedules the continuations
internal void FinalizeCancellation()
{
	SignalCompletion();
	ExecuteContinuations();
}
```

Then:

- In `Job.Cancel()` scheduler-null branch:
  ```cs
  if (UpdateStatus(JobStatus.Canceled, JobStatus.Created))
  {
      canceled = true;
      FinalizeCancellation();
  }
  ```
- In `JobScheduler.Cancel(Job)`, after the status update succeeds and before `return true;`:
  ```cs
  job.FinalizeCancellation();
  ```
- In both queue-drain loops, only when the update succeeded:
  ```cs
  while ((job = _jobsQueueLongRunning.Dequeue()) != null)
  {
      if (UpdateJobStatus(job, JobStatus.Canceled, JobStatus.WaitingForActivation))
          job.FinalizeCancellation();
  }
  ```

Why this is safe:
- `ExecuteContinuations()` internally calls `ExecuteContinuationsCore(Status)`; since `Status` is now `Canceled` (which is ≥ `RanToCompletion`), the continuation bookkeeping (`_execContinuationsBaseIndex`) is extended correctly, and `CanRunContinuation` still honors `NotOnCanceled` filters.
- Awaiter continuations will then run and their `GetResult()` will throw `OperationCanceledException` into the awaiting method — **this only works after F2 is fixed** (otherwise the continuation itself may be suppressed by the shared token).
- `JobScheduler` is in the same assembly, so it can call the `internal` method.

### Verify

```cs
[Fact]
public void Cancel_Of_Queued_Job_Wakes_Blocked_Waiter()
{
	using JobScheduler scheduler = new("cancelTest", 1, 1);

	// occupy the single pool thread so the next job stays queued
	Job blocker = new(() => Thread.Sleep(2000), CancellationToken.None, JobCreationOptions.None, scheduler);
	blocker.Run();
	Thread.Sleep(200);

	Job queued = new(() => { }, CancellationToken.None, JobCreationOptions.None, scheduler);
	queued.Run();
	Thread.Sleep(100);

	Boolean waiterWoke = false;
	Thread waiter = new(() => { queued.WaitNoThrow(); waiterWoke = true; });
	waiter.Start();
	Thread.Sleep(200); // let the waiter block on the wait handle

	Assert.True(queued.Cancel());
	Assert.True(waiter.Join(3000), "Wait()er must wake up after Cancel()");
	Assert.True(waiterWoke);
	Assert.Equal(JobStatus.Canceled, queued.Status);
}

[Fact]
public void Cancel_Of_Queued_Job_Runs_Continuations()
{
	using JobScheduler scheduler = new("cancelTest2", 1, 1);

	Job blocker = new(() => Thread.Sleep(2000), CancellationToken.None, JobCreationOptions.None, scheduler);
	blocker.Run();
	Thread.Sleep(200);

	Job queued = new(() => { }, CancellationToken.None, JobCreationOptions.None, scheduler);
	using ManualResetEventSlim continuationRan = new(false);
	queued.ContinueWith(j => continuationRan.Set());
	queued.Run();
	Thread.Sleep(100);

	Assert.True(queued.Cancel());
	Assert.True(continuationRan.Wait(3000), "continuation must run after Cancel()");
}
```

---

## F2 — Awaiting a Job whose `CancellationToken` fires never resumes the awaiter

**Severity: Critical. Confidence: Confirmed by repro (awaiting task never completed).**

### Problem

`Job.SetAwaiterContinuation` creates the continuation that resumes an awaiting method. It passes **the antecedent job's own `CancellationToken`** into that continuation job. When the token is canceled:

1. The awaited job cancels itself (correct).
2. Its continuations run (`ExecuteContinuations`) — but the awaiter continuation *shares the canceled token*, so its own pre-run check (`ThrowIfCompletedUnexpectedly` inside `ExecuteProcedureCore`) throws `OperationCanceledException` **before invoking the state machine's `MoveNext`**.
3. The awaiting method is never resumed. `await job` hangs forever.

TPL semantics: an awaiter continuation must *always* run; cancellation is surfaced by `GetResult()` throwing.

Note: `async Job` methods awaiting other Jobs use a different path (`MethodBuilderOnCompleted` with the builder's own `ResultJob`, which has no token) and are NOT affected. The hang hits `async Task` methods awaiting a `Job`, and any direct `OnCompleted` / `UnsafeOnCompleted` registration. That is why the existing test suite never caught it.

### Affected code

File `Projects/Threading/Job.cs`, method `SetAwaiterContinuation`. Anchor:

```cs
return ContinueWith((Delegate)continuation, configuration, CancellationToken, continuationOptions, Scheduler);
```

### Fix

Replace `CancellationToken` (the job's own token property) with `CancellationToken.None`:

```cs
return ContinueWith((Delegate)continuation, configuration, CancellationToken.None, continuationOptions, Scheduler);
```

One line. The awaiter continuation must be unstoppable; the awaiting code observes cancellation via `GetResult()` → `ThrowIfCompletedUnexpectedly(true)` → `OperationCanceledException`.

### Verify

```cs
[Fact]
public void Await_Of_Token_Canceled_Job_Resumes_With_OCE()
{
	using CancellationTokenSource cts = new();
	Job job = Job.Run(() =>
	{
		Thread.Sleep(800);
		cts.Token.ThrowIfCancellationRequested();
	}, cts.Token);

	String outcome = "not-resumed";
	System.Threading.Tasks.Task awaitTask = System.Threading.Tasks.Task.Run(async () =>
	{
		try
		{
			await job.ConfigureAwait(false);
			outcome = "resumed-ok";
		}
		catch (OperationCanceledException)
		{
			outcome = "threw-oce";
		}
	});

	Thread.Sleep(200); // let the await register its continuation
	cts.Cancel();

	Assert.True(awaitTask.Wait(4000), "await must resume after token cancellation");
	Assert.Equal("threw-oce", outcome);
}
```

---

## F3 — `WaitNoThrow(Int32, CancellationToken)` reports token cancellation as job completion

**Severity: High. Confidence: Confirmed by repro (returned `true` at ~300 ms while the job was still `Running`).**

### Problem

`WaitHandle.WaitAny` returns the **index of whichever handle signaled** (0 = the job's handle, 1 = the cancellation token's handle) or `WaitHandle.WaitTimeout`. The current code treats *any* non-timeout result as "job completed":

File `Projects/Threading/Job.cs`, anchor:

```cs
return WaitHandle.WaitAny(new WaitHandle[] { AsyncWaitHandleSlim.WaitHandle, cancellationToken.WaitHandle }, millisecondsTimeout) != WaitHandle.WaitTimeout;
```

So when the *token* fires, the method returns `true` ("completed") for a job that is still running. This also corrupts `Wait(Int32, CancellationToken)`, which uses the return value to decide whether to throw `OperationCanceledException` — it skips the throw and reports success.

### Fix

Return `true` only when the *job's* handle (index 0) signaled:

```cs
return WaitHandle.WaitAny(new WaitHandle[] { AsyncWaitHandleSlim.WaitHandle, cancellationToken.WaitHandle }, millisecondsTimeout) == 0;
```

**Do NOT change** the `void WaitNoThrow(CancellationToken)` overload just above it — it has no return value, and its caller (`Wait(CancellationToken)`) already re-checks the token afterwards. That one is correct as-is.

After this fix, `Wait(Int32, CancellationToken)` behaves correctly with no further change: on token cancellation `completed` is `false`, the token check throws `OperationCanceledException` (TPL parity).

### Verify

```cs
[Fact]
public void WaitNoThrow_With_Timeout_And_Token_Does_Not_Report_Cancellation_As_Completion()
{
	Job job = Job.Run(() => Thread.Sleep(3000));
	using CancellationTokenSource cts = new(300);

	Boolean result = job.WaitNoThrow(5000, cts.Token);

	Assert.False(result, "token cancellation must not be reported as job completion");
	job.WaitNoThrow(); // cleanup: let the job finish
}
```

---

## F4 — A successfully completed job throws `OperationCanceledException` if its token is canceled later

**Severity: High. Confidence: Confirmed by repro (`Wait()` threw OCE with `Status == RanToCompletion`).**

### Problem

`ThrowIfCompletedUnexpectedly` checks the raw token *before* looking at the job's status:

File `Projects/Threading/Job.cs`, anchor:

```cs
protected void ThrowIfCompletedUnexpectedly(Boolean throwInnerException)
{
	if (CancellationToken.IsCancellationRequested || IsCanceled)
	{
		throw new OperationCanceledException(CancellationToken);
	}
```

Consequence: run a job with a token, let it finish successfully, cancel the token afterwards → every subsequent `Wait()`, `Result`, and `await` throws `OperationCanceledException` even though `Status == RanToCompletion`. TPL bases this decision on final task status only.

The same flawed "token = canceled" equation appears in `CanRunContinuation` (same file), anchor:

```cs
if ((continuationOptions & JobContinuationOptions.NotOnCanceled) == JobContinuationOptions.NotOnCanceled)
{
	if (executionStatus == JobStatus.Canceled || CancellationToken.IsCancellationRequested)
		return false;
}
```

which wrongly skips `OnlyOnRanToCompletion`-style continuations of a *successful* job whose token fired later.

### Fix (two edits)

**Edit 1** — rewrite `ThrowIfCompletedUnexpectedly` so that: faulted state reports exceptions; cancellation is reported only when the job's status is `Canceled`, or the token fired while the job has *not yet completed* (this keeps the pre-run cancellation check in `ExecuteProcedureCore` working):

```cs
protected void ThrowIfCompletedUnexpectedly(Boolean throwInnerException)
{
	// a faulted job reports its exception(s)
	List<ExceptionDispatchInfo>? listExceptions = _listExceptions;
	if (listExceptions != null)
	{
		lock (listExceptions)
		{
			if (throwInnerException && listExceptions.Count == 1)
				listExceptions[0].Throw();
			else
				throw Exception!;   // exception is never null when _listExceptions != null
		}
	}

	// a canceled job (or a not-yet-completed job with a canceled token) reports cancellation
	if (IsCanceled || (CancellationToken.IsCancellationRequested && !IsCompleted))
	{
		throw new OperationCanceledException(CancellationToken);
	}
}
```

Behavior table for the new version:

| Job state | Token | Result |
|-----------|-------|--------|
| RanToCompletion | canceled after completion | returns normally (was: threw OCE — the bug) |
| Canceled | any | throws OCE (unchanged) |
| WaitingToRun / Running | canceled | throws OCE (unchanged — needed by the pre-run check) |
| Faulted | any | throws the exception(s) (was: OCE if token also canceled — now exception wins, TPL parity) |

**Edit 2** — in `CanRunContinuation`, delete `|| CancellationToken.IsCancellationRequested` from the `NotOnCanceled` branch (the `executionStatus` parameter already carries the antecedent's actual outcome). Leave the `NotOnFaulted` branch (`|| Exception != null`) unchanged — it is needed for the `WaitingForChildrenToComplete` phase where the final status is not yet set.

### Verify

```cs
[Fact]
public void Wait_After_Successful_Completion_Ignores_Late_Token_Cancellation()
{
	using CancellationTokenSource cts = new();
	Job job = Job.Run(() => { }, cts.Token);
	job.Wait(); // completes successfully

	cts.Cancel(); // cancel AFTER completion

	job.Wait(); // must NOT throw
	Assert.Equal(JobStatus.RanToCompletion, job.Status);
}
```

---

## F5 — `WhenAll` / `WhenAny` enumerate a lazy `IEnumerable` input four times

**Severity: High. Confidence: Confirmed by repro (12 jobs created from a 3-element LINQ query).**

### Problem

The `IEnumerable` overloads enumerate the sequence once for `Count()`, again in the registration loop, and again (twice) inside the waiter's lambda (`GetJobsExceptions`, `GetJobsResults` / `GetAnyCompletedJob`). If the caller passes a lazy LINQ query that *creates* jobs (e.g. `items.Select(i => Job.Run(...))`), each enumeration creates and starts a **new generation of jobs**: continuations get registered on one generation, results are read from a different one. Observed: 12 jobs ran instead of 3.

### Affected code

File `Projects/Threading/Job.cs`. Anchors — the four public overloads:

```cs
public static Job WhenAll(IEnumerable<Job> jobs)
{
	return WhenAll(jobs, jobs.Count());
}
```
```cs
public static Job<TResult[]> WhenAll<TResult>(IEnumerable<Job<TResult>> jobs)
{
	return WhenAll(jobs, jobs.Count());
}
```
```cs
public static Job<Job> WhenAny(IEnumerable<Job> jobs)
{
	return WhenAny(jobs, jobs.Count());
}
```
```cs
public static Job<Job<TResult>> WhenAny<TResult>(IEnumerable<Job<TResult>> jobs)
{
	return WhenAny(jobs, jobs.Count());
}
```

### Fix

Materialize exactly once in each `IEnumerable` overload and pass the array through (the `params Job[]` overloads already pass arrays; the private implementations then capture the same array in their lambdas — no change needed inside them):

```cs
public static Job WhenAll(IEnumerable<Job> jobs)
{
	Job[] arrJobs = jobs as Job[] ?? jobs.ToArray();
	return WhenAll(arrJobs, arrJobs.Length);
}
```

Apply the same pattern to all four overloads (`System.Linq` is already imported in the file).

Notes for the fixer:
- Duplicate job references in the input are **already handled correctly** by the `LockCounter` mechanism (a duplicated job fires the shared continuation once per registration). Do not "fix" duplicates.
- Optional TPL-parity improvement (separate decision): `WhenAll` with an **empty** input currently throws `ArgumentException`; TPL returns an already-completed task. If parity is desired, return `Job.CompletedJob` (and `Job.FromResult(Array.Empty<TResult>())` for the generic version) instead of throwing. `WhenAny(empty)` throwing matches TPL and should stay.

### Verify

```cs
[Fact]
public void WhenAll_Enumerates_Lazy_Input_Exactly_Once()
{
	Int32 factoryCalls = 0;
	IEnumerable<Job<Int32>> jobs = Enumerable.Range(0, 3)
		.Select(i =>
		{
			Interlocked.Increment(ref factoryCalls);
			return Job.Run(() => i);
		});

	Job<Int32[]> all = Job.WhenAll(jobs);
	Assert.True(all.WaitNoThrow(4000));
	Assert.Equal(3, Volatile.Read(ref factoryCalls));
}
```

---

## F6 — `JobRuntimeScope.Enter` succeeds for a key inherited from the job context, but `GetValue` keeps returning the outer value

**Severity: Medium. Confidence: Confirmed by repro (`Enter` returned a live scope; `GetValue` returned the outer value).**

### Problem

The duplicate-key check in `Enter`/`Create` and the lookup in `GetValue` consult **different sources**:

- `Create` checks only the *thread* context (`ThreadRuntimeContext.GetOrCreateCurrent().GetScope(key)`).
- `GetValue` checks the *current job's captured context first*, then the thread context:
  ```cs
  JobRuntimeScope? scope = Job.Current?.RuntimeContext.GetScope(key);
  scope ??= ThreadRuntimeContext.Current?.GetScope(key);
  ```

Inside a job that *inherited* key K through its captured `JobRuntimeContext`, entering K again succeeds (thread context doesn't have it) — but `GetValue(K)` still resolves to the inherited outer value. The freshly entered scope is silently shadowed. This breaks the documented contract ("`Enter` returns `JobRuntimeScope.Null` if the key already exists") and silently breaks nested `CorrelationIdScope`s.

### Affected code

File `Projects/Threading/JobRuntimeScope.cs`, private method `Create`. Anchor:

```cs
// get or create thread runtime context to store the instance of JobRuntimeScope object
ThreadRuntimeContext context = ThreadRuntimeContext.GetOrCreateCurrent();

// check if it's already within the scope
JobRuntimeScope result = context.GetScope(key);
```

### Fix — Option A (recommended, matches documented contract)

Make `Create` reject keys that are visible through the *same* resolution chain `GetValue` uses. Insert before the thread-context check:

```cs
// reject keys already visible through the current job's captured context,
// so that Enter() and GetValue() agree on what "already exists" means
if (Job.Current?.RuntimeContext.GetScope(key) != null)
	return Null;
```

(`JobRuntimeContext.GetScope` returns `null` when the key is absent.) Callers already must check `IsNull` per the documentation, so returning `Null` here is contract-compliant.

### Fix — Option B (alternative, true nesting)

Make `GetValue` prefer the thread context over the job context (swap the two lookups). This would let an inner scope genuinely shadow an inherited one during the current stage. It is a broader semantic change affecting every lookup, and interacts with how the method-builder cleans up scopes at stage boundaries — only choose this with the author's sign-off. **If unsure, implement Option A.**

### Verify (for Option A)

```cs
[Fact]
public void Enter_Inside_Job_That_Inherited_Key_Returns_Null_Scope()
{
	using JobRuntimeScope outer = JobRuntimeScope.Enter("K_F6", () => (Object)"outer");

	Boolean innerEnterSucceeded = false;
	String? observed = null;

	Job job = Job.Run(() =>
	{
		using JobRuntimeScope inner = JobRuntimeScope.Enter("K_F6", () => (Object)"inner");
		innerEnterSucceeded = !inner.IsNull;
		observed = JobRuntimeScope.GetValue("K_F6") as String;
	});
	Assert.True(job.WaitNoThrow(3000));

	// Enter and GetValue must agree: either Enter fails (Option A) ...
	Assert.False(innerEnterSucceeded);
	Assert.Equal("outer", observed);
	// ... or (Option B) Enter succeeds AND GetValue returns "inner". Never the mix.
}
```

---

## F7 — Scheduler queue priority is inverted relative to the stated intent

**Severity: Medium-High. Confidence: code-read + verified `PriorityQueue` ordering.**

### Problem

Jobs are enqueued with `priority = job.Depth` (depth 0 = root job, +1 per continuation level):

File `Projects/Threading/JobScheduler.cs`, inner class `JobsQueue`, anchor:

```cs
_queue.Enqueue(job, job.Depth);
```

.NET's `PriorityQueue<TElement, TPriority>` dequeues the **lowest** priority value first (verified: depth 0 dequeues before depth 2). So **continuations (deeper) sort behind fresh root jobs** — yet the comment in `RunJobSynchronously` (same file) claims the opposite:

```cs
// No need to take care of continuations here, those will be queued with higher priority upon submission
```

Consequence under load: in-flight work (continuation chains) starves behind newly submitted root jobs. When parents block waiting on children, this priority inversion increases the chance of pool exhaustion. (TPL uses LIFO local queues for nested tasks for exactly this reason.)

### Fix — Option A (recommended: make the code match the intent)

Deeper jobs should dequeue first:

```cs
_queue.Enqueue(job, -job.Depth);
```

`Depth` is non-negative, so negation cannot overflow. Run the full test suite afterwards — `JobSchedulerUnitTest_Performance` timings may shift slightly but should not regress.

### Fix — Option B (keep FIFO-by-depth, fix the lie)

If root-first is actually desired, correct the comment in `RunJobSynchronously` and document the starvation trade-off. In that case also consider replacing `PriorityQueue` + lock with a simple queue, since depth-ascending order then has no benefit over FIFO.

Pick A unless the author says otherwise.

---

## F8 — One `NotSupportedException` from `SynchronizationContext.Send` disables `Send` process-wide, and the fallback runs the continuation on the wrong thread

**Severity: Medium. Confidence: code-read (mechanism certain).**

### Problem

File `Projects/Threading/Job.cs`, method `ExecuteAwaitContinuation` + static flag. Anchors:

```cs
private static Boolean _supportsSynchronizationContextSend = true;
```
```cs
catch (NotSupportedException)
{
	// if SynchronizationContext.Send is not supported, fallback to direct call
	_supportsSynchronizationContextSend = false;
	_fnActionExecutorSendOrPostCallback(actionWithContext);
}
```

Two defects:

1. The flag is a **single process-wide static**. The first context that rejects `Send` (e.g. MAUI) permanently disables `Send` for *every* `SynchronizationContext` in the process — including ones that support it (WinForms, WPF).
2. The fallback invokes the callback **inline on the current (pool) thread**. The caller asked for the continuation to run on a specific `SynchronizationContext` (typically the UI thread); running it inline violates that and can crash UI code with cross-thread access errors.

### Fix

Replace the inline fallback with `Post` to the same context (honors the context, merely loses synchrony), and remember unsupported contexts **per context type** rather than globally:

```cs
private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, Boolean> _sendUnsupportedContexts = new();
```

```cs
if (runSynchronously)
{
	SynchronizationContext syncContext = configuration.SynchronizationContext;
	if (!_sendUnsupportedContexts.ContainsKey(syncContext.GetType()))
	{
		try
		{
			syncContext.Send(_fnActionExecutorSendOrPostCallback, actionWithContext);
			return;
		}
		catch (NotSupportedException)
		{
			_sendUnsupportedContexts.TryAdd(syncContext.GetType(), true);
		}
	}

	// Send is not supported by this context type - Post instead (still the right thread,
	// just asynchronous). Never invoke inline: that would run the continuation on the wrong thread.
	syncContext.Post(_fnActionExecutorSendOrPostCallback, actionWithContext);
}
```

Remove the old `_supportsSynchronizationContextSend` field. Note: the `Tuple`-based state already carries the job/thread context restoration, so `Post` reuses the same callback safely.

---

## F9 — Every `Job` carries a finalizer that does nothing

**Severity: Medium (performance). Confidence: code-read (mechanism certain).**

### Problem

File `Projects/Threading/Job.cs`, anchor:

```cs
~Job()
{
	Dispose(false);
}
```

`Dispose(Boolean disposing)` does nothing when `disposing == false` (its whole body is inside `if (disposing)`). So the finalizer has zero effect — but its mere existence puts **every job allocation on the finalization queue**: objects survive an extra GC generation and the finalizer thread must process each one. For a library whose selling point is TPL-class performance, this is measurable overhead on every job. (TPL's `Task` deliberately has no finalizer; the blog post already cited in the code comments — "Do I need to dispose of Tasks?" — makes the same point.)

### Fix

Delete the `~Job()` destructor. Keep `Dispose()`/`Dispose(Boolean)` as-is (the `GC.SuppressFinalize(this)` call in `Dispose()` becomes redundant but is harmless and keeps the standard pattern; removing it is optional).

Optionally do the same for `~JobSchedulerBase()` in `Projects/Threading/JobSchedulerBase.cs` — its `Dispose(false)` only sets a flag; schedulers are few so the impact is small, but the pattern is equally pointless there.

### Verify

Build succeeds; full test suite passes. For evidence of the improvement, run `JobSchedulerUnitTest_Performance` before/after and compare timings (expect equal or better).

---

## F10 — `Initiator` chain keeps every ancestor job alive (unbounded memory for self-continuing chains)

**Severity: Medium. Confidence: code-read. Requires a design decision — do not implement without author sign-off.**

### Problem

Every job holds a strong `Initiator` reference to its parent, forming a chain to the root (`Job.Root` walks it). References are never severed on completion. A workload that continuously chains continuations (e.g. a periodic job that re-schedules itself) accumulates an ever-growing chain of completed `Job` objects that can never be collected, and `Depth` grows monotonically (which also worsens F7's priority behavior).

File `Projects/Threading/Job.cs` — `Initiator` property, `Root`, `Depth`.

### Options

- **Option A (minimal, safe):** document the constraint clearly in `Projects/Threading/Readme.md` and the root `README.md`: "do not build unbounded continuation chains; each continuation roots its ancestors".
- **Option B (code change):** when a job reaches a terminal status (in the completion path, e.g. end of `ExecuteProcedureCore` and in F1's `FinalizeCancellation`), set `_initiator = null`. `Depth` is already stored separately, so queue priority is unaffected. **Breaking consequence:** `Initiator`/`Root` return `null`/`this` after completion — any user code reading them post-completion changes behavior, and `InvokeContinuationAction` passes `Initiator` into `ContinueWith` callbacks, so the clearing must happen only *after* continuations have executed.

Recommend shipping Option A now and considering Option B for a major version.

---

## F11 — `RegisterContinuation` executes user code while holding the `_continuations` lock

**Severity: Low-Medium. Confidence: code-read (mechanism certain).**

### Problem

File `Projects/Threading/Job.cs`, method `RegisterContinuation(Job, JobContinuationOptions)`. Anchor:

```cs
lock (_continuations)
{
	// ensure to have the continuation flag on the job
	job._executionOptions |= (Int32)extraOptions;
	_continuations.Add(job);

	if (_execContinuationsBaseIndex != -1)
	{
		...
		ExecuteContinuations();
	}
}
```

When registering a continuation on an already-completed job, `ExecuteContinuations()` runs *inside* the lock. Synchronous continuations execute arbitrary user code there. If that user code blocks on another thread that is itself trying to register a continuation on the same job, the process deadlocks. Running user code under an internal lock is a standing hazard.

### Fix

Move the execution outside the lock:

```cs
Boolean runNow;
lock (_continuations)
{
	job._executionOptions |= (Int32)extraOptions;
	_continuations.Add(job);
	runNow = _execContinuationsBaseIndex != -1;
	if (runNow)
		Debug.Assert(Status > JobStatus.Running);
}

if (runNow)
	ExecuteContinuations();
```

Why exactly-once execution still holds (important — do not skip this reasoning): pending continuations are handed out by `GetPendingContinuations`, which under the same lock returns only items *beyond* `_execContinuationsBaseIndex` and atomically extends the index. If the completing thread and the registering thread both call `ExecuteContinuations`, whichever takes the lock first claims the newly added item; the other finds nothing pending. This also protects the shared lightweight continuations used by `WhenAll`/`WhenAny` (whose status never leaves `Created`) — their exactly-once-per-registration guarantee comes from the base-index window, not from status checks.

---

## F12 — `JobSchedulerScope`: out-of-order disposal silently corrupts `IJobScheduler.Current`

**Severity: Low. Confidence: code-read.**

File `Projects/Threading/JobSchedulerScope.cs`. `Dispose()` anchor:

```cs
public void Dispose()
{
	if (Current == NewScheduler)
		Leave();
}
```

If two scopes are disposed out of order, the outer `Dispose` silently does nothing and the thread-static `Current` is left pointing at a stale (possibly disposed) scheduler forever. Also, `Leave()` throws `ArgumentException`, which is the wrong exception type for a state error.

**Fix:** in `Dispose`, add a debug assertion so misuse surfaces during development, keeping the lenient release behavior; and change `Leave()` to throw `InvalidOperationException`:

```cs
public void Dispose()
{
	if (Current == NewScheduler)
		Leave();
	else
		System.Diagnostics.Debug.Assert(false, "JobSchedulerScope disposed out of order - IJobScheduler.Current not restored");
}
```

---

## F13 — `Enqueue` of a long-running job mutates state before validating the configuration

**Severity: Low. Confidence: code-read.**

File `Projects/Threading/JobScheduler.cs`. `Enqueue` first sets the job to `WaitingForActivation` (adding it to `_jobsInPool` and counting it in statistics), and only then `StartLongRunningJob` throws when the scheduler has `MaxLongRunningThreads <= 0`. Anchor:

```cs
private void StartLongRunningJob(Job job)
{
	if (MaxLongRunningThreads <= 0)
		throw new NotSupportedException("jobScheduler does not support execution of long running jobs");
```

The exception leaves the job stuck in the pool dictionary and inflates `IncompleteJobs` forever (which also skews the thread-allocation heuristic).

**Fix:** validate *before* mutating. In `Enqueue`, before the `UpdateJobStatus(job, JobStatus.WaitingForActivation, JobStatus.Created)` call, add:

```cs
if ((job.CreationOptions & JobCreationOptions.LongRunning) == JobCreationOptions.LongRunning &&
	MaxLongRunningThreads <= 0)
{
	throw new NotSupportedException("jobScheduler does not support execution of long running jobs");
}
```

The check inside `StartLongRunningJob` may remain as a safety assert.

---

## F14 — `JobsQueue`: wrong exception type for a full queue; `ReaderWriterLockSlim` never disposed; wrong lock kind

**Severity: Low. Confidence: code-read.**

File `Projects/Threading/JobScheduler.cs`, inner class `JobsQueue`.

1. A full queue throws `OverflowException` (anchor: `throw new OverflowException("Overflow of pending jobs in JobScheduler")`) — `OverflowException` means arithmetic overflow in .NET. Throw `InvalidOperationException` with the same message instead (keep the message text so existing log searches still work).
2. The `ReaderWriterLockSlim _lock` is `IDisposable` but never disposed (it can hold an event under contention). And it is the wrong tool: both hot operations (`Enqueue`, `Dequeue`) take *write* locks, so reader/writer separation buys nothing over plain `lock` while costing more per operation.

**Fix (single change resolving both):** replace the `ReaderWriterLockSlim` with a private `readonly Object _lock = new();` and use `lock (_lock) { ... }` in `IsEmpty`, `Count`, `Enqueue`, `Dequeue`. This removes the undisposed resource and is faster. (If keeping the RWLS instead, make `JobsQueue` disposable and dispose both queue instances from `JobScheduler.Dispose`.)

---

## F15 — Statistics take a write lock 3+ times per job on the hot path

**Severity: Low (performance). Confidence: code-read.**

File `Projects/Threading/JobSchedulerStatistics.cs`, class `JobSchedulerStatisticsCalculator`. Every `Queued`/`Started`/`Succeeded`/`Canceled`/`Faulted` call takes a `ReaderWriterLockSlim` **write** lock — a scheduler-wide serialization point crossed at least three times per job lifecycle.

**Fix:** replace the lock with `Interlocked` operations on plain `Int32` fields, and build the snapshot struct on read:

```cs
private Int32 _jobQueued, _jobRunning, _jobSucceeded, _jobCanceled, _jobFaulted;
private Int32 _mbQueued, _mbRunning, _mbSucceeded, _mbCanceled, _mbFaulted;

public void Queued(Job job)
{
	if (!job.IsMethodBuilderResult)
		Interlocked.Increment(ref _jobQueued);
	else
		Interlocked.Increment(ref _mbQueued);
}
// Started: Decrement(queued) + Increment(running); Succeeded/Canceled/Faulted: Decrement(running) + Increment(respective)

public JobSchedulerStatistics JobStatistics
{
	get
	{
		return new JobSchedulerStatistics
		{
			QueuedJobs = Volatile.Read(ref _jobQueued),
			RunningJobs = Volatile.Read(ref _jobRunning),
			SucceededJobs = Volatile.Read(ref _jobSucceeded),
			CanceledJobs = Volatile.Read(ref _jobCanceled),
			FaultedJobs = Volatile.Read(ref _jobFaulted)
		};
	}
}
```

Trade-off to state in a code comment: snapshots become *per-counter* atomic rather than cross-counter consistent (a reader may see queued already decremented but running not yet incremented). For monitoring statistics this is acceptable; tests that assert exact totals after quiescence are unaffected. `Dispose` can become a no-op (keep the method for API compatibility).

---

## F16 — `WaitAll` with a timeout can wait almost twice the requested time

**Severity: Low. Confidence: code-read.**

File `Projects/Threading/Job.cs`, `WaitAll(Job[], Int32)` (and the token/`TimeSpan` variants). Each job in the loop is given the **full** original timeout instead of the remaining budget; the stopwatch check only runs *after* each wait. Worst case ≈ 2× the requested timeout. The final elapsed-time check can also return `false` when every job actually completed right at the boundary.

**Fix:** compute the remaining budget per iteration:

```cs
public static Boolean WaitAll(Job[] jobs, Int32 millisecondsTimeout)
{
	if (jobs.Length == 0)
		return true;

	Stopwatch sw = Stopwatch.StartNew();
	foreach (Job job in jobs)
	{
		Int32 remaining = millisecondsTimeout - (Int32)sw.ElapsedMilliseconds;
		if (remaining < 0)
			remaining = 0;
		if (!job.WaitNoThrow(remaining))
			return false;
	}

	return true;
}
```

(`WaitNoThrow(0)` returns instantly with the completion state, so all-completed-at-the-boundary now correctly returns `true`.) Apply the same pattern to the `(Job[], Int32, CancellationToken)` and `(Job[], TimeSpan)` overloads.

Related, document-only: `WaitAny` builds a `WaitHandle[]` of all jobs — `WaitHandle.WaitAny` supports at most 64 handles and will throw `NotSupportedException` beyond that. Either document the limit or chunk the wait; documenting is acceptable.

---

## F17 — `GetValue<T>(String key)` hard-casts while `GetValue<T>()` soft-casts

**Severity: Low. Confidence: code-read.**

File `Projects/Threading/JobRuntimeScope.cs`. The keyed generic overload does `return (T)value;` (throws `InvalidCastException` on a type mismatch) while the keyless overload does `as T` (returns `null`). The README documents null-on-not-found semantics.

**Fix:** make the keyed overload consistent:

```cs
public static T? GetValue<T>(String key)
	where T : class
{
	return GetValue(key) as T;
}
```

---

## F18 — `async Job` methods canceled via `OperationCanceledException` end up `Faulted`, never `Canceled`

**Severity: Low (TPL divergence). Confidence: code-read.**

File `Projects/Threading/JobMethodBuilder.cs`, both `JobMethodBuilder.SetException` and `JobMethodBuilderT<TResult>.SetException`. Anchor:

```cs
public void SetException(Exception exception)
{
	ResultJob job = GetOrCreateResult();

	job.AppendException(exception);
	job.UpdateStatus(JobStatus.Faulted);
```

TPL marks a task `Canceled` when an async method throws `OperationCanceledException`. Here it always becomes `Faulted`, so `IsCanceled` is `false` for canceled async jobs. Awaiters still observe an `OperationCanceledException` (it is rethrown as the single inner exception), so `try/catch (OperationCanceledException)` works — only the *status* diverges.

**Fix (both builders):**

```cs
public void SetException(Exception exception)
{
	ResultJob job = GetOrCreateResult();

	if (exception is OperationCanceledException)
	{
		// cancellation is not a fault (TPL parity): mark the job canceled, do not record an exception
		job.UpdateStatus(JobStatus.Canceled);
	}
	else
	{
		job.AppendException(exception);
		job.UpdateStatus(JobStatus.Faulted);
	}
	job.SignalCompletion();

	job.ExecuteContinuations();
}
```

Caveat: after this change, awaiting such a job surfaces cancellation through `ThrowIfCompletedUnexpectedly`'s `IsCanceled` branch (generic OCE without the original token). If preserving the original exception instance matters, discuss with the author first; otherwise this is the standard behavior.

---

## F19 — Cosmetics and small API nits (batch)

**Severity: Low. Confidence: code-read.** Each is a one-line change; do them together.

1. `Projects/Threading/JobScheduler.cs`, `Enqueue` and `Cancel`: `throw new ObjectDisposedException("this")` produces the message *"Object name: 'this'"*. Use `GetType().FullName`. Additionally, `Cancel` on a stopped scheduler would be friendlier returning `false` than throwing — optional, author's call.
2. `Projects/Threading/Job.cs`, `ToString()` returns `Procedure.Method.Name` — for lambdas this is compiler gibberish. Consider `$"Job {Id} [{Status}]"`. Optional.
3. `Projects/Threading/Job.cs`, `AsyncWaitHandleSlim` property getter: when two threads race the `Interlocked.CompareExchange` that lazily creates the wait handle, the loser's freshly allocated `ManualResetEventSlim` is never disposed. Dispose the loser:
   ```cs
   ManualResetEventSlim candidate = new(false);
   if (Interlocked.CompareExchange<ManualResetEventSlim?>(ref _waitHandle, candidate, null) != null)
       candidate.Dispose();
   ```
4. `Projects/Threading/Job.cs`, `Delay`: each delay parks a pool thread for the whole duration (`Thread.Sleep` / handle wait). Fine for the current design; a timer-based implementation would free the thread but requires the centralized completion path from F1. Document as a known limitation unless the author wants the larger change.

---

## F20 — Test-suite gap: zero cancellation coverage

**Severity: Medium (meta). Confidence: certain (verified by grep).**

`Projects/ThreadingTest` never calls `Cancel()` and every `CancellationToken` passed anywhere is `CancellationToken.None`. That is why F1–F4 went unnoticed. The existing suite's pattern (`JobSchedulerUnitTest_<Area>` classes comparing TPL `Task` vs `Job` behavior via `Helpers/Executor.cs`) should be extended with a `JobSchedulerUnitTest_Cancellation` class containing, at minimum, the verification tests from F1–F4 above, plus TPL mirror-assertions where applicable (the TPL behavior is the specification, per the project's own convention).

---

## Suggested commit grouping

1. **Commit 1 — cancellation correctness (F2, F3, F4, F1 + F20 tests).** These four form one behavioral unit; the new tests gate them.
2. **Commit 2 — WhenAll/WhenAny materialization (F5 + test).**
3. **Commit 3 — runtime-scope consistency (F6 + test).**
4. **Commit 4 — scheduler queue priority (F7)** — isolated so it can be reverted independently if throughput characteristics change.
5. **Commit 5 — Send fallback (F8).**
6. **Commit 6 — performance batch (F9, F14, F15, F16).**
7. **Commit 7 — small fixes batch (F12, F13, F17, F18, F19).**
8. **F10 and F11** each as their own commit after explicit author decision (F10 is a design choice; F11 changes locking behavior).

After each commit: `dotnet test Solution/Armat.Threading/Armat.Threading.sln -c Debug` must pass in full (the suite takes minutes; use `--filter` while iterating, full run before committing).
