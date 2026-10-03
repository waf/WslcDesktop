# Spike S1: `wslc.exe` CLI contract and sessions

- **Date:** 2026-10-03
- **Tested build:** `wslc 3.0.1.0` (`C:\Program Files\WSL\wslc.exe`), session manager 3.0.1, kernel 6.18.40.1-1, Windows 10.0.26300.
- **Session:** the CLI default session (no `--session`), display name `wslc-cli-will`.
- **Fixtures:** `tests/fixtures/cli/`. Each fixture is the raw stdout, byte for byte. Error fixtures (`error-*.txt`) are an annotated capture: argv, exit code, stdout, then stderr.
- **How output was captured:** stdout was redirected to a file or pipe, so it was **not a TTY**. This is how the GUI will run the CLI. Timings marked "PS" come from PowerShell `Measure-Command`, 5 runs each. Other timings came from Git Bash and include roughly 40 ms of MSYS process overhead.
- **Source cross-check:** I cross-checked against microsoft/WSL. The local sparse clone is at `master` (after 3.0.2). I fetched tag `3.0.1` for the parts that differ, and where it matters this report says which one applies. Anything marked **(inferred)** comes from source and was not observed.

---

## 1. Summary table

| Command (argv after `wslc`) | Exit | Stream | Shape | Warm time (PS) |
|---|---|---|---|---|
| `list -a --format json` | 0 | stdout | **JSON Lines** (one compact object per line, CRLF). **Empty = 0 bytes**, not `[]` | 42 ms |
| `images --format json` | 0 | stdout | JSON Lines | 50 ms |
| `container inspect <id>...` / `image inspect` / `volume inspect` / `network inspect` / `inspect` | 0 | stdout | **JSON array**, pretty-printed (2-space indent, CRLF). `-f json` gives a single-line array | 33 ms |
| `stats --format json [ids...]` | 0 | stdout | JSON Lines. Empty = 0 bytes | **1.0–2.0 s** (running containers); 30 ms (stopped) |
| `info --format json` | 0 | stdout | single JSON object | 30 ms |
| `version --format json` | 0 | stdout | single JSON object | 22 ms |
| `volume list --format json` | 0 | stdout | JSON Lines | 28 ms |
| `network list --format json` | 0 | stdout | JSON Lines | 34 ms |
| `system session list` | 0 | stdout | **table only** (no `--format`) | 26 ms |
| `events` (3.0.1) | n/a (long-running) | stdout | **text lines only. 3.0.1 has no `--format`**; it was added after 3.0.1 | streams within ms |
| `logs [-t] [-f]` | 0 | **container stdout → stdout, container stderr → stderr** | raw bytes (LF) | ~70 ms |
| `pull <ref>` | 0 | stdout (progress), stderr (errors) | text lines, **block-buffered when redirected** | network-bound |
| `build ...` | 0 / 1 | **stderr** (progress), stdout empty | text lines, streamed promptly | n/a |
| `exec <id> cmd...` | the **command's own exit code** | stdout/stderr relayed | raw bytes | n/a |

`--format json` is available in 3.0.1 on `list`, `images`, `stats`, `info`, `version`, `volume list` and `network list`. It is **not** available on `events`, `system session list`, `logs` or `pull`. The `inspect` commands output JSON by default; there, `-f json` only switches to compact output.

---

## 2. Per-command details

### 2.1 `container list` (`list`, `ls`, `ps`)

Fixtures:
- `container-list-a.json`, `container-list.json`
- `container-list-a.no-trunc.json`, `container-list-a.size.json`
- `container-list-a.table.txt`, `container-list-a.no-trunc.table.txt`
- `container-list-q.txt`
- `container-list-filter.json` (`--filter status=exited`), `container-list-filter-name.json` (`--filter name=...`)
- `container-list-a.empty.json` (0 bytes), `container-list-a.empty.table.txt` (header only)

**Shape:** JSON Lines, newest first. This matches Docker's `ps --format json` (`ContainerOutputInformation` in `src/windows/common/wslc/models/ContainerModel.h`, not `wslc_schema.h`). **Every value is a string except `Platform`.**

| Field | Example | Notes |
|---|---|---|
| `ID` | `754ceed05ea0` | 12 hex characters. Full 64 with `--no-trunc` |
| `Names` | `wslcgui-s1-pg` | no leading `/` |
| `Image` | `postgres:16-alpine` | the reference as the user typed it |
| `Command` | `"\"docker-entrypoint.s…\""` | **wrapped in literal double quotes**. Truncated to 20 characters plus `…` (U+2026) unless `--no-trunc` |
| `CreatedAt` | `2026-10-03 10:37:36 +0700 GMT+7` | **local time**, second precision, with an odd zone suffix. Don't parse it; use inspect `Created` instead |
| `RunningFor` | `10 seconds ago` | relative English text |
| `State` | `running`, `exited` | lowercase. Only these two were observed; `created`/`paused`/`restarting` are expected to follow Docker **(inferred)** |
| `Status` | `Up 10 seconds`, `Exited (3) 9 seconds ago` | invariant English in JSON. The table form is localized (source comment in `ContainerTasks.cpp`) |
| `Ports` | `127.0.0.1:16379->6379/tcp` | Docker style, `""` if none. Multiple ports are probably comma-joined **(inferred)**. Ports default to binding on 127.0.0.1 |
| `Mounts` | `wslcgui-s1-vol` / `0db5a1c8646f04…,/mnt/{7484b990…` | comma-joined volume names or VM paths, truncated with `…`. **The order was not stable between two calls** |
| `LocalVolumes` | `"1"` | number as a string |
| `Networks` | `bridge` | |
| `Labels` | `com.microsoft.wsl.container.metadata={"V1":{...}},com.wslcgui.spike=s1` | comma-joined `k=v`. **This includes an internal label whose value is JSON containing commas**, so naive splitting breaks |
| `Size` | `0B`; with `-s`: `63B (virtual 294MB)` | decimal units |
| `HealthStatus` | `""` | |
| `Platform` | `{"architecture":"amd64","os":"linux"}` | object |

