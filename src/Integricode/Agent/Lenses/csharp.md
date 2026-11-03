# C# / .NET lens

Applies to: `*.cs`, `*.csproj`, `*.razor`, `*.cshtml` (skip `obj/`, `bin/`, generated: `*.g.cs`, `*.Designer.cs`, `Source/Orm/`).

## Before you start
- `<TargetFramework>`/`<LangVersion>`/`<Nullable>` (`.csproj`, `Directory.Build.props`): newer syntax/BCL (collection expressions, `ThrowIfNull`, `Dictionary.TryAdd`, `string.Contains(string, StringComparison)`, `Split(char)`) may not exist on target: flag code above LangVersion, never suggest blind
- `.editorconfig`: `none`/`silent` rules, `TreatWarningsAsErrors`; suppressed-rule findings = noise
- `.claude/rules/domain.md` (or equivalent): format contracts/invariants invisible in code
- Refactoring PR (all commits `refactor:`/`perf:`): read changed `.cs` whole (old bugs hide outside added lines), report as pre-existing, verify "no behavior change" on whole method
- Registration/naming/test-setup: read 3-5 unchanged siblings; one example isn't a convention, two+ are

## Hunt for

### Correctness & runtime
- `.Value` on `Nullable<T>`; `First()`/`Single()`/`ElementAt()` on possibly-empty sequence; dereferenced `FirstOrDefault()`: throws when empty (`Single()` also on duplicates); verify caller/guard
- `!` or `#pragma warning disable CS86xx` silencing compiler: still null, fails at runtime
- Dictionary indexer without `TryGetValue`: `KeyNotFoundException`
- Deferred LINQ enumerated twice or over variable changed before enumeration: query/side effect repeats; `Select` side effects never enumerated
- Collection modified in `foreach`; `Remove`/`RemoveAt` in forward `for`: `InvalidOperationException`/skipped items
- `switch`/`if` over enum/hierarchy that gained member in this PR: grep other consumers (no `default` = silent no-op); `(MyEnum)intValue` on unvalidated input accepts undefined
- String comparison without `StringComparison` (`StartsWith(string)`, `IndexOf(string)`, `Compare`, `ToLower() == x`): locale-dependent; same for number/date `Parse`/`ToString` across DB/file/wire without `CultureInfo.InvariantCulture`; `DateTime.Now` vs `UtcNow` when stored/compared
- Arithmetic: `int` overflow on sizes/sums/ticks (unchecked), `Math.Abs(int.MinValue)`, integer division where `decimal` meant, `double` money, `Substring(s.IndexOf(x))` on -1, `<` vs `<=` in paging/ranges
- `Equals` without `GetHashCode`, mutable field in dictionary-key `GetHashCode`: lookups miss; `==` on `object` operands compares references
- Hidden failure: empty `catch {}`; `catch (Exception)` returning default (looks like no data); `throw ex;` (use `throw;`, keeps stack); `finally` that can throw, masking original
- Multi-segment formats (`"type:id:version"`): doc says N segments, code reads fewer (`parts[1]`, `parts[2]`, never `parts[0]`) -> logic bug; cross-check `domain.md`
- Shared helper that excludes/skips/filters values, 2+ call sites: check each; right for one may drop data in another
- Comment wrong about runtime behavior (says `null`, tests `Count == 0`): flag; not vague/missing ones

