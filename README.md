# SharpBastion

## Purpose

SharpBastion is a system-first LLM harness and orchestrator, built on domain-driven design in C#. A domain layer of value objects, protocol-gated commands, and explicit queues decides what a model's output is allowed to become. The model runs isolated, read-only, and session-scoped: no write-side tools, no access beyond its own packaged repository context. The model contributes text. The domain model contributes judgment.

Privacy is structural, not configured. LM Studio runs on-device by default, and every sensitive action is closed by default and opened only by explicit protocol override. That applies whether it's repository content leaving the machine, a search query leaving the machine, or a scheduled script running unattended.

Every side effect passes through one named, auditable protocol: a write, a search query, a script run, or an external LLM call.

<p align="center">
  <img src="assets/sharpbastion_gif_protocol.gif" alt="OpenHarness Terminal Demo" width="800">
</p>

**Core principles:**

- **System-controlled execution.** The model responds, the system decides what happens next.
- **Explicit approval gates.** Whether it's a file write, a search or a script run, nothing executes without passing through a queue and, by default, a human.
- **Local-first and private by default.** LmStudio runs on-device but with possible external LLM routing. Search and unattended execution are each an explicit, revocable opt-in.
- **No hidden state.** No background agents beyond the scheduler, no autonomous re-ingestion, no persisted schedules — every action is user-triggered.
- **Guardrails over convenience.** Path traversal, file-type allowlists, script-directory sandboxing, and search-query validation are enforced at the system level, independent of what the model returns.
- **Protocols over policy.** Every side-effecting behavior is mediated by one named, inspectable protocol object, never an ad-hoc conditional. See [Architecture > Protocols](#architecture) for the full list and their mechanics.
- **Open source by design.** Apache 2.0. Every protocol and guardrail is verifiable in source.

---

## Architecture

```
CLI (Program.cs)
  ├── RepositoryController
  │     └── RepositoryIngestorService
  │           ├── RepomixCliClient        → packages repo to XML via repomix
  │           ├── RepositoryDomainService
  │           │     ├── LmStudioClient    → primary LLM backend (local)
  │           │     └── ClaudeCliClient   → fallback LLM backend
  │           ├── PendingWriteQueue       → buffers file write proposals for user review
  │           ├── KagiClient              → external Kagi Search API (model-proposed, protocol-gated)
  │           └── PendingKagiSearchQueue  → buffers search proposals for user review
  ├── KagiSearchController
  │     └── → approve/reject search-related proposals
  └── JobController
        └── JobSchedulerService           → holds registered scheduled jobs (in-memory)
              └── ScheduledJobRunnerHostedService  → background timer, ticks every 30s
                    ├── PythonScriptClient → runs a Scripts/*.py job via `python3`/`python`
                    └── PendingJobRunQueue → buffers due job runs for user review
```

**LLM routing:** LmStudio is queried first via health check. If offline, Claude CLI is used as fallback. Both backends share the same message format.

**File write flow:** LLM responses are parsed for `<file_write path="...">` blocks, each becoming a pending proposal handled by `PendingWriteQueue`. See Protocols below for the review/approval gate.

**Kagi search flow:** lets the model check the internet for something it can't derive from the repository or provided snippets/logs. LLM responses are parsed for `<kagi_search query="...">` tags, each becoming a pending proposal handled by `PendingKagiSearchQueue`. See Protocols below for the override/cap behavior. Once approved, whether interactively or automatically, the query runs against Kagi's Search API, and the possibly truncated results are fed back to you as your next turn on the same session. That follow-up reply is then processed for further proposals exactly like any other response, recursively, until it stops proposing further searches or the round-trip budget runs out.

**Scheduled job flow:** `schedule <script.py> <interval>` registers a job (must resolve inside `Scripts/`, must be a `.py` file). A background timer checks all registered jobs every 30 seconds. A due job either runs immediately or is queued in `PendingJobRunQueue`. See Protocols below for which applies and when. Job state, meaning registrations and their next-run times, lives only in memory for the current process.

**Session-listen flow:** `listen` lets one running Bastion process observe another's console output, read-only — it cannot type input or otherwise act as that session. Candidate sessions are discovered via `SessionDiscovery`; picking one streams its output live until Enter is pressed to detach. See Protocols below for the override gate.

**Repository discovery:** `ingest` run with no argument lists candidate repositories under the configured `Assistant:WorkspaceRoot` (every top-level subdirectory, excluding dot-prefixed ones such as `.git`, `.vscode`, `.idea` — no requirement that it be a Git repository, since Repomix packages a wide range of project layouts, not just Git repositories) and lets you pick which to ingest by index. `ingest <path>` still ingests a single path directly, and `ingest <path>,<path>,...` ingests several in one call — all three converge on the same batch-ingestion call, so partial failures are reported the same way regardless of which form triggered it.

**Protocols:** Behaviours with security or privacy impact are mediated by explicit protocol objects rather than ad-hoc checks. A protocol is a component the system consults before acting; it is never assumed, never bypassed, and never implicit.

- `IExternalLlmProtocol` — gates routing of repository content to any non-local LLM backend. Defaults to `IsOverridden = false`. Until explicitly overridden via `OverrideProtocol()`, `RepositoryDomainService` refuses to fall back to Claude CLI even when LmStudio is offline, throwing `InvalidOperationException` instead of silently exfiltrating data. The override is per-process and is requested at startup (see [Startup protocol prompts](#usage)).
- `IScriptExecutionProtocol` — gates unattended execution of scheduled scripts. Defaults to `IsOverridden = false`. Until explicitly overridden, a due job is never executed directly by the background timer — it is queued in `PendingJobRunQueue` instead, and only runs once approved via `review`. The override is per-process and is requested at startup alongside the external-routing prompt.
- `IKagiSearchProtocol` — gates unattended execution of model-proposed Kagi searches. Defaults to `IsOverridden = false`. Until explicitly overridden, every `<kagi_search query="...">` proposal parsed out of a response is queued in `PendingKagiSearchQueue` instead of running, and only executes once approved via `review`. The override is per-process, requested at startup alongside the other two prompts, and even then is bounded: `Assistant:KagiSearchMaxRoundTrips` caps how many auto-approved search→reply hops can chain unattended within a single `ask`/search-resolution call before control falls back to manual review.
- `ISessionListenProtocol` — gates whether this process's console output can be observed by another local Bastion session at all. Defaults to `IsOverridden = false`. Until explicitly overridden, `SessionBroadcastServer` never binds a listener socket — unlike the other three protocols here, which gate a per-action decision on top of an already-running mechanism, there is no partial/inert state to probe. Once overridden, a same-user-only Unix domain socket and manifest sidecar are created together; observers connect via the `listen` command, read-only, with no channel back into this session.
- `PendingWriteQueue` — gates persistence of any file content proposed by the LLM. No `<file_write>` block is applied without an interactive `y` confirmation in the `review` command. The queue is the only path from model output to disk. Dedupes by file path — a new proposal for a path already pending is skipped, not stacked.
- `PendingJobRunQueue` — gates unattended-disabled job execution the same way: one pending entry per job id, skip-and-log (not stacked) if a prior due occurrence for that job hasn't been reviewed yet.
- `PendingKagiSearchQueue` — gates execution of any Kagi search proposed by the model, the same shape as `PendingWriteQueue`/`PendingJobRunQueue`: one pending entry per proposal, skip-and-log (not stacked) if an identical (repository, query) pair is already pending.
- Path-traversal guard — `RepositoryIngestorService.IsWithinRoot` enforces that every proposed write target resolves strictly inside the ingested repository root, independent of what the model returns. Allowed file extensions and filenames are likewise an explicit allowlist on `Domain.File`. `Domain.ScheduledJob` applies the equivalent guard for scheduled scripts: only `.py` files resolving inside the process's own `Scripts/` directory may be registered. `ValueObjects.KagiQuery` applies the equivalent guard for search proposals: non-empty and length-capped.
- `Assistant:WorkspaceRoot` — the root repository discovery and Repomix packaging both operate under. Has no implicit default; `AssistantProfile.Load` refuses to start rather than assume a location nobody explicitly configured. See [Configuration](#configuration).

The pattern is uniform: each protocol has a single, inspectable point of enforcement, and every external-facing or autonomous-facing action passes through one.

---

## Prerequisites

| Dependency | Purpose | Required |
|---|---|---|
| [.NET 10 SDK](https://dotnet.microsoft.com/download) | Build and run | Yes |
| [repomix](https://github.com/yamadashy/repomix) | Repository packaging | Yes |
| [LM Studio](https://lmstudio.ai) | Local LLM backend | No (fallback available) |
| [Claude CLI](https://github.com/anthropics/claude-code) | Fallback LLM backend | No (if LmStudio is online) |
| Python 3 (`python3` / `python` on PATH) | Runs scheduled `Scripts/*.py` jobs | No (only if using `schedule`) |
| [Kagi API key](https://kagi.com/api/docs/openapi) | Model-proposed web search | No (only if a response proposes a `<kagi_search>`) |

At least one LLM backend must be available at runtime.

---

## Setup

**1. Clone the repository**
```bash
git clone https://github.com/davincihmkay/SharpBastion.git
cd SharpBastion
```

**2. Install repomix**
```bash
npm install -g repomix
```

**3. Configure app settings**

`appsettings.json` is per-clone and not versioned. Copy the template and point it at your own system-prompt file:
```bash
cp appsettings.example.json appsettings.json
cp Config/system-prompt.example.md Config/my-assistant.md
```
Edit `appsettings.json`'s `Assistant:SystemPromptPath` to reference the file you just created (e.g. `Config/my-assistant.md`), and edit that file's contents to define your assistant's persona. Also set `Assistant:WorkspaceRoot` to the directory containing the repositories you intend to ingest (e.g. `~/git`) — SharpBastion refuses to start without it. If your persona file contains the literal token `{{WORKSPACE_ROOT}}`, it is substituted with this resolved path at load time. See [Configuration](#configuration) for the full set of options.

**4. (Optional) Authenticate Claude CLI**

Only required if using Claude CLI as the fallback backend.
```bash
claude auth login
```

**5. (Optional) Set a Kagi API key**

Only required if you want to allow the model to propose web searches.
```bash
export KAGI_API_KEY="..."
```
Never placed in `appsettings.json` — see [Configuration](#configuration).

**6. Build**
```bash
dotnet build
```

---

## Configuration

| Environment Variable | Default | Description |
|---|---|---|
| `CLAUDE_CLI_PATH` | `~/.local/bin/claude` (Linux/macOS), `claude` (Windows) | Path to the Claude CLI binary |
| `KAGI_API_KEY` | *(none — required only if a response proposes a `<kagi_search>`)* | Kagi API key used by `KagiClient` to authenticate search requests. Read once at process start; never sourced from `appsettings.json` and never logged. |

`Assistant:WorkspaceRoot` (or the `Assistant__WorkspaceRoot` environment variable) sets the directory SharpBastion discovers and resolves repositories under — it backs both `RepomixCliClient`'s invocation root and the `ingest` command's no-argument discovery flow (see [Usage](#usage)). Accepts an absolute path or a `~/`-prefixed one (expanded against the current user's home directory). Unlike the environment variables above, this is bound through the `Assistant` options section rather than read directly, so it can be pinned per-clone in `appsettings.json` and still overridden per-session via `Assistant__WorkspaceRoot`. There is no default: SharpBastion fails fast at startup if it is unset, or if it resolves to a directory that does not exist — a silent fallback location would run against this project's privacy-by-explicit-configuration posture (see Core principles above). If the loaded system-prompt file contains the literal token `{{WORKSPACE_ROOT}}`, it is substituted with this resolved path.

`Assistant:KagiSearchMaxRoundTrips` (or the `Assistant__KagiSearchMaxRoundTrips` environment variable) caps how many auto-approved search→reply hops `IKagiSearchProtocol`, once overridden, will chain unattended within a single `ask`/search-resolution call. Defaults to `3`; must be a positive integer if set explicitly — see [`appsettings.example.json`](./appsettings.example.json).

LmStudio is expected at `http://localhost:1234`. This is currently hardcoded — configurable via environment variable in a future release.

The Python interpreter used for scheduled jobs is hardcoded (`python3` on Linux/macOS, `python` on Windows) — not currently configurable via environment variable.

---

## Usage

**Run**
```bash
dotnet run
```

Or via Docker:
```bash
docker compose run --rm sharpbastion
```
Note: Docker support is still in development and does not work yet (See [Known Limitations](#known-limitations)). If you are not contributing to its development, please run the project with dotnet run instead.

**Startup protocol prompts**

Before the interactive loop starts, SharpBastion asks three independent consent questions:

```
[PROTOCOL] If LmStudio is offline, queries can fall back to an External LLM provider.
[PROTOCOL] Repository content would leave this machine.
[PROTOCOL] Override external routing restriction? [y/n]:

[PROTOCOL] Scheduled Python scripts (Scripts/ only) can run unattended, or wait for interactive approval.
[PROTOCOL] Unattended execution runs due scripts with no per-run confirmation.
[PROTOCOL] Override script-execution restriction and allow unattended runs? [y/n]:

[PROTOCOL] Model-proposed Kagi searches can run unattended, or wait for interactive approval.
[PROTOCOL] The search query text (crafted by the model) leaves this machine to Kagi's API once a
[PROTOCOL] proposal runs, whether unattended or explicitly approved.
[PROTOCOL] Override kagi-search restriction and allow unattended searches? [y/n]:

[PROTOCOL] A second local process (e.g. another SSH session on this machine) can attach and
[PROTOCOL] observe this session's console output in real time, read-only — it cannot type
[PROTOCOL] input, approve reviews, or otherwise act as this session.
[PROTOCOL] Attaching exposes ingested repository content and LLM responses to that observer.
[PROTOCOL] Override session-listen restriction and allow read-only observers? [y/n]:
```

- External routing — Answer `y` to enable Claude CLI fallback when LmStudio is unreachable. Answer anything else and the system throws `InvalidOperationException` rather than routing externally when LmStudio is offline.
- Script execution — Answer `y` to let due scheduled jobs run unattended. Answer anything else (the default posture) and due jobs are queued in `PendingJobRunQueue`, requiring a `y` in `review` before they execute.
- Kagi search — Answer `y` to let proposed searches run unattended, bounded by `Assistant:KagiSearchMaxRoundTrips`. Answer anything else (the default posture) and proposed searches are queued in `PendingKagiSearchQueue`, requiring a `y` in `review` before they run.
- Session-listen — Answer `y` to let another local Bastion session (via `listen`) observe this one's console output, read-only. Answer anything else (the default posture) and no listener socket is ever bound — there is nothing to attach to, for the lifetime of this process.

All four gates are enforced by their respective protocol objects and are intentional: external routing, unattended script execution, unattended search execution, and session observability are never implicit. Each choice is per-process and made fresh on every startup.

**CLI commands**

```
ingest <path>[,<path>...]  Ingest one or more repositories and send their contents to the LLM
ingest                     Discover repositories under Assistant:WorkspaceRoot and pick which to ingest
use <path>                 Switch the active repository without re-ingesting
ask <question>             Ask a question about the active repository
schedule <s> <i>           Schedule a Scripts/*.py job on a fixed interval (e.g. schedule downloadDailyBarcelonaPdf.py 24h)
review                     Interactively approve or reject pending file writes, job runs, and searches
status                     Display the currently active repository
listen                     Discover other running Bastion sessions and observe one's output, read-only
help                       Show this command list
exit / quit                Shut down
```

Interval format for `schedule` is a plain duration: a number followed by `s`, `m`, `h`, or `d` (seconds/minutes/hours/days) — e.g. `30m`, `24h`, `2d`. No cron expressions.

`ingest` accepting more than one path always ingests each independently and reports success/failure per path; the active repository afterward is the *first path that actually succeeded*, in request order — not necessarily the first path you typed, if an earlier one in the list failed.

**Example session**
```
coolassistant> ingest ~/git/MyProject
Ingesting: /home/user/git/MyProject
Successfully ingested repositories: /home/user/git/MyProject
Active repo set to: /home/user/git/MyProject

coolassistant> ask where is the entry point of the application?
...

coolassistant> schedule downloadDailyBarcelonaPdf.py 24h
Scheduled 'downloadDailyBarcelonaPdf.py' every 1.00:00:00.

coolassistant> review
1 pending write(s).
  File  : /home/user/git/MyProject/Program.cs
--- diff ---
...
--- end ---
Approve write? [y/n]: y
Written: /home/user/git/MyProject/Program.cs
```

**Example discovery session**
```
coolassistant> ingest
Repositories under /home/user/git:
  1. /home/user/git/MyProject
  2. /home/user/git/OtherProject
Select repositories (comma-separated indices, e.g. 1,3): 1,2
Ingesting: /home/user/git/MyProject, /home/user/git/OtherProject
Successfully ingested repositories: /home/user/git/MyProject, /home/user/git/OtherProject
Active repo set to: /home/user/git/MyProject
```

**Supported file types for write proposals**

`.cs` `.json` `.yaml` `.yml` `.xml` `.txt` `.md` `.config` `.toml` `.ini` `.sql` `.html` `.css` `.js` `.ts` `.proto` `.sh` `.ps1` `.bat`

**Supported proposal tags**

Alongside `<file_write path="...">...</file_write>`, a response may contain `<kagi_search query="...">` (self-closing, or with a bare/closed tag — no body). Both are parsed out of the same LLM response text and queued for review the same way; neither is ever executed just because the model wrote the tag.

**Scheduled jobs**

Only `.py` files that resolve inside this process's own `Scripts/` directory (relative to its working directory) may be registered — arbitrary paths are rejected. Registration is in-memory only; jobs do not survive an app restart and must be re-scheduled.

---

## Docker

```bash
docker compose up --build
```

The `compose.yaml` builds and runs the application inside a .NET 10 runtime container. Note: LmStudio, Claude CLI, and Kagi's API must be reachable from within the container. Adjust networking in `compose.yaml` as needed for your environment.

`Scripts/` resolution for scheduled jobs depends on the process's working directory containing a `Scripts/` folder alongside the running binary — this is not currently wired into the Docker publish step (see Known Limitations) and is unverified in a container context.

---

## Known Limitations

- Repository context is held in-memory only. Re-ingestion is required after restart.
- Scheduled job registrations are held in-memory only. Restarting the app clears all schedules; they must be re-registered via `schedule`.
- The scheduler polls for due jobs every 30 seconds (fixed, not configurable) — jobs with intervals shorter than that will not fire more precisely than the poll interval.
- LmStudio URL (`http://localhost:1234`) is not yet configurable via environment variable.
- The Python interpreter path is hardcoded (`python3`/`python`), not configurable via environment variable.
- `IsBesPractice` field on `IngestRepositoriesRequestObject` contains a typo (tracked internally).
- Blocking async calls (`GetAwaiter().GetResult()`) throughout the LLM pipeline — async refactor pending.
- `Assistant:KagiSearchMaxRoundTrips` only bounds the *unattended* search→reply chain (`IKagiSearchProtocol` overridden). When the protocol is not overridden, a single response proposing many searches queues all of them for manual review with no separate cap.
- `listen`/session-listen has no backlog or scrollback — an observer only sees output written after it attaches, and there is no way to send input back to the observed session.
- Session-listen access control is filesystem permissions on a Unix domain socket (owner-only, same OS user) — POSIX-specific. On Windows, the equivalent protection falls back to the user profile directory's default ACLs, unverified.
- Session-listen's socket and manifest sidecar (`~/.sharpbastion/session-<pid>.*`) are only cleaned up on a graceful stop (`exit`/`quit`, or EOF) within a bounded 2-second shutdown window. A hard kill (`kill -9`, OOM, crash) can leave stale files behind — harmless (dead PIDs are filtered out of `listen`'s candidate list), but not swept.
- Docker support is still a work in progress. The current setup has unresolved path issues, and the intended design is to run Claude CLI in an isolated container with LM Studio traffic routed through that container as well. `Scripts/` is not currently copied into the published/container output, so scheduled jobs are only verified when running via `dotnet run` from the repository root.

---

## Contributing

1. Fork the repository.
2. Create a feature branch: `git checkout -b feature/your-feature`
3. Commit your changes: `git commit -m "feat: description"`
4. Push and open a pull request.

Please ensure no credentials, `.env` files, or local paths are included in commits. See `.gitignore` for the full exclusion list.

---

## License

Licensed under the Apache License, Version 2.0. See [LICENSE](./LICENSE) for the full text.