**Bonus:** the internal `com.microsoft.wsl.container.metadata` label holds typed port and volume data. For example:

```json
{"V1":{"Flags":0,"InitProcessFlags":0,
  "Ports":[{"BindingAddress":"127.0.0.1","ContainerPort":6379,"Family":2,"HostPort":16379,"Protocol":6,"VmPort":20003}],
  "Volumes":[{"ContainerPath":"/hostdocs","CreateSourceIfMissing":true,"HostPath":"D:\\Personal\\WslcGui\\docs","ParentVMPath":"/mnt/{guid}","ReadOnly":true,"SourceFilename":""}]}}
```

In this label, `Protocol` 6 means TCP and `Family` 2 means AF_INET. It is undocumented, so treat it as optional. Prefer `inspect`.

- **`-q`:** prints 12-character IDs, CRLF.
- **Table:** CRLF, UTF-8 (the `…` is UTF-8), columns padded with spaces.

### 2.2 `image list` (`images`)

Fixtures:
- `image-list.json`, `image-list-a.json`, `image-list-a.with-dangling.json`
- `image-list.no-trunc.json`
- `image-list.table.txt`, `image-list.digests.table.txt`

**Shape:** JSON Lines (`ImageOutputInformation` in `wslc/services/ImageModel.h`). All values are strings.

| Field | Example | Notes |
|---|---|---|
| `ID` | `320994c3b997` | 12 hex characters, no prefix. With `--no-trunc`: `sha256:<64 hex>` |
| `Repository` / `Tag` | `alpine` / `latest` | dangling images show `<none>` / `<none>` |
| `Digest` | `<none>` | **always `<none>` in JSON, even when a digest exists.** `--digests` in table mode does show it. Inspect `RepoDigests` has it |
| `Size` | `8.42MB`, `294MB` | decimal, human-readable. **Use inspect `Size` (bytes) for exact values** |
| `CreatedAt` | `2026-09-18 03:37:20 +0700 GMT+7` | local time, same odd format as list |
| `CreatedSince` | `2 weeks ago` | |
| `Containers` | `"2"` | count as a string |
| `SharedSize` / `UniqueSize` | `N/A` | |

### 2.3 `image inspect` and `container inspect`

Fixtures:
- `image-inspect.json`, `image-inspect.multi.json`
- `container-inspect.running.json` (named volume, user network, port)
- `container-inspect.running-bindmount.json` (Windows bind mount, anonymous volume)
- `container-inspect.exited.json`
- `container-inspect.multi.json`
- `container-inspect.compact.json` (`-f json`)

**Shape:** a JSON **array** (one element per id). Pretty-printed with 2-space indent and CRLF. Keys are alphabetical. The fields are exactly `InspectContainer` / `InspectImage` from `src/windows/inc/wslc_schema.h` (identical in 3.0.1 and master). This is a **reduced subset of Docker's inspect**.

**Container fields:**
- **Top level:**
  - `Id` (64 hex)
  - `Name` (**leading `/`**)
  - `Created` (RFC 3339, nanoseconds, UTC `Z`)
  - `Image` (`sha256:` image id; the reference is in `Config.Image`)
  - `Labels` (map)
  - `SizeRw`/`SizeRootFs`: only with `--size`
- **`State`:**
  - `Status` (`running`/`exited`), `Running` (bool), `ExitCode` (int)
  - `StartedAt`, `FinishedAt`: RFC 3339 nanoseconds. A running container has `FinishedAt` = `0001-01-01T00:00:00Z`
  - `Health`: `null` or `{Status, FailingStreak, Log[]}`
- **`Config`:**
  - `Image`, `Env[]`, `Labels`, `User`, `WorkingDir`
  - `Cmd[]`, `Entrypoint[]|null`
  - `StopTimeout: int|null`, `Healthcheck: obj|null`
- **`HostConfig`:** `NetworkMode`, `Memory` (bytes, 0 means unlimited), `NanoCpus`, `Ulimits[]`
- **`Ports`:** at the **top level**, not under `NetworkSettings` as in Docker. A map `"5432/tcp" → [{HostIp:"127.0.0.1", HostPort:"15432"}]`. **`HostPort` is a string.** A stopped container has `{}`.
- **`Mounts[]`:** `{Type: "bind"|"volume", Name, Source, Destination, ReadWrite}`
  - For a bind mount, `Source` is the **Windows path** (`D:\\Personal\\WslcGui\\docs`).
  - For an anonymous image volume (redis `/data`), `Name` is the volume id and `Source` is `""`.