### Async & concurrency
- `.Result`, `.Wait()`, `.GetAwaiter().GetResult()` on request/UI path: deadlock under `SynchronizationContext` (classic ASP.NET/System.Web, WinForms/WPF), thread-pool starvation on ASP.NET Core; low priority in `Main`, tests, startup. ehs-v5: also sync ORM/file I/O in new `async` methods
- `async void` outside event handler: exception escapes/crashes process; same for async lambda where `Action` expected (`list.ForEach(async x => ...)`, `Parallel.ForEach(..., async ...)`), caller also continues early
- Un-awaited Task (`SaveAsync(x);`, `_ = DoAsync();`): compiler warns only inside async methods; races response/disposal, exception lost; fire-and-forget on scoped services (`DbContext`, `HttpContext`) -> `ObjectDisposedException`
- `return task;` not `return await task;` inside `using`/`try/catch`: early dispose, `catch` blind
- One `DbContext`/connection/`SqlCommand` across concurrent tasks (`Task.WhenAll` on shared instance): throws/corrupts; unthrottled `Task.WhenAll` over thousands exhausts pool
- Shared mutable state on multi-threaded server: static fields written per request, `Dictionary`/`List` across threads, `ConcurrentDictionary` check-then-act (`ContainsKey`->`TryAdd`; `GetOrAdd` factory may run twice, keep side-effect free), `lock(this)`/`lock(typeof(T))`/`lock("literal")`, double-checked init without `volatile`/`Lazy<T>`
- Captive dependency: singleton injecting scoped/transient-disposable (`DbContext`): stale instance
- `SemaphoreSlim.WaitAsync()`/`Monitor.Enter` without release in `finally`: one exception wedges all callers
- `CancellationToken` accepted, not forwarded (CA2016): work outlives disconnect; only where overload exists and call is long-running

### Resources & disposal
- `IDisposable` without `using`/`try-finally` (`SqlConnection`, `SqlCommand`, `SqlDataReader`, `Stream`, `StreamReader`, `HttpResponseMessage`, `CancellationTokenSource`, `Timer`, `Process`; ehs-v5 data layer: adapters, connections, readers): leaks on early return/throw; connections end as "Timeout expired ... max pool size"
- Use after dispose: `IQueryable`, lazy `IEnumerable`, `DataReader`, `Stream` returned out of its `using`/context
- `new HttpClient()` per call/iteration: socket exhaustion, stale DNS; use `IHttpClientFactory`/shared instance (disposing a shared client is also a bug)
- `+=` on long-lived/static publisher without `-=`, undisposed timers, ever-growing static collections/caches: slow leak, app-pool recycle
- Class owning `IDisposable` fields without `IDisposable`; `Dispose` that can throw
- Test infrastructure: `WebApplicationFactory<T>`, `HttpClient`, `TestServer` never disposed (in `new WebApplicationFactory<T>().WithWebHostBuilder(...)` the parent needs disposal too); siblings using `IClassFixture<T>` -> no hand-rolled factories

### Data access (ADO.NET / EF / any ORM)
- Query, `FindAsync` or lazy navigation in a loop: 1+N round trips; missing `Include`/prefetch/projection; timeout at prod volume. ehs-v5/LLBLGen: non-prefetched related-entity/collection property = query per access (N+1 in a loop/repeated getter); look for `PrefetchPath` or bulk fetch
- Early materialization: `ToList()`/`AsEnumerable()` before `Where`/`Select`/`Count`/`Any`, or `Func<T,bool>` instead of `Expression<Func<T,bool>>`: filter runs in memory over whole table
- Writes without transaction boundary: dependent `SaveChanges`/commands that must succeed together; `TransactionScope`+`await` without `TransactionScopeAsyncFlowOption.Enabled`; missing `scope.Complete()` (silent rollback); `catch` swallowing error then committing partial work; no rollback on error path. ehs-v5: every atomic entity/query enrolled in same transaction/adapter, every path ends in commit or rollback, transaction disposed
- Read-modify-write without concurrency token (`[Timestamp]`/rowversion, `UPDATE ... WHERE version = @v`): silent lost update
- `Contains` over huge list, `SaveChanges` in loop, full entities for one column: parameter limit (2100 on SQL Server), round trips, memory; `AsNoTracking` only for large read-only sets

### Performance that matters (hot paths and scale only)
- `list.Contains`/`FirstOrDefault` on another list in a loop: O(n^2); use `HashSet`/`ToDictionary`/`ToLookup` when n is realistically thousands
- `string +=` in loop, `new Regex(...)` per call, uncached reflection (`GetProperty`, `Type.GetType`) on hot path
- `new JsonSerializerOptions { ... }` in a method (CA1869): rebuilds metadata cache; hoist to `static readonly`; real in request paths, not startup/DI registration
- Whole file/response in memory (`File.ReadAllBytes`, `ReadAsStringAsync`, `MemoryStream.ToArray()`) for unbounded input: LOH >85 KB; stream
- Wide `lock` around I/O, `ReaderWriterLockSlim`-shaped workloads on plain `lock`, large mutable structs by value (mutates a copy), non-generic collections of value types (boxing): only on a stated hot path

