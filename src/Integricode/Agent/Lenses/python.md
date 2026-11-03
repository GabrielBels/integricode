# Python lens

Applies to `*.py`, `*.pyi`, `pyproject.toml`/`requirements*.txt`/`Pipfile` (dependency checks). Skip `venv/`, `.venv/`, `__pycache__/`, `*.egg-info/`, `dist/`, `build/`, `.tox/`, generated migrations unless hand-edited.

## Before you start
- Python version (`.python-version`, `requires-python`, CI matrix) gates bugs: `datetime.utcnow()` deprecated 3.12, `asyncio.get_event_loop()` outside running loop, `zip(strict=)` 3.10, `TaskGroup` 3.11, `tarfile` `filter=` 3.12/backport.
- Project type (Django, Flask, FastAPI, Celery, pandas, CLI, library): framework findings (N+1, CSRF, missing `Depends`) outrank generic.
- Pydantic v1/v2, SQLAlchemy 1.x/2.x differ (v2: `Optional[int]` w/o default is required; `.dict()`/`@validator`/`orm_mode` are v1).
- Skip CI-enforced checks (`ruff`, `flake8`, `bandit`, `mypy`, `.pre-commit-config.yaml`).

## Hunt for
### Runtime traps that give wrong results
- Mutable default `acc=[]`/`{}`/`set()`, import-time default (`ts=datetime.now()`, `id=uuid4()`): shared/frozen at start.
- Late-binding closure in loop (`executor.submit(lambda: use(i))`, `signal.connect`, Celery factories): last value wins; `lambda i=i:`/`functools.partial`.
- Generator/`map`/`filter`/`zip`/`csv.reader`/file handle iterated twice: empty 2nd pass; bare `next(it)`: `StopIteration` (`RuntimeError` in generator).
- Truthiness on valid falsy `0`/`0.0`/`""`/`[]`: `if x:`, `x or default` (`timeout or 30` turns `0` into `30`), `if not qty`; want `is None`.
- `is` on int/str literals (CPython caching: passes tests, fails prod), `== None`, `== True`; `__eq__` w/o `__hash__` (unhashable).
- Mutation while iterating: `for k in d: del d[k]` (`RuntimeError`), `lst.remove(x)` in `for x in lst` (skips); list/dict as dict key/set member (`TypeError`); `d[k]` where `.get()`/`setdefault` meant.
- Class-level mutable (`items = []`) shared by instances; `__init__` returning value; `super().__init__()` skipped/misordered in multiple inheritance.
- Money/time: `float` currency (`Decimal` from `str`; `Decimal(0.1)` keeps binary error); naive vs aware `datetime` mixed (`TypeError`/silent offset); `timedelta.seconds` for `total_seconds()` (drops days).
- Text/bytes: `open()` w/o `encoding=` (cp1252 Windows, UTF-8 Linux CI); `str`/`bytes` mixed w/o decode; `%`-format arg-count mismatch (fails only on that branch); `s.lstrip("abc")` strips char set, not prefix (`removeprefix`).
- Empty input: `/` by possibly-`0` count, `max([])`/`statistics.mean([])` w/o `default=`, `lst[-1]`, `range(len(x) - 1)`, 1-based page times page size.

### Exceptions, resources, caching
- `except:`/`except BaseException` swallows `KeyboardInterrupt`/`SystemExit`; `except Exception: pass` (or log-and-continue) mid write/sync/payment leaves silent partial state. Skip bare-except if `ruff E722/BLE001` enforced.
- `raise e` vs `raise` re-roots traceback; `e` used after `except` fails (name deleted); `return`/`break` in `finally` discards in-flight exception.
- Unclosed `open()`, sockets, cursors, `Popen` outside `with`/`finally`; `tempfile.mktemp()` (race); `Popen(stdout=PIPE)` never read (deadlock); `subprocess.run` w/o `check=True`/return-code check.
- HTTP w/o `timeout=` (`requests.get(url)`, `urllib`): waits forever, hung upstream pins every worker; missing `raise_for_status()` before using body.
- `assert` as runtime validation (inputs, permissions): stripped under `python -O`.
- `lru_cache`/`cache` on instance methods (pins `self`, leaks; unhashable args raise), functions returning mutable objects (callers mutate cache), time/env/DB-dependent results (stale).
- Threads/processes: shared mutable state w/o lock; `ThreadPoolExecutor` futures never `.result()`ed (exceptions vanish); lambdas/closures to `multiprocessing` (unpicklable under spawn, default on Windows/macOS).
- Import-time side effects: network/DB/file I/O or hard env failures at module level; circular imports (`ImportError`/`AttributeError` on one entry path).