- **`NetworkSettings.Networks.<name>`:** `{IPAddress, IPPrefixLen, Gateway, MacAddress, Aliases[], Links[], DriverOpts{}, IPAMConfig}`. For an exited container these are blank strings and 0.

**Image fields:**
- `Id`, `RepoTags[]`, `RepoDigests[]` (`repo@sha256:…`)
- `Created` (RFC 3339 nanoseconds Z), `Size` (**int bytes**)
- `Architecture`, `Os`, `Author`, `Comment`, `Parent`
- `Metadata.LastTagTime`
- `Config{Cmd, Entrypoint, Env, ExposedPorts, Labels, StopSignal, User, Volumes, WorkingDir}` (nullable members)
- `RootFS{Type, Layers[]}`

**Multiple ids:**
- One call accepts several ids and returns a single array (`container-inspect.multi.json`, `image-inspect.multi.json`).
- If **any** id is missing, the exit code is 1. stdout still holds an array of the ones that were found (`error-inspect-multi-partial.txt`), and stderr names the missing one.
- If the only id is missing, stdout is `[]`.

**Generic `wslc inspect`:** accepts any mix of object types (`inspect-generic.mixed.json`: container, volume, network and image in one array). There is **no type discriminator**, so the caller must infer the type from the shape. `--type volume <container>` returns `Object not found: …` with exit 1.

### 2.4 `stats`

Fixtures:
- `stats.json`, `stats-a.json`, `stats.multi.json`
- `stats.no-trunc.json`, `stats.stopped-explicit.json`
- `stats.table.txt`, `stats.empty.json` (0 bytes)

**Shape:** JSON Lines. The CLI computes these from Docker stats (`ComputeContainerStatsJson` in `ContainerTasks.cpp`).

| Field | Example | Notes |
|---|---|---|
| `ID` | 64 hex | **always full in JSON**. `--no-trunc` has no effect on JSON |
| `Name` | `wslcgui-s1-pg` | |
| `CPUPerc` | `0.31%` | string, 2 decimal places |
| `MemUsage` | `107.5MiB / 15.31GiB` | **binary units** (KiB/MiB/GiB) |
| `MemPerc` | `0.69%` | |
| `NetIO` | `1.25kB / 0B` | **decimal units** (rx / tx) |
| `BlockIO` | `46.8MB / 47.4MB` | decimal (read / write) |
| `PIDs` | `6` | **integer** (the only numeric field) |

**Which containers it covers:**
- By default, only running containers. `-a` adds stopped ones with all zeros (`MemUsage "0B / 0B"`, `PIDs 0`).
- Explicit ids are accepted, several in one call, including stopped ones.
- If an id is missing, exit 1 and no stdout.

**Latency:** a running container costs **1–2 s per call**. The CLI waits for a sample, and containers are sampled in parallel: 1 container took 1.0–2.0 s, 2 containers 1.9 s, 4 containers 2.0 s. A stopped container takes about 30 ms.

**There are no raw byte counters.** Graphs must parse human-readable strings, which lose precision (for example `1.25kB`).

### 2.5 `info` and `version`

Fixtures: `info.json`, `info.table.txt`, `version.json`, `version.table.txt`.

`info.json` is a single object:

```json
{"Client":{"Direct3DVersion":"…","DxCoreVersion":"…","KernelVersion":"6.18.40.1-1",
  "SettingsFile":"C:\\Users\\will\\AppData\\Local\\wslc\\settings.yaml","Version":"3.0.1.0","WindowsVersion":"10.0.26300.9457"},
 "Server":{"SessionManagerVersion":"3.0.1","Sessions":[{"CreatorPid":22452,"ID":1,"Name":"wslc-cli-will"}]}}
```

- `version.json` is `{"Client":{"Version":"3.0.1.0"}}`.
- **Neither command boots the VM**. Both take about 25–30 ms even when the VM is down.
- **`info --format json` is the machine-readable way to list sessions**, because `system session list` only prints a table.

### 2.6 Volumes

Fixtures: `volume-list.json`, `volume-list.table.txt`, `volume-list.empty.json` (0 bytes), `volume-inspect.json`.

- **`volume list --format json`:** JSON Lines (`VolumeOutputInformation`). All values are strings.
  - `Name`, `Driver` (`guest`), `Mountpoint` (path inside the VM), `Scope` (`local`)
  - `Labels` as a string. An anonymous volume shows `com.docker.volume.anonymous=`.
  - `Availability`/`Group`/`Links`/`Size`/`Status` are always `N/A`.
- **`volume inspect`:** an array of `{CreatedAt: "2026-10-03T03:37:33Z" (second precision), Driver, Labels: null|map, Mountpoint, Name, Options: null|map, Scope}`. This matches `InspectVolume` `to_json`: empty maps are emitted as `null`, and `Status` is omitted when empty.
- **"Used by" is not available here.** Get it by cross-referencing container inspect `Mounts[].Name`.

### 2.7 Networks

Fixtures: `network-list.json`, `network-list.table.txt`, `network-inspect.json`, `network-inspect.bridge.json`, `inspect-generic.json`.