### Security sinks in .NET (generic OWASP lives in the security lens)
- SQL built with `+`, `$""`, `string.Format` into `SqlCommand.CommandText`, `FromSqlRaw`, `ExecuteSqlRaw`, `Database.SqlQuery`: parameterize or `FromSqlInterpolated`/`FormattableString`; dynamic `ORDER BY`/table/column names need allowlist
- `Process.Start` with `UseShellExecute = true` or `cmd /c`/`powershell -Command` + request data in `Arguments`: use `ArgumentList`; no user text in shell
- `Path.Combine(root, userInput)`: absolute 2nd arg discards `root`, `..` escapes; check `Path.GetFullPath(...)` starts with `root`+separator. Also zip-slip (`ZipArchiveEntry.FullName`), trusted `IFormFile.FileName`
- Unsafe deserialization: `BinaryFormatter`, `NetDataContractSerializer`, `SoapFormatter`, `LosFormatter`, `ObjectStateFormatter`, `JavaScriptSerializer` with `SimpleTypeResolver`, Json.NET `TypeNameHandling` != `None` without `SerializationBinder`, `XmlSerializer`/`DataContractSerializer` with `Type`/`KnownTypes` from input
- XML on untrusted input: `DtdProcessing.Parse` or non-null `XmlResolver` on `XmlReader`/`XmlDocument`/`XDocument.Load` (XXE); `SelectNodes`/`XPath`/`DirectorySearcher.Filter` built by concatenation
- Crypto: `MD5`/`SHA1`/`DES`/`TripleDES`/`RC2` for passwords/signatures/encryption; `CipherMode.ECB`; hard-coded key/IV; `Random`/`Guid.NewGuid()` for tokens (use `RandomNumberGenerator`); `Rfc2898DeriveBytes` low iterations; secrets compared with `==`/`SequenceEqual` (use `CryptographicOperations.FixedTimeEquals`)
- TLS validation off: `ServerCertificateValidationCallback = (...) => true`, `DangerousAcceptAnyServerCertificateValidator`, custom handler always true; JWT `ValidateIssuerSigningKey`/`ValidateLifetime`/`ValidateAudience = false` or `RequireSignedTokens = false`
- ASP.NET exposure: new action/endpoint missing siblings' `[Authorize]`/`.RequireAuthorization()`; new `[AllowAnonymous]`; state-changing action without `[ValidateAntiForgeryToken]` or with `.DisableAntiforgery()`; entity loaded by route/body id, unscoped to user/tenant (`db.Orders.Find(id)`); entity/domain model bound directly (`[FromBody] Entity`, `TryUpdateModel`) not DTO (overposting); `Redirect(returnUrl)` without `Url.IsLocalUrl`; CORS `SetIsOriginAllowed(_ => true)` or `AllowAnyOrigin` with credentials
- Output encoding: `@Html.Raw(x)`, `MvcHtmlString.Create`/`HtmlString` over user data, `Response.Write`, `Literal.Text`/`InnerHtml`, WebForms `<%= %>` (`<%: %>` encodes), `<script>var id = '@Model.Id'</script>` (HTML encoding != JS encoding; use `Json`/`JavaScriptEncoder`), `[ValidateInput(false)]`/`[AllowHtml]`
- `new Regex(pattern)` or static `Regex` over user text, no `matchTimeout`: ReDoS if pattern/input attacker-influenced
- Dynamic code: `Assembly.Load*`, `Activator.CreateInstance(Type.GetType(userString))`, `CSharpCodeProvider`, Roslyn scripting on user input
- Disclosure: `ex.ToString()`/`ex.Message`/stack trace in a response, `UseDeveloperExceptionPage` outside Development, `customErrors="Off"`/`debug="true"` in Web.config, request bodies/tokens/connection strings in logs, secrets in `appsettings*.json`/`Web.config`, interpolated log templates (`LogInformation($"...")`; also log forging)

