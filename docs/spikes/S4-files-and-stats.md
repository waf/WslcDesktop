# S4: Container filesystem browsing, file copy and stats charts

Date: 2026-10-03. Environment: WSL/wslc 3.0.1, images alpine:latest (BusyBox) and debian:bookworm-slim (GNU coreutils).
Everything below was observed unless marked *(inferred)*.

## 1. Listing a directory in a running container

Command, run through `wslc container exec` with the `QUOTING_STYLE=literal` environment variable:

```
find <dir> -mindepth 1 -maxdepth 1 -exec stat -c '%F|%s|%Y|%A|%N' {} +
```

Output for a test directory, on both images:

```
directory|4096|1791004864|drwxr-xr-x|/tmp/t/sub dir
regular empty file|0|1791004864|-rw-r--r--|/tmp/t/pipe|name
regular file|100|1791004864|-rw-r--r--|/tmp/t/data
symbolic link|13|1791004864|lrwxrwxrwx|'/tmp/t/link' -> '/etc/hostname'    (BusyBox)
symbolic link|13|1791004864|lrwxrwxrwx|/tmp/t/link -> /etc/hostname        (GNU, QUOTING_STYLE=literal)
```

- Both images accept the same command line.
- Fields:
  - **type**: `directory`, `regular file`, `regular empty file`, `symbolic link`, and others.
  - **size**: bytes.
  - **mtime**: Unix seconds.
  - **mode**: as `ls` shows it.
  - **path**: last, so names containing `|` parse correctly when split into at most 5 fields.
- **Symlinks:**
  - `%N` is `path -> target`.
  - BusyBox always wraps both parts in single quotes for symlinks. GNU does not when `QUOTING_STYLE=literal`.
  - Parse by splitting on ` -> `, then strip a matching pair of surrounding `'`.
- **Speed:** about 95 ms (alpine) and 130 ms (debian) for a small directory. `/usr/bin` on debian (275 entries) took 137 ms.
- **Not tested:** filenames containing newlines would break the line-based format. Distroless images have no `sh`/`find`, so exec fails (exit 126/127). Fall back to the export snapshot below.

## 2. `wslc container cp`

- **The command is `wslc container cp`. There is no top-level `wslc cp` alias**, unlike `exec` and `logs`.
- **Download a file** with `container cp <id>:/path C:\local\file`. This works.
  - **A symlink is copied as a link.** Debian's `/etc/os-release` (a symlink) arrived as a 0-byte file.
  - **Use `--follow-link`/`-L`** to get the content. Downloads of user-selected files should always pass it.
- **Download a directory** with `container cp <id>:/dir C:\local\`.
  - **The Windows target needs a trailing separator.** Without it you get `Cannot copy a directory to a file path…` (`E_FAIL`).
  - Characters that Windows doesn't allow in names are replaced: `pipe|name` became `pipe_name`.
  - Symlinks inside become Windows symlinks.
- **Upload into an existing directory** with `container cp C:\local\file <id>:/dir/`. This works, including names with spaces.
- **Upload can't rename.** A destination file path that doesn't exist yet fails with `Could not find the file /tmp/renamed.txt…` (`ERROR_PATH_NOT_FOUND`). Docker would create it. So uploads keep the local name and go into a directory.
- **A missing source** gives `Could not find the file /nope in container …`, `Error code: ERROR_PATH_NOT_FOUND`. Map this to `NotFound`.
- **Stopped containers:** `cp` works in both directions (download, and upload into an existing directory).

## 3. Browsing stopped containers: `container export`

- `wslc container export -o <file.tar> <id>` on stopped debian took **1.1 s** and produced a 78 MB tar with 4,239 entries.
- Names, sizes, modes, mtimes and symlink targets are kept, including `pipe|name`, spaces and dotfiles.
- **Plan:**
  - Export to a temp file and index it with `System.Formats.Tar` (AOT-safe), then delete the file.
  - Browse the snapshot read-only and show a "snapshot of a stopped container" note.
  - Downloads and previews still use `cp`, which works on stopped containers.
- *(inferred)* Volume and bind-mount contents aren't in the export; Docker behaves the same way.
- Larger images take longer *(inferred: postgres at about 300 MB should take a few seconds)*, so show progress text.

## 4. Stats and charts

- `stats --format json` gives one JSON line per running container:

  ```
  {"BlockIO":"2.55MB / 0B","CPUPerc":"0.00%","ID":"…","MemPerc":"0.02%","MemUsage":"3.352MiB / 15.31GiB","Name":"…","NetIO":"946B / 0B","PIDs":1}
  ```

- Strings use binary units for memory and decimal units for I/O; S1 measured 1–2 s per call.
- **MewCharts** (`Aprillz.MewUI.MewCharts` 0.22.1, the LiveChartsCore 2.0.4 engine drawn through MewUI) was tested in a NativeAOT test app. That app had a live `LineSeries<ObservablePoint>` updated 4× a second.
  - The publish had **0 warnings** and a 4.9 MB exe.
  - It rendered correctly and used about 83 MB of memory.

## Implications for M5

- **`IContainerFiles.ListDirectoryAsync`:** `find`/`stat` via exec when running. When stopped, or when exec fails because the image has no `find`/`sh`, use an export snapshot.
- **Downloads:** `container cp --follow-link`. Folders get a trailing separator on the target.
- **Uploads:** into the current directory, keeping the local name. Show an upload control only for running containers; uploads to stopped containers work but are of little use.
- **Preview:** `cp` the file to a temp location, read the first 64 KB, and show it as text if it isn't binary.
- **Stats:**
  - A shared non-overlapping loop that runs only while someone is watching and containers are running.
  - CPU and memory columns in the container list.
  - A Stats tab in container details with CPU, memory, network and disk charts (about 2 minutes of history).

No decisions are needed from the user; the findings fit the existing plan.