- **`network list --format json`:** JSON Lines (`NetworkOutputInformation`).
  - `ID` (12 hex), `Name`, `Driver` (`bridge`/`host`/`null`), `Scope`, `Labels` (string)
  - `IPv4`/`IPv6`/`Internal` are the **strings `"true"`/`"false"`**.
  - `CreatedAt` is `2026-10-03 03:37:32.993374191 +0000 UTC`. This is a **different format from the container and image `CreatedAt`** (UTC with nanoseconds, Go `time.String()` style).
- **`network inspect`:** an array (`Network` in `wslc_schema.h`):
  - Flags: `Attachable`, `ConfigOnly`, `Ingress`, `Internal`, `EnableIPv4`, `EnableIPv6`
  - `ConfigFrom{Network}`, `Created` (RFC 3339 nanoseconds Z), `Driver`, `Id` (64), `Name`, `Scope`
  - `IPAM{Config[{Gateway, Subnet}], Driver, Options}`
  - `Labels{}`, `Options{}`, `Status{}`
  - `Containers{<64-hex id>: {EndpointID, IPv4Address: "172.18.0.2/16", IPv6Address, MacAddress, Name}}`
- **The default `bridge` network gets a new ID every time the VM boots.** I saw `090566631b33`, `776f04df0ed2`, `9530c244bc99` and `847306aa279f`. `host` and `none` kept the same IDs. Never key on the bridge network's ID.

### 2.8 `system session list`

Fixture: `session-list.txt`.

- Table only, CRLF: `ID   Creator PID   Display Name` / `1    22452         wslc-cli-will`.
- `Creator PID` is the first `wslc.exe` that created the session. That process is long gone.

### 2.9 `events` (`system events`)

Fixture: **`events.txt`**. I did not create `events.jsonl`, because 3.0.1 does not emit JSON. The fixture is a capture made while I created and started postgres, redis, the logger, debian and a foreground-exited container; ran `--rm` containers; and ran stop/start/restart/kill/rm, a network create, and the cleanup.

**3.0.1 format (observed):** text lines, CRLF, one event per line:

```
2026-10-03T10:37:33.000000000+07:00 container start 1246fd70…484c (com.wslcgui.spike=s1, image=postgres:16-alpine, name=wslcgui-s1-pg)
<local RFC3339, seconds only, fake .000000000> <Type> <Action> <Actor.ID> (<k=v, …> sorted by key)
```

- **`wslc events --format json` fails on 3.0.1** with exit 1: `Option name was not recognized for the current command: '--format'`.
- On `master` (after 3.0.2), `--format json` exists and emits Docker-shaped JSON Lines: `{Type, Action, Actor{ID, Attributes}, scope, time, timeNano, status, id, from}`. In 3.0.1 the `Event` schema has `time`, which master renamed to `timeNano`. **Plan for both formats.**

**Event types and actions observed:**
- `container`: `create`, `start`, `kill`, `stop`, `destroy`
  - **`stop` carries `exitCode=N`** and plays the role of Docker's `die`.
  - `kill` is emitted for both `wslc stop` (SIGTERM) and `wslc kill`, with no signal attribute.
  - `restart` shows up as `start`, `kill`, `stop`, then `start`. There are no `die`, `restart`, `exec_*` or `pause` events.
- `network`: `create`, `connect`, `disconnect`, `destroy`
  - A container on the default bridge produces `connect` on create and `disconnect` when it exits.

**Not observed at all:** volume create/rm, image pull/tag/untag/delete, build and `exec` produced **no events**. Image and volume pages therefore need polling.

**Attributes include the container's user labels** (`com.wslcgui.spike=s1`). A label value containing `, ` or `)` would make the text format ambiguous.

**Streaming behaviour:**
- The command stays open indefinitely, including when the session is idle (an open `events` client keeps the VM alive; see §4).
- Each line arrived 25–150 ms after the action that caused it, because the CLI calls `Flush` after every event.

**`--since`/`--until`:**
- Both accept epoch seconds or RFC 3339 (`2026-10-03T03:41:00Z`).
- With `--until`, history is replayed and the command exits 0 (about 110 ms).
- `--filter type=network` works.
- History only goes back to the current VM boot **(inferred)**: events from before an idle teardown were not checked.

### 2.10 `logs`

Fixtures:
- `logs-t.txt` (stdout, `-t -n 6`), `logs-t.stderr.txt`
- `logs-t.combined.txt`: `2>&1` into one **file**, which is corrupted. See the next list.
- `logs.txt`, `logs.stderr.txt`
- `logs-pg.txt` (empty), `logs-pg.stderr.txt`: postgres logs go to stderr

**Streams:**
- The container's stdout is relayed to `wslc` stdout and the container's stderr to `wslc` stderr. These are **raw bytes with LF line endings**, from two independent relays (`ContainerService::Logs` uses a `MultiHandleWait` with two `RelayHandle`s).
- **Without `-f`, all stdout is written first and then all stderr.** Order between the two streams is lost, so merge them by `-t` timestamp.
- Writing `2>&1` into a single **file** overwrote bytes, because the two CRT handles don't share a file position. With pipes this doesn't happen, but this harness must never use a shared file.