### Roslyn / IDE-level cleanups (usually 🟢; cite rule ID, one comment listing every occurrence)
- `ContainsKey`+indexer -> `TryGetValue` (CA1854; double lookup, racy if shared); `ContainsKey`+`Add` -> `TryAdd` (CA1864); `Count() > 0` -> `Any()` (CA1827; on a query too)
- Single-char string in `StartsWith`/`EndsWith`/`IndexOf`/`Split` -> `char` overload (CA1865/CA1866); `ToLower()`/`ToUpper()` for comparison -> `StringComparison.OrdinalIgnoreCase` (CA1862; correctness issue when culture matters)
- Array literal in loop/hot method -> `static readonly` field (CA1861); `new JsonSerializerOptions` per call (CA1869)
- Private/internal member typed as interface but always one concrete type (CA1859); instance member using no instance state -> `static` (CA1822). Breaking/reflection-visible on public API; say so
- Guard patterns -> throw helpers: `ArgumentNullException.ThrowIfNull` (CA1510, .NET 6+), `ThrowIfNullOrEmpty`/`ThrowIfNegative` (CA1511/CA1512, .NET 8+). Collection expressions `[a, b]` / `[.. x]` (IDE0300/IDE0301) only on C# 12
- `x != null ? x.P : null` -> `x?.P`; `new T(); t.P = v;` -> object initializer; `default(T)` -> `default`; `$"{x.ToString()}"` -> `$"{x}"`; `foreach` with lone `if` -> `.Where()`
- `.GetType() == typeof(T)` -> `is T` changes meaning (exact vs derived); suggest only if subclasses impossible or wanted

## Don't flag
- Rules `.editorconfig` sets to `none`/`silent`, generated code, suggestions target framework/`LangVersion` can't compile
- Missing `ConfigureAwait(false)` in ASP.NET Core/app code (matters only in libraries under a `SynchronizationContext`)
- Micro-optimizations off hot path (constructors, startup, DI registration, tests, one-time init); `string.Format`/interpolation style in cold code
- "Missing null check" that caller, guard clause or nullable annotation covers; open caller first
- MD5/SHA1 for cache keys, ETags or dedupe; `Random` for jitter or sampling; `AddWithValue` on an already-parameterized query
- Best-effort cleanup that deliberately swallows (temp-file delete, `Dispose`, logging) with stated intent
- Convention drift with <2 sibling examples, justified in PR description, or new code clearly better than legacy (🟢 `note` at most)
- Vague/incomplete comments; naming and `var` preferences `.editorconfig` doesn't enforce

## Typical light
- 🔴 `.Result`/`.Wait()`/`GetAwaiter().GetResult()` on any request path (deadlock on System.Web/UI, thread-pool starvation on ASP.NET Core); query/lazy relation in loop on growing data; whole result set or export buffered in memory with no limit, paging or streaming; per-call `HttpClient` (socket exhaustion); O(n^2) at realistic n; leaking subscription/cache that grows with uptime; user-facing text or files that garble characters (CSV for Excel without UTF-8 BOM, wrong `Encoding`); culture-sensitive parse/format/comparison of persisted or displayed values (dates, decimals); SQL from request data; unsafe deserialization (`BinaryFormatter`/`TypeNameHandling`); dropped `[Authorize]` or id without owner/tenant scope; partial commit after swallowed error; unguarded `First()`/`.Value`/indexer on main path with realistic empty data; un-awaited/`async void` write loss; undisposed request-path connections; `GetSingle`+`FaultAction.Ignore` hiding duplicates; changed behavior no test covers; leftovers of the change (members it made unused, commented-out code, `Console.WriteLine`/`Debug.WriteLine`, comments it made wrong)
- 🟡 per-call `JsonSerializerOptions`; `throw ex;`; unforwarded `CancellationToken` on long operation; undisposed test factory; new endpoint missing sibling attributes without security effect; pre-existing bug in refactoring PR
- 🟢 Roslyn/IDE rule hits; redundant casts/`default(T)`; naming/registration drift, no functional effect
