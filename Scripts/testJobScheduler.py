#!/usr/bin/env python3
"""
testJobScheduler.py

Dummy script for exercising SharpBastion's scheduled-job pipeline
(JobSchedulerService -> ScheduledJobRunnerHostedService -> PendingJobRunQueue
-> ScheduledJobExecutionService -> PythonScriptClient) without depending on
any real external system.

Each run appends one line to a heartbeat log. Schedule it, then either
approve it via `review` or override the script-execution protocol for
unattended runs, and inspect the log to confirm the scheduler is actually
invoking the script on cadence.

Set SHARPBASTION_TEST_FORCE_FAIL=1 in the environment to make the run fail
(non-zero exit, message on stderr) instead — exercises
ScheduledJobExecutionService's failure-message branch.

stdlib only.
"""

import os
import sys
from datetime import datetime, timezone
from pathlib import Path

# --- Configuration -----------------------------------------------------------

LOG_FILE = Path.home() / ".sharpbastion" / "testJobScheduler.log"

FORCE_FAIL_ENV_VAR = "SHARPBASTION_TEST_FORCE_FAIL"


# --- Core --------------------------------------------------------------------

def append_heartbeat(log_file: Path) -> int:
    """Appends a UTC-timestamped line to log_file. Returns the new run number."""
    log_file.parent.mkdir(parents=True, exist_ok=True)

    line_count = 0
    if log_file.exists():
        with log_file.open("r", encoding="utf-8") as f:
            line_count = sum(1 for _ in f)

    run_number = line_count + 1
    timestamp = datetime.now(timezone.utc).isoformat()

    with log_file.open("a", encoding="utf-8") as f:
        f.write(f"[{timestamp}] run #{run_number}\n")

    return run_number


def main() -> None:
    if os.environ.get(FORCE_FAIL_ENV_VAR):
        sys.exit(f"[ERROR] Forced failure via {FORCE_FAIL_ENV_VAR}.")

    run_number = append_heartbeat(LOG_FILE)
    print(f"[OK] Heartbeat #{run_number} written to {LOG_FILE}")


if __name__ == "__main__":
    main()