**`-t` and `-n`:**
- `-t` adds the prefix `2026-10-03T03:39:17.395973225Z ` (RFC 3339 nanoseconds, UTC, one space).
- `-n N` counts **lines across both streams**: `-n 6` returned 3 stdout lines and 3 stderr lines.
- **`-n 0` is rejected** (`Invalid tail option value: 0`, exit 1).
- `--details` adds a leading space when there are no details.

**`-f` through a pipe:**
- Lines arrived **30–40 ms** after the container wrote them, on both streams. There was no buffering delay.
- `logs -f` **exits by itself (code 0) when the container stops**.

**Encoding:** UTF-8 passes through unchanged (`ünïcødé ✓`).

### 2.11 `pull`

Fixtures:
- `pull.stdout.txt`, `pull.stderr.txt` (0 bytes)
- `pull-q.stdout.txt`
- `pull.uptodate.stdout.txt`, `pull.uptodate.stderr.txt`
- `pull.postgres.combined.txt`
- `error-image-pull-not-found.txt`

**When stdout is not a TTY:**
- Progress goes to **stdout** as plain lines, CRLF, with no VT codes and **no byte counts**.
- There is one line per **status change** per layer (`ImageProgressCallback::OnProgress`, non-VT branch, which deduplicates repeated byte-progress callbacks):

```
7-alpine: Pulling from library/redis
16333ee0c00f: Pulling fs layer
9928009b15c8: Waiting
83234986c742: Downloading
83234986c742: Verifying Checksum
83234986c742: Download complete
16333ee0c00f: Extracting
16333ee0c00f: Pull complete          (also seen: "<id>: Already exists")
Digest: sha256:858f…
Status: Downloaded newer image for redis:7-alpine      | "Status: Image is up to date for alpine:latest"
docker.io/library/redis:7-alpine      <- last line = fully-qualified reference
```

**Critical: when redirected, stdout is block-buffered by the CRT and pull never flushes.**
- Pulling `ubuntu:24.04` took 14.1 s, and **all 10 lines reached the pipe at +14.1 s**.
- So a pipe gives **no live progress** for typical pulls under 4 KB of output.
- By contrast, `events` calls `Flush` explicitly, and `logs` uses raw handle relays.

**Other `pull` behaviour:**
- `-q` prints only the reference.
- On error, stderr gets the registry message plus `Error code: WSLC_E_IMAGE_NOT_FOUND`, and the exit code is 1.

**With a TTY** (inferred from source): an in-place display using cursor movement, one line per layer, `<id>: Downloading [=====>    ] 12.3MB/45.6MB`, truncated to the console width. Getting that output would need a ConPTY and VT parsing.

### 2.12 `build`

Fixtures: `build.stdout.txt` (empty), `build.stderr.txt`, `build.plain.*`, `build.fail.*`.

- **All progress goes to stderr, CRLF**, and stdout stays empty:
  ```
  [1/2] FROM docker.io/library/alpine:latest
  [1/2] CACHED
  [2/2] RUN echo building && echo to-stderr >&2
    | to-stderr
    | building
  exporting to image
    | exporting layers
    | writing image sha256:…
    | naming to docker.io/library/wslcgui-s1-build:latest
  ```
- It **streams live**: step output arrived in real time (`sleep 2` steps showed up about 2 s apart). The CRT leaves stderr unbuffered.
- `--progress plain` looked the same in non-TTY mode.
- `--iidfile` writes `sha256:<64 hex>` with no newline.
- **On failure:** exit 1. stderr contains BuildKit's error block, then `Error code: E_FAIL`.

### 2.13 `exec`

Fixtures: `exec-stat.txt`, `error-exec-*.txt`.

- **The exit code is the command's own exit code** (`exit 7` → 7). stdout and stderr are relayed separately, as raw LF bytes.
- **No shell, and argv is passed literally**, so `/etc/*` is not expanded. Use `sh -c "…"`.
- **If the command is not found:** exit **126**, and the OCI error text goes to **stdout**, not stderr: `OCI runtime exec failed: exec failed: unable to start container process: exec: "/no/such/binary": stat …: no such file or directory: unknown`.

**`stat -c '%F|%s|%Y|%A|%N'` (S4 preview, `exec-stat.txt`):** the format works on both BusyBox (alpine) and GNU coreutils (debian), but **`%N` quoting differs**:
- **BusyBox** prints plain names (`/etc/hosts`) and quotes only symlinks (`'/etc/mtab' -> '/proc/mounts'`).
- **GNU** quotes every name (`'/etc/adduser.conf'`) using shell-escape quoting.
- **For S4:** use `%n` (unquoted) for the name and get symlink targets separately, or use a NUL-delimited format. A missing path gives exit 1 with `stat: can't stat` (BusyBox) or `stat: cannot statx` (GNU) on stderr.

### 2.14 Lifecycle commands

- **`run -d`** prints the 64-hex id on stdout.
- **Foreground `run`** relays the container's stdout and stderr and **exits with the container's exit code** (3, then 2 in a repeat test). A `--rm` container produces a `destroy` event.
- **`start`/`stop`/`kill`/`restart`/`rm`** print each name or id on its own line.
  - `rm -f -v` on 5 containers took 959 ms.
  - `rmi` prints `Untagged: …` and `Deleted: sha256:…` lines (`image-remove.txt`).
