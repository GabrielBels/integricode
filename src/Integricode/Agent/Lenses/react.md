# React lens

Applies to frontend `*.tsx`/`*.ts`/`*.jsx`/`*.js`/`*.css`/`*.scss`/CSS modules, `package.json`/lockfile; Playwright: last section. Skip `node_modules/`, `dist/`, `build/`, `.vite/`, `.next/`, `coverage/`, generated clients/types.

## Before you start
- React 18 vs 19: StrictMode double-invokes effects in dev; 19 passes `ref` as prop (`forwardRef` unneeded). React Compiler on (`babel-plugin-react-compiler`, `reactCompiler`): drop memoization findings.
- Skip hook-order/deps findings when CI ESLint runs `react-hooks/rules-of-hooks` + `exhaustive-deps`; check `tsconfig.json` strictness (`strict`, `noUncheckedIndexedAccess`).
- With TanStack Query/SWR/RTK Query/Apollo, fetching belongs there, not `useEffect`; plain `fetch` in effects makes race/cleanup findings real.
- UI library/build (PrimeReact, MUI, Tailwind, Vite `VITE_*` vs CRA `REACT_APP_*` vs Next `NEXT_PUBLIC_*`) set import, styling, secret rules.
- Read 3-5 sibling components to judge convention drift.

## Hunt for
### Hooks and effects
- Hook in `if`/loop/nested function/after early `return`/non-`use*` function: "rendered more hooks" crash on branch flip.
- Stale closure/missing deps: `useEffect`/`useCallback`/`setInterval` reads unlisted state/props (`[]` + `fetchForms(selectedCategoryCode)` never refetches); `setCount(count + 1)` vs `setCount(c => c + 1)`.
- Unstable deps (object/array literal, inline function) or effect setting state it depends on: infinite loop/fetch storm.
- No cleanup for `addEventListener`, `setInterval`/`setTimeout`, subscriptions, observers, websockets: StrictMode shows doubled listeners, double POSTs/analytics.
- Fetch race: effect fetch w/o `AbortController`/ignore flag, slow older response overwrites newer (search box).
- `async` passed to `useEffect`: Promise becomes cleanup; use inner async function.
- Derived state via `useEffect(() => setX(f(props)), [props])`: extra render, stale flash; compute in render. `useState(props.value)` ignores later prop changes (derive, or reset with `key`).
- Writing/reading `ref.current` for output in render; expecting ref change to re-render.

### State and rendering
- Mutation: `items.push(x)`, `state.obj.field = v`, `arr[0] = x` then `setState(sameRef)`; mutated props; in-place `.sort()`/`.reverse()`/`.splice()` on state/props (copy: `[...a].sort()`, `toSorted`): no update or one-render lag.
- Keys: index `key` on reordering/filtering/inserting list with stateful items (inputs, expanded rows, focus); missing/duplicate; `Math.random()`/`crypto.randomUUID()`/`Date.now()` (remount each render, state/focus lost).
- Component defined in component (`const Row = () => ...` in parent body, `<Parent.Child>` built per render): new type each render, remount, state lost.
- Falsy short-circuit: `{count && <X/>}` renders `0`/`NaN`; `{label && <span>}` hides legit `""`; use `count > 0 &&`/ternary.
- `data.items.map`/`user.profile.name` while query `undefined`/`null` or API field optional.
- Controlled/uncontrolled flip: `value={undefined}` then `value={x}`, or `value` w/o `onChange`: resets/warns.
- `onClick={handle(id)}` (runs in render) vs `() => handle(id)`; `<button>` in `<form>` w/o `type="button"` submits; submit w/o `preventDefault`; async submit w/o disabled/loading state (double submit).
- `onClick={async () => ...}` w/o `try/catch`: unhandled rejection, user sees nothing; show error state/toast per codebase pattern.

### Data fetching and TypeScript/JS pitfalls
- `fetch` doesn't reject on 4xx/5xx: no `res.ok` check before `res.json()` treats error body as data. `await res.json()` cast `as Foo` unvalidated; prefer schema (zod etc.) at boundary if repo has one.
- `forEach(async ...)`/`.map(async ...)` unawaited: errors lost, order undefined; `for...of` + `await` or `Promise.all`. Sequential `await`s of independent calls where `Promise.all` fits (latency).
- Casts hiding bugs: `as X`/`as unknown as X`/`any`/`@ts-ignore` over real mismatch; `!` on value really `undefined` at runtime (first render, optional API field, `Map.get`, `find`, `array[i]`).
- Dates/numbers: `new Date("2024-01-01")` is UTC but `getDate()` local (off-by-one west of UTC); `toISOString().slice(0, 10)` as local date; default `.sort()` on numbers lexicographic; `parseFloat`/`toFixed` for money; `JSON.parse` of storage/server text w/o `try/catch`.