### Async (asyncio, FastAPI, aiohttp)
- Missing `await`: coroutine never runs (`RuntimeWarning`), is truthy (`if check_permission(user):` always passes).
- Blocking calls in `async def` (`requests.*`, `time.sleep`, sync DB driver/Django ORM w/o `sync_to_async`, big `open().read()`, CPU loops, `subprocess.run`) stall the loop; use `httpx.AsyncClient`, `asyncio.sleep`, `asyncio.to_thread`/`run_in_executor`. Plain `def` FastAPI endpoints run in a threadpool: check only `async def`.
- Fire-and-forget `asyncio.create_task(coro())` w/o stored reference: GC'd mid-flight, exception only at GC; keep a set or use `TaskGroup`.
- `asyncio.gather`: first exception propagates while siblings keep running unless `return_exceptions=True`; unbounded over thousands of items w/o `Semaphore` exhausts connections/rate limits; independent awaits in a loop run serially (latency only).
- `asyncio.run()` inside running loop; `threading.Lock` held across `await`; one `AsyncSession`/client shared by concurrent tasks (not concurrency-safe).

### Django / SQLAlchemy / data layer
- N+1: related object/reverse manager in loop, serializer, template or `__str__` (`order.customer.name`, `obj.items.all()`, nested DRF serializer) w/o `select_related`/`prefetch_related` (SQLAlchemy `joinedload`/`selectinload`): 50 rows = 51 queries.
- Queryset misuse: `len(qs)`/`if qs:`/`list(qs)[0]` vs `.count()`/`.exists()`/`.first()`; whole-table `.all()` in memory (`iterator()`); `.save()`/`.get()` in loop (`bulk_create`, `bulk_update`, `filter(pk__in=...)`).
- Read-modify-write races: `obj.n += 1; obj.save()` loses concurrent updates (`F("n") + 1`, or `select_for_update()` in `atomic`); `get_or_create`/check-then-insert w/o unique constraint; `save()` on stale instance overwrites other columns (`update_fields`).
- Missing `transaction.atomic` on multi-row writes; emails/Celery `.delay()`/webhooks inside transaction (worker reads uncommitted rows, fires for rolled-back change): use `transaction.on_commit`.
- `QuerySet.update()`, `bulk_create`, `bulk_update`, `delete()` skip `save()`, signals, `auto_now`, validation; `save()` skips `full_clean()`.
- `.get()` on user-reachable path w/o handling `DoesNotExist`/`MultipleObjectsReturned` (500); `.filter().first()` hiding duplicates that should error.
- SQLAlchemy sessions shared across requests/threads or not rolled back after error; lazy load on detached instance or in async (`DetachedInstanceError`, `MissingGreenlet`).
- Migrations: NOT NULL column w/o default/backfill on populated table; index w/o `Concurrently` on big table; `RunPython` w/o reverse; row-rewriting `RunPython`/`RunSQL` in schema migration (check repo rule).
- Pydantic/FastAPI: v1 and v2 APIs mixed in one model; `response_model` omitted so ORM objects leak internal fields; `Depends` generator never closing resource.

### Performance (only on a hot path or with scale evidence)
- Quadratic: `x in big_list` in loop (`set`/`dict`); nested loops matching lists (index one in dict); `s += part` over many items (`"".join`); `list.insert(0)`/`pop(0)` in loop (`deque`).
- `.read()`/`readlines()`/`json.load` on large inputs where streaming works; full list consumed once.
- pandas/NumPy: `iterrows()`/row-wise `apply(lambda)` in pipeline; `pd.concat` in loop (quadratic; `DataFrame.append` removed in pandas 2.0); chained assignment `df[df.a > 0]["b"] = 1` writes to copy (value lost); Python loops over arrays.
- Repeated identical expensive calls in hot path w/o `functools.cache`.