- **Idempotent:** `start` on a running container gives **exit 0** and prints the name. So does `stop` on a stopped one.

---

## 3. Errors

The process exit code is **1 for every CLI or engine error**. The exceptions are `exec` and foreground `run`, which pass through the container's code; `exec` also uses 126 when the command is not found. stderr text is CRLF. The general form is:

```
<human message>
Error code: <SYMBOL>
If this error was unexpected, please consider searching for existing issues or filing a new issue at https://github.com/microsoft/WSL/issues.
```

| Case (fixture) | stderr first line | `Error code:` |
|---|---|---|
| start / stop / logs / rm missing container | `Container 'x' not found.` | `WSLC_E_CONTAINER_NOT_FOUND` |
| `stats` missing container | **(empty line)** | `WSLC_E_CONTAINER_NOT_FOUND` |
| `container inspect` missing | `Container 'x' not found.` (stdout `[]`) | **none** |
| `inspect` (generic) missing / wrong `--type` | `Object not found: x` | none |
| `image inspect` missing | `Image 'x:latest' not found.` | none |
| `volume inspect` / `network inspect` missing | `Volume not found: 'x'` / `Network not found: 'x'` | none |
| `pull` nonexistent repo | `pull access denied for x, repository does not exist or may require 'docker login': …` | `WSLC_E_IMAGE_NOT_FOUND` |
| `run --pull never` missing image / `rmi` missing | `No such image: x:latest` | `WSLC_E_IMAGE_NOT_FOUND` |
| `rm` running container (no `-f`) | `Container '<64hex>' is running and cannot be removed. Either stop … or use forced remove (-f).` | `WSLC_E_CONTAINER_IS_RUNNING` |
| `exec` in a stopped container | `Container '<64hex>' is not running.` | `WSLC_E_CONTAINER_NOT_RUNNING` |
| `run --name` that is already used | `Conflict. The container name "/x" is already in use by container "…". …` | `ERROR_ALREADY_EXISTS` |
| `--session` unknown | `Session not found: 'x'` | `WSLC_E_SESSION_NOT_FOUND` |
| build step failed | BuildKit block | `E_FAIL` |
| bad option / bad value (`-n 0`) | `Option name was not recognized …` + blank line + usage + `Run 'wslc … --help'` | none |
| start already running / stop already stopped | n/a (**exit 0**) | n/a |

**Is there a machine-readable error code?** Partly. The `Error code: <SYMBOL>` line is reliable when present. It is missing for inspect not-found and for argument-parsing errors, so a fallback has to match message patterns (`not found`). Messages are probably localized **(inferred)**; the symbols are not.

---

## 4. Sessions, VM lifetime and cold start

All of the following was observed.

1. **Start state:** `system session list` printed only its header (no sessions). The first `wslc list -a --format json` took **2,694 ms** and created session `1 / wslc-cli-will` with creator PID 22452.
2. **The session outlives the CLI.** PID 22452 exited at once. The session (`wslcsession.exe`, same ID and name) was still there after every later command and at the end of the spike.
3. **Detached containers keep running after the CLI exits.** postgres, redis, the logger and debian ran for about 7 minutes across many short-lived `wslc` processes.
4. **The VM idle-terminates inside the session.**
   - With no running containers and no CLI operation in progress, the `vmmemwslc-cli-will` process went away **31–32 s** later (measured twice). The source default is `session.idleTimeout: 30` seconds (`WSLCUserSettings.h`; 3.0.1 `wslc.idl` has the same `IdleTimeoutSec` and idle-termination machinery).
   - The session object stays.
   - **The next command that needs the VM pays the cold start again: 2,612 ms and 2,649 ms.** The first `network create` of the spike also took about 2.7 s, because the VM had idled out after the pulls.
5. **An open `wslc events` keeps the VM alive.**
   - With no containers left and only `events` running, the VM stayed up for over 60 s (12 checks, 5 s apart).
   - After I killed the events process, the VM went down 32 s later.
   - The VM's working set was about **1.1 GB**.
6. **State that persists across VM idle teardown:**
   - Stopped containers (`Exited (0)` still listed after the VM went down and came back), named volumes, user networks and images all persist.
   - The default `bridge` network is recreated with a **new ID**.
7. **Commands that do not boot the VM:** `version` (24 ms), `system session list` (24 ms) and `info` (30 ms) all ran with the VM down, and the VM stayed down.
8. **Warm latency** (PS, 5 runs, average):

   | Command | Average |
   |---|---|
   | `version` | 22 ms |
   | `session list` | 26 ms |
   | `volume list` | 28 ms |
   | `inspect` | 33 ms |
   | `network list` | 34 ms |
   | `list -a` | 42 ms |
   | `images` | 50 ms |
   | `stats` (stopped) | 30 ms |
   | **`stats` (running)** | **1.0–2.0 s** |

---

## 5. Encoding and line endings

- **Text that `wslc` writes itself** (tables, JSON, errors, pull and build progress, events) is **UTF-8, no BOM, with CRLF line endings**.
  - It is written through `fwprintf` with the CRT in `_O_U8TEXT` mode when the stream is not a console (`Main.cpp: SetCrtEncoding(_O_U8TEXT)`, `OutputChannel.cpp`). So it does **not depend on the console code page**.
  - Pretty-printed inspect JSON also has CRLF inside it.