### Performance (only on large lists, frequent re-renders, or bundle weight)
- Inline object/array/arrow to `React.memo` child or in 100+ row list defeats memo; Context `value={{ a, b }}` literal re-renders all consumers each provider render; fast-changing values (scroll, pointer, typing) in shared context re-render everything.
- Unvirtualized: over ~100-200 flat rows; PrimeReact `DataTable`/`TreeTable` over ~50 rows w/o `virtualScrollerOptions`; `TreeNode`/row arrays rebuilt in render instead of `useMemo`; inline `body` templates on big table.
- Bundle: `import { Button } from "primereact"` (use `primereact/button`), `import _ from "lodash"`, `import * as`, whole icon packs; heavy rare libs (editors, charts, maps, PDF, xlsx) eager vs `React.lazy` + `Suspense`/dynamic `import()` at route.
- Render cost: `useState(expensive())` (use `useState(() => expensive())`); filter/sort of thousands of rows per keystroke w/o `useDeferredValue`/debounce; full API response in state when a slice renders; fetch waterfalls (child after parent) that could be parallel.
- `window.addEventListener` outside effect.

### Accessibility, CSS and convention drift
- Unreachable by keyboard/screen reader: `div`/`span` `onClick` w/o role, `tabIndex`, key handler (use `<button>`); icon-only button w/o `aria-label`; `<img>` w/o `alt`; custom dialog not trapping/returning focus; `outline: none` w/o replacement focus style.
- CSS: library internals (`.p-datatable-*`) vs `pt`/theme tokens (breaks on upgrade); leaking global selectors (`button {}`, `.title {}`) where siblings use modules/BEM; `!important`, z-index escalation; hardcoded colors/spacing where design tokens exist; fixed `px` widths breaking narrow viewports.
- Convention drift (flag only if 2+ siblings set the pattern; quote one): named vs default export, props `interface` vs `type` and location, hook return shape (tuple vs object), sub-path imports, `body` templates outside JSX, shared dialog wrapper, error state/toast pattern, new context/state library where siblings drill props, camelCase vs kebab-case classes.

### Security sinks in React/TypeScript
- HTML injection: `dangerouslySetInnerHTML={{ __html: x }}` with `x` from user input, API field, URL param, uploaded file w/o `DOMPurify.sanitize`; `innerHTML`/`outerHTML`/`insertAdjacentHTML`/`document.write` via refs; markdown with HTML enabled (`rehype-raw`, raw `marked` output); chart/tooltip/rich-text libs with `html: true` or HTML-returning formatters; inline user-supplied SVG.
- URL sinks: `<a href={x}>`, `<iframe src>`, `window.location = x`, `window.open(x)`, `router.push(x)`, post-login `?redirect=`/`returnUrl` with user-controlled `x`: `javascript:`/`data:` URLs, open redirect. Parse with `new URL()`; allowlist `http(s):` or require relative path.
- Code sinks: `eval`, `new Function(str)`, `setTimeout("string")`, runtime `createElement("script")` with dynamic `src`.
- `postMessage`: `message` listener not checking `event.origin`; `postMessage(data, "*")` with sensitive data.
- Auth/JWT/refresh tokens newly in `localStorage`/`sessionStorage` (XSS-readable; prefer HttpOnly cookies; flag only if PR adds it); PII persisted client-side; tokens/PII in query strings, `console.log`, error toasts, logged payloads.
- Bundle secrets: API keys, tokens, hardcoded `Authorization` headers, private URLs in source; secrets behind `VITE_`/`REACT_APP_`/`NEXT_PUBLIC_` (inlined into public bundle).
- Requests: ``fetch(`/api/${userValue}`)`` w/o `encodeURIComponent` (path traversal to other endpoints); `credentials: "include"` cross-origin; `mode: "no-cors"` hiding CORS error; `rejectUnauthorized: false`/`NODE_TLS_REJECT_UNAUTHORIZED=0` in non-test code.
- Prototype pollution: deep merge/lodash `merge`/`set`/`Object.assign` of `JSON.parse` output or URL-derived keys (`__proto__`, `constructor`) into config/state.
- Next/SSR (when present): `getServerSideProps` data and server-to-client component props serialize into page (no secrets); server actions/route handlers need own authz.