### Security sinks in Python
- SQL: `cursor.execute(f"...{x}")`/`%`/`.format`/`+`; Django `.raw()`, `.extra()`, `RawSQL`, `order_by(request.GET[...])`/`values(user_field)` (column injection); SQLAlchemy `text(f"...")`. Use bind params.
- Code/command execution: `eval`/`exec`/`compile` on non-literals; `os.system`, `os.popen`, `subprocess.*(shell=True)` with interpolated input (list args avoid shell, but leading-`-` user value is argument injection); `importlib.import_module(user)`, `getattr(mod, user)()`.
- Deserialization: `pickle.load(s)`, `dill`, `shelve`, `marshal`, `jsonpickle.decode`, `pd.read_pickle`, `torch.load` w/o `weights_only=True`, `yaml.load` w/o `SafeLoader` (also `Loader=yaml.Loader|FullLoader|UnsafeLoader`). Cache/queue/Redis payloads untrusted if anything else can write them.
- XML: `xml.etree`/`minidom`/`sax` on untrusted input (entity-expansion DoS), `lxml` with default entity/network settings; use `defusedxml` or `XMLParser(resolve_entities=False, no_network=True)`.
- Paths/archives: `open(base + user)`; `os.path.join(base, user)` discards `base` if `user` absolute; `send_file`/`FileResponse(user_path)`; no `.resolve()` + `is_relative_to(base)`; `zipfile.extractall`/`tarfile.extractall` (zip-slip; tar needs `filter="data"`); upload with raw filename (`secure_filename`/random), web-served root, type trusted from header.
- Templates/XSS: `render_template_string(user)`, `jinja2.Template(user)` (SSTI); Jinja `Environment` w/o `autoescape=True`/`select_autoescape` (off by default); `mark_safe`, `|safe`, `Markup(user)`, `format_html(f"...")`, `HttpResponse(f"<b>{x}</b>")`.
- SSRF: `requests`/`httpx`/`urlopen(user_url)` (webhooks, URL preview, import-by-URL, image fetch) w/o scheme/host allowlist blocking private ranges and `169.254.169.254`; redirects into internal hosts; `urlopen` accepts `file://`.
- AuthN/AuthZ: route/view w/o `@login_required`/`permission_classes`/`Depends(...)` (DRF defaults to `AllowAny` if `DEFAULT_PERMISSION_CLASSES` unset); object fetched by client id w/o owner/tenant filter (`get_object_or_404(M, pk=pk)`, ViewSet `queryset = M.objects.all()`); mass assignment via `Model(**request.data)` or serializer/ModelForm `fields="__all__"` (exposes `is_staff`); JWT `decode` w/o `algorithms=[...]`, with `verify_signature: False`, or w/o `exp`/`aud` checks.
- Secrets/crypto: hardcoded keys/passwords/tokens, literal `SECRET_KEY`; `random.*` for tokens/OTPs/reset codes (`secrets`); md5/sha1/plain sha256 for passwords (`argon2`/`bcrypt`/`scrypt`/`make_password`); `==` on MAC/token (`hmac.compare_digest`); ECB, DES/RC4, static IV; `verify=False`, `ssl._create_unverified_context`, `CERT_NONE`.
- Config/leakage: `DEBUG=True`, `ALLOWED_HOSTS=["*"]`, `*_COOKIE_SECURE=False`, `CORS_ALLOW_ALL_ORIGINS=True` with credentials, `@csrf_exempt` on cookie-authenticated state-changing view, Flask `app.run(debug=True)` (Werkzeug console = RCE); logging passwords/tokens/PII/full request bodies; `str(e)`/traceback in response.

## Don't flag
- PEP 8, naming, import order, missing type hints (linter's job).
- Deliberate, documented mutable-default shared cache/registry.
- `except Exception` that logs traceback and re-raises, or at top-level boundary (worker loop, request handler); flag only swallow mid-write.
- `is` against `None`/`True`/`False`/enum members/sentinels; `==` for ints/strings.
- `shell=True`, `eval`, `pickle`, `yaml.load` on literal/constant/trusted internal artifact: flag only if untrusted data reaches it.
- `random` for jitter, shuffling, sampling, test data; md5/sha1 for cache keys, ETags, non-adversarial checksums (`usedforsecurity=False`).
- Missing `select_related` on single-object detail view or loop of few rows; micro-optimizations (local attribute caching, `list(map())`, `re.compile` in loop since `re` caches, `.keys()` iteration); missing index w/o evidence of table size/query frequency.
- Missing auth/CSRF decorator when a layer above enforces it (middleware, gateway, `DEFAULT_PERMISSION_CLASSES`): read `settings.py`/middleware/app factory first.
- Tests, fixtures, notebooks, one-off scripts unreachable from untrusted input (test credentials, `assert`, `shell=True`).

## Typical light
- 🔴 SQL/command/template injection from request data; unpickle/unsafe `yaml.load` of untrusted bytes; missing authn/ownership check; missing `await`/blocking call on async request path; outbound HTTP without `timeout=`; N+1 on data that grows; whole queryset/list or export loaded into memory with no limit, paging or streaming; `lru_cache` on a method (keeps every `self` alive); lost update/no `atomic` on money/stock/status; mutable default/late-binding closure giving wrong results on normal path; swallowed mid-write exception; files or text for users written without `encoding=`; naive datetimes compared, stored or shown across time zones; committed secrets; `verify=False`/`DEBUG=True` in prod; changed behavior no test covers; leftovers of the change (`print`/`breakpoint()`/`pdb`, commented-out code, functions it made unused).
- 🟡 N+1 on a list that stays small; side effects outside `on_commit`; `save()` w/o `update_fields`; quadratic loop at moderate n; generator exhausted on rare branch; bare `except` w/o data impact.
- 🟢 `raise e`/missing `raise ... from`; `== None`; micro-optimizations; harmless unused defensive code; naming.