- **Container bytes** (`logs`, `exec`, foreground `run` output) are relayed **raw**: LF endings, whatever encoding the container wrote, with no normalization.
- `System.Text.Json` accepts CRLF whitespace. JSON Lines readers must trim `\r`.

---

## 6. `--no-trunc`

| Command | Default | `--no-trunc` |
|---|---|---|
| `list` | `ID` 12, `Command` 20 characters + `…`, `Mounts` items truncated with `…` | full ID, full command (inner quotes JSON-escaped), full mounts |
| `images` | `ID` 12 hex with no prefix | `sha256:` + 64 |
| `stats` (JSON) | `ID` already 64 | no change |
| `stats` (table) | `CONTAINER ID` 12 | n/a |

**Always pass `--no-trunc` for the data the GUI uses.** Truncate in the UI.

---

## 7. Other surprises

- **Five timestamp formats:**
  1. list/images `CreatedAt`: `2026-10-03 10:37:36 +0700 GMT+7` (local)
  2. network list `CreatedAt`: `2026-10-03 03:37:32.993374191 +0000 UTC`
  3. inspect and logs: RFC 3339 nanoseconds `Z`
  4. volume inspect: RFC 3339 seconds `Z`
  5. events: local RFC 3339 with offset and zeroed nanoseconds
- **Two kinds of booleans:** network list `"true"`/`"false"` strings, versus real bools in inspect.
- **Empty JSON Lines output is zero bytes**, not `[]`.
- **Testing harness pitfall (not a GUI issue):** Git Bash/MSYS rewrites arguments that start with `/` (`/no/such/binary` became `C:/Program Files/Git/no/such/binary`). Use `MSYS_NO_PATHCONV=1` when capturing fixtures from bash.
- **Ports bind to 127.0.0.1 by default** (`-p 15432:5432` → `127.0.0.1:15432->5432/tcp`).
- **Bind-mount sources are Windows paths** in inspect, and a VM path in the list `Mounts` column.

---

## 8. Implications for `WslcGui.Engine.Cli`

**DTOs**
- Use two DTO families:
  - **"Format" DTOs** for `--format json` lists: all-string Docker-template records (`ContainerListLine`, `ImageListLine`, `StatsLine`, `VolumeListLine`, `NetworkListLine`), plus `Platform` as an object and `PIDs` as an int.
  - **Inspect DTOs** that mirror `wslc_schema.h` exactly (`InspectContainer`, `InspectImage`, `InspectVolume`, `Network`).
- Mark every inspect member nullable. Several are `null` rather than empty (`Entrypoint`, `Healthcheck`, `Labels`/`Options` on volumes, image `Config.*`).
- Container `Ports` is top-level `Dictionary<string, List<PortBinding>>` with `HostPort` as a **string**.
- `Name` has a leading `/`; strip it.
- Add an `EventLine` parser for the 3.0.1 text format, and an `EventJson` DTO for later builds. Detect which to use from `version --format json`: use JSON when the version is ≥ the first release that accepts `events --format json`, or simply try `--format json` once and fall back on exit 1 plus "not recognized".

**Parsing strategy**
- Read stdout as UTF-8 and split JSON Lines on `\n`, trimming `\r`. Empty output means an empty list.
- Parse inspect as an array with source-generated `JsonTypeInfo<List<T>>`.
- **Use `list`/`images` only for the grid.** Get exact data (created time, byte sizes, ports, mounts, state, exit code) from a **batched `inspect id1 id2 …`**: one call for all ids, about 33 ms. Avoid parsing `CreatedAt`/`Size` strings from lists.
- Always pass `--no-trunc`.
- Parse port strings only as a fallback; prefer inspect `Ports`.
- For `Labels` in list output, don't split on commas. Use inspect `Labels`.
- Write unit parsers for human sizes, because stats has no raw counters:
  - binary units (`KiB`/`MiB`/`GiB`) for memory
  - decimal units (`B`/`kB`/`MB`/`GB`) for net and block I/O
  - `%` strings for CPU and memory percentages
- **Error mapping:**
  1. If stderr contains `Error code: (\w+)`, map `WSLC_E_*_NOT_FOUND` → NotFound, `WSLC_E_CONTAINER_IS_RUNNING` / `ERROR_ALREADY_EXISTS` → Conflict, `WSLC_E_CONTAINER_NOT_RUNNING` → NotRunning, `WSLC_E_SESSION_NOT_FOUND` → Unavailable.
  2. Otherwise match ` not found` → NotFound.
  3. Otherwise Unknown.
  4. Use the first non-empty stderr line as the message, and keep the full stderr for details.
  5. For multi-id inspect, still parse stdout on exit 1; it may hold partial results.
- `exec`: return the exit code as is. Treat 126/127 with an `OCI runtime exec failed` **stdout** line as "command not found".

**Process handling**
- Always read stdout and stderr on **separate pipes at the same time**, to avoid deadlock and because logs splits its streams.
- For logs, run `-t` all the time. Merge stdout and stderr by timestamp, then strip the prefix if the user hid timestamps.
- `logs -f` ends by itself when the container stops. Treat EOF plus exit 0 as "stream ended", not as an error.
- Never use `-n 0`; `-n 1` is the minimum. To get "only new lines", use `--since <now>`.
- The pipe output of `logs -f`, `events` and `build` is already prompt, so no PTY is needed for them.