## Don't flag
- Missing `memo`/`useMemo`/`useCallback` on cheap work, rarely rendered components; `useMemo` on trivial expressions.
- Hook-order/dependency-array findings ESLint enforces; refs listed in deps; index keys on static, never-reordered lists.
- `setState` after unmount as warning (removed in 18); flag only the stale-response overwrite.
- Non-sensitive UI preferences (theme, collapsed panels, last tab) in `localStorage`; `VITE_*` values public by design (API base URL, publishable key).
- `dangerouslySetInnerHTML` fed by constant or already through `DOMPurify`; authorization enforced only on backend, CSP/HTTPS (server layer), missing client-side authz checks.
- `any`/casts in tests, mocks, generated code; compile-time-only TypeScript errors.
- Convention differences backed by fewer than 2 siblings, legacy code the new code improves on, or explained in the PR; minor style w/o functional consequence.

## Typical light
- 🔴 XSS sink fed by user/API data; `javascript:`/open redirect; token/secret in storage, logs or bundle; conditional hook; effect loop/fetch storm; stale closure/fetch race showing wrong data in normal flow; component-in-component wiping form state; index keys on a list that reorders or filters; in-place state mutation (UI doesn't update); `res.ok` unchecked or async handler failing with no feedback to the user; control not reachable by keyboard or screen reader (`<div onClick>`, missing label); unvirtualized list on data that grows; text or downloads that garble characters or formats (CSV without BOM, dates/numbers in the wrong locale); changed behavior no test covers; leftovers of the change (`console.log`, `debugger`, commented-out JSX, components or props it made unused).
- 🟡 missing cleanup w/o visible symptom; eagerly bundled heavy path; barrel imports; library-internal CSS selectors; convention drift with maintenance cost.
- 🟢 import order, `interface` vs `type`, class naming, inline style objects on non-memoized children, naming.

## Playwright / e2e tests
Applies to `*.spec.ts`, `*.spec.tsx`, `tests/e2e/`, `e2e/`, `playwright.config.ts`. Read shared helpers/fixtures (`helpers/`, `fixtures/`) and config first; tests should fail only when the product is broken.

### Hunt for
- Assertions that cannot fail: missing `await` on `expect(locator)...` or `click`/`fill`/`goto` (floating promise, passes before check runs); `expect(page.locator("x")).toBeTruthy()` (Locator always truthy); `expect(await loc.isVisible()).toBe(true)` or `textContent()` snapshots (no retry); assertion guarded by `if (await el.isVisible())`; `try/catch` or `.catch(() => {})` around steps; `expect(true)`.
- Hard waits: `waitForTimeout(n)`, `sleep` helpers flake on slow CI, waste time on fast; wait for state (`await expect(loc).toBeVisible()`, `waitForResponse`, `toHaveURL`). `waitForLoadState("networkidle")` discouraged, hangs on polling apps. `force: true` clicks hide a real actionability bug.
- Brittle selectors: CSS classes, `nth-child`, XPath, `.first()`/`.nth(i)` on lists w/o guaranteed order; library-internal classes (`.p-treetable-row`, `.p-accordion-header`); exact copy text that changes with i18n. Prefer `getByRole`/`getByLabel`/`getByText`; `getByTestId` acceptable.
- Isolation: test depending on state left by earlier one; `beforeAll` mutating shared state or sharing one `page`; `describe.serial` where tests could be independent; created data not cleaned up or not uniquely named; mutated `storageState` reused across tests.
- Network: spec hitting real backend/external assets instead of `page.route()`/shared fixture (`routeMinimalConfig`-style) before `goto`; route mocks outliving the test.
- Committed `test.only`/`describe.only` (`forbidOnly` missing), `test.skip`/`test.fixme` w/o reason and ticket.
- Coverage gaps: happy path only (no error, empty, loading state); widgets (tree table, accordion, tabs, dialog) driven by mouse only, no `Tab`/`Enter`/`Space`/`Escape`; no check ARIA state follows interaction (`aria-expanded`, `aria-selected`, `toBeFocused()` after dialog open/close).
- Maintainability: identical `beforeEach`/`goto`/`waitForAppReady` blocks instead of fixture; same selector or color hex duplicated across specs; title not matching what body asserts.
- Config/secrets: `retries: 0` on constrained CI, `expect.timeout` so low it flakes (or per-assertion timeouts contradicting it), hardcoded `baseURL`/ports in specs, real credentials/tokens in specs or committed `storageState` files.

### Don't flag
- Style preferences not affecting reliability; `data-testid` where role selector would be no clearer; `.first()` over genuinely equivalent items; test-title wording w/o ambiguity; missing unit tests (belong with implementation).

### Typical light
- 🔴 floating promise or assertion that cannot fail; committed `.only`; new flow covered only on the happy path; `skip` without a reason (a test switched off); spec mutating real backend data or sharing state so order matters; real credentials in spec.
- 🟡 hard waits, `networkidle`, library-internal CSS selectors, unmocked network.
- 🟢 duplicated setup, title wording, repeated constants.
