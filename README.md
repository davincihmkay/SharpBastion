# SharpBastion

## Purpose

SharpBastion is a system-first LLM harness, built on domain-driven design in C#. A domain layer of value objects, protocol-gated commands, and explicit queues decides what a model's output is allowed to become. The model runs isolated, read-only, and session-scoped: no write-side tools, no access beyond its own packaged repository context. The model contributes text. The domain model contributes judgment.

Privacy is structural, not configured. LM Studio runs on-device by default, and every sensitive path, such as repository content leaving the machine, a script escaping `Scripts/`, or an execution running unattended, is closed by default and opened only by explicit protocol override.

Every side effect, whether a write, a script run, or an external LLM call, passes through one named, auditable protocol. The model enables. The system decides.

<p align="center">
  <img src="assets/sharpbastion_gif_protocol.gif" alt="OpenHarness Terminal Demo" width="800">
</p>

**Core principles:**

- **System-controlled execution.** The LLM does not initiate actions. It receives exactly the context the system prepares, responds, and stops. All subsequent behaviour is determined by the system, not the model.
- **Explicit approval gates.** No file is written without the user reviewing and approving the proposed content. The `PendingWriteQueue` holds all proposals. Nothing is applied silently.
- **Local-first and private by default.** LmStudio runs on-device. No repository content leaves the machine unless the user explicitly configures an external backend. Claude CLI is available as a fallback but requires conscious opt-in via authentication.
- **Scheduled jobs are explicit, bounded, and gated — not hidden state.** SharpBastion can run allowlisted Python scripts (`Scripts/` only) on a fixed interval, registered explicitly via the `schedule` command. Nothing runs autonomously by default: unless the script-execution protocol is overridden at startup, a due job is queued (`PendingJobRunQueue`) and requires the same interactive `review` approval as a file write. Job state is in-memory only and does not survive a restart — there is no persisted background scheduler independent of this process.
- **No hidden state, beyond the above.** There are no background agents outside what's described above, no autonomous re-ingestion, no persisted schedules. Every operation is triggered explicitly by the user, and every autonomous behaviour (unattended script execution) is an explicit, revocable opt-in.
- **Guardrails over convenience.** Write proposals are sandboxed to the ingested repository root. Allowed file types are an explicit allowlist. Path traversal is blocked at the system level, independent of what the model returns. Scheduled jobs are sandboxed the same way: only `.py` files resolving inside the process's own `Scripts/` directory may be registered.
- **Protocols over policy.** Every behaviour with security or privacy impact is mediated by an explicit protocol object — `IExternalLlmProtocol` for external routing, `IScriptExecutionProtocol` for unattended script execution, `PendingWriteQueue`/`PendingJobRunQueue` for gated persistence and execution, path-traversal and allowlist checks for write and script targets. Protocols are enforced at a single point. They are never implicit, never bypassed, and never replaced by ad-hoc conditionals scattered through the codebase. See [Architecture > Protocols](#architecture).
- **Open source by design.** Released under the Apache License 2.0 ([LICENSE](./LICENSE), [NOTICE](./NOTICE)). Every protocol, guardrail, and routing decision is verifiable in source.

The user is always in control. The AI is a tool. The system is the gatekeeper.

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
  │           └── PendingWriteQueue       → buffers file write proposals for user review
  └── JobController
        └── JobSchedulerService           → holds registered scheduled jobs (in-memory)
              └── ScheduledJobRunnerHostedService  → background timer, ticks every 30s
                    ├── PythonScriptClient → runs a Scripts/*.py job via `python3`/`python`
                    └── PendingJobRunQueue → buffers due job runs for user review
```

**LLM routing:** LmStudio is queried first via health check. If offline, Claude CLI is used as fallback. Both backends share the same message format.

**File write flow:** LLM responses are parsed for `<file_write path="...">` blocks. Matches are queued in `PendingWriteQueue` and held until the user explicitly reviews and approves them via the `review` command.

**Scheduled job flow:** `schedule <script.py> <interval>` registers a job (must resolve inside `Scripts/`, must be a `.py` file). A background timer checks all registered jobs every 30 seconds. A due job either runs immediately and unattended (script-execution protocol overridden) or is queued in `PendingJobRunQueue` and held until approved via `review` (the default). Job state — registrations and their next-run times — lives only in memory for the current process.

**Protocols:** Behaviours with security or privacy impact are mediated by explicit protocol objects rather than ad-hoc checks. A protocol is a component the system consults before acting; it is never assumed, never bypassed, and never implicit.

- `IExternalLlmProtocol` — gates routing of repository content to any non-local LLM backend. Defaults to `IsOverridden = false`. Until explicitly overridden via `OverrideProtocol()`, `RepositoryDomainService` refuses to fall back to Claude CLI even when LmStudio is offline, throwing `InvalidOperationException` instead of silently exfiltrating data. The override is per-process and is requested at startup (see [Startup protocol prompts](#usage)).
- `IScriptExecutionProtocol` — gates unattended execution of scheduled scripts. Defaults to `IsOverridden = false`. Until explicitly overridden, a due job is never executed directly by the background timer — it is queued in `PendingJobRunQueue` instead, and only runs once approved via `review`. The override is per-process and is requested at startup alongside the external-routing prompt.
- `PendingWriteQueue` — gates persistence of any file content proposed by the LLM. No `<file_write>` block is applied without an interactive `y` confirmation in the `review` command. The queue is the only path from model output to disk. Dedupes by file path — a new proposal for a path already pending is skipped, not stacked.
- `PendingJobRunQueue` — gates unattended-disabled job execution the same way: one pending entry per job id, skip-and-log (not stacked) if a prior due occurrence for that job hasn't been reviewed yet.
- Path-traversal guard — `RepositoryIngestorService.IsWithinRoot` enforces that every proposed write target resolves strictly inside the ingested repository root, independent of what the model returns. Allowed file extensions and filenames are likewise an explicit allowlist on `Domain.File`. `Domain.ScheduledJob` applies the equivalent guard for scheduled scripts: only `.py` files resolving inside the process's own `Scripts/` directory may be registered.

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
Edit `appsettings.json`'s `Assistant:SystemPromptPath` to reference the file you just created (e.g. `Config/my-assistant.md`), and edit that file's contents to define your assistant's persona. See [Configuration](#configuration) for the full set of options.

**4. (Optional) Authenticate Claude CLI**

Only required if using Claude CLI as the fallback backend.
```bash
claude auth login
```

**5. Build**
```bash
dotnet build
```

---

## Configuration

| Environment Variable | Default | Description |
|---|---|---|
| `CLAUDE_CLI_PATH` | `~/.local/bin/claude` (Linux/macOS), `claude` (Windows) | Path to the Claude CLI binary |

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

Before the interactive loop starts, SharpBastion asks two independent consent questions:

```
[PROTOCOL] If LmStudio is offline, queries can fall back to an External LLM provider.
[PROTOCOL] Repository content would leave this machine.
[PROTOCOL] Override external routing restriction? [y/n]:

[PROTOCOL] Scheduled Python scripts (Scripts/ only) can run unattended, or wait for interactive approval.
[PROTOCOL] Unattended execution runs due scripts with no per-run confirmation.
[PROTOCOL] Override script-execution restriction and allow unattended runs? [y/n]:
```

- External routing — Answer `y` to enable Claude CLI fallback when LmStudio is unreachable. Answer anything else and the system throws `InvalidOperationException` rather than routing externally when LmStudio is offline.
- Script execution — Answer `y` to let due scheduled jobs run unattended. Answer anything else (the default posture) and due jobs are queued in `PendingJobRunQueue`, requiring a `y` in `review` before they execute.

Both gates are enforced by their respective protocol objects and are intentional: neither external routing nor unattended script execution is ever implicit. Each choice is per-process and made fresh on every startup.

**CLI commands**

```
ingest <path>         Ingest a repository and send its contents to the LLM
use <path>            Switch the active repository without re-ingesting
ask <question>        Ask a question about the active repository
schedule <s> <i>      Schedule a Scripts/*.py job on a fixed interval (e.g. schedule downloadDailyBarcelonaPdf.py 24h)
review                Interactively approve or reject pending file writes and job runs
status                Display the currently active repository
help                  Show this command list
exit / quit           Shut down
```

Interval format for `schedule` is a plain duration: a number followed by `s`, `m`, `h`, or `d` (seconds/minutes/hours/days) — e.g. `30m`, `24h`, `2d`. No cron expressions.

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

**Supported file types for write proposals**

`.cs` `.json` `.yaml` `.yml` `.xml` `.txt` `.md` `.config` `.toml` `.ini` `.sql` `.html` `.css` `.js` `.ts` `.proto` `.sh` `.ps1` `.bat`

**Scheduled jobs**

Only `.py` files that resolve inside this process's own `Scripts/` directory (relative to its working directory) may be registered — arbitrary paths are rejected. Registration is in-memory only; jobs do not survive an app restart and must be re-scheduled.

---

## Docker

```bash
docker compose up --build
```

The `compose.yaml` builds and runs the application inside a .NET 10 runtime container. Note: LmStudio and Claude CLI must be reachable from within the container. Adjust networking in `compose.yaml` as needed for your environment.

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