**Refresh strategy**
- **Events:**
  - Run one long-lived `wslc events` for **container and network** state.
  - Map `create`/`start`/`stop` (with `exitCode`)/`destroy` and `network connect`/`disconnect` to targeted `inspect` calls, and debounce bursts (`restart` emits four events).
  - No image or volume events exist, so poll `images` and `volume list` (cheap, about 50 ms) every 5–10 s while those pages are visible, and also after any image or volume action the GUI performs itself.
- **Trade-off:** an open `events` process **keeps the VM (~1.1 GB) running indefinitely**.
  - **Recommended:** only run `events` while at least one container is running or while the main window is visible.
  - With nothing running, fall back to a slow poll and let the VM idle out.
  - Note that polling `list`/`images` itself **reboots** the VM (2.6 s) once it has gone down.
- **VM-free health checks:** use `info --format json` / `version` (about 25 ms). They don't wake the VM and they give the session list.
- **Show a "Starting engine…" state** when a call takes over about 500 ms. The first VM-needing call after the VM has been idle for about 30 s costs **about 2.6 s**.
- **Stats** take 1–2 s per call no matter how many containers.
  - Run **one `stats --format json` for all running containers** (no ids) on a 2–3 s loop, only while a stats view is visible.
  - Never run one process per container, and never let two stats calls overlap.
- Key containers by the 64-hex `Id` (not by name). Never key on the default bridge network's ID.

**Pull progress approach**
- Plain `pull` through a pipe gives **no live progress**: stdout is block-buffered until the process exits. Options, best first:
  1. **ConPTY:** run `wslc pull` inside a pseudo-console. The S3 ConPTY plumbing is needed for the terminal anyway. Parse the VT redraw lines `<id>: <status> [bar] cur/total` per layer, keyed by layer id, ignoring cursor-movement sequences. This also gives byte counts. Width is limited by the PTY column count, so use a wide PTY (for example 200 columns).
  2. **Pipe as a fallback:** run `pull` with an indeterminate progress bar and parse the final stdout on exit. The last line is the full reference; `Status:` tells "Downloaded newer" from "up to date".
  3. Later, a COM engine can use `IProgressCallback`, which is typed.
- **Pull cancel = kill the process** (Job Object). Check in a later spike that the service-side pull really aborts **(untested)**.
- **Build** is fine over plain pipes: read stderr line by line, with `[n/N]` step headers and `  | ` output lines. Use `--iidfile` to get the resulting id reliably.

---

## 9. Fixture index (`tests/fixtures/cli/`)

| Area | Files |
|---|---|
| Container list | `container-list-a.json`, `container-list.json`, `container-list-a.no-trunc.json`, `container-list-a.size.json`, `container-list-filter*.json`, `container-list-a.empty.json` (0 B), `*.table.txt`, `container-list-q.txt` |
| Images | `image-list.json`, `image-list-a.json`, `image-list-a.with-dangling.json`, `image-list.no-trunc.json`, `image-list*.table.txt`, `image-inspect.json`, `image-inspect.multi.json`, `image-remove.txt` |
| Container inspect | `container-inspect.running.json`, `.running-bindmount.json`, `.exited.json`, `.multi.json`, `.compact.json`; generic: `inspect-generic.json`, `inspect-generic.mixed.json` |
| Stats | `stats.json`, `stats-a.json`, `stats.multi.json`, `stats.no-trunc.json`, `stats.stopped-explicit.json`, `stats.empty.json` (0 B), `stats.table.txt` |
| Info, version, session | `info.json`, `info.table.txt`, `version.json`, `version.table.txt`, `session-list.txt` |
| Volumes and networks | `volume-list.json`, `volume-list.empty.json`, `volume-list.table.txt`, `volume-inspect.json`, `network-list.json`, `network-list.table.txt`, `network-inspect.json`, `network-inspect.bridge.json` |
| Events | `events.txt` (3.0.1 text format; no JSON available) |
| Logs | `logs-t.txt` + `logs-t.stderr.txt`, `logs.txt` + `logs.stderr.txt`, `logs-pg.txt` + `logs-pg.stderr.txt`, `logs-t.combined.txt` (shows the shared-file corruption; not for parser tests) |
| Pull | `pull.stdout.txt`, `pull.stderr.txt` (0 B), `pull-q.stdout.txt`, `pull.uptodate.*.txt`, `pull.postgres.combined.txt` |
| Build | `build.stdout.txt`/`build.stderr.txt`, `build.plain.*`, `build.fail.*` |
| Exec / S4 | `exec-stat.txt` (alpine and debian, with a `sh -c` glob and a literal glob) |
| Lifecycle | `remove-multi.txt` |
| Errors | `error-*.txt` (24 files; see §3) |

**Cleanup:** I removed every `wslcgui-s1-*` container, volume (including redis's anonymous volume, via `rm -v`), network and built image. I kept the pulled images: `alpine:latest`, `redis:7-alpine`, `postgres:16-alpine`, `debian:bookworm-slim` and `ubuntu:24.04`. No wslc settings were changed. There were no pre-existing containers, images, volumes or networks.
