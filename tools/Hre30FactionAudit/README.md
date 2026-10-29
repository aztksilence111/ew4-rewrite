# HRE AI stress audit

Uses the actual HRE map from `rewrite/content/three-map-review.ew4mod` with
30 synthetic factions and 500 independent units per faction. It exercises
ordinary AI actions, client commits, handoffs, autosaves and final full views.
The fixture uses fixed combat and AI seeds. Player profiles are never loaded.

```powershell
dotnet build tools/Hre30FactionAudit/Hre30FactionAudit.csproj -c Release --artifacts-path build/hre30-audit
$env:EW4_HRE30_BATCH = '1'
dotnet build/hre30-audit/bin/Hre30FactionAudit/release/Hre30FactionAudit.dll . build/hre30-run
```

Output must be a new directory below the repository's `build/` directory.
The default run stops after returning to the player, with a six-hour limit.
Remove output, temporary profiles and isolated build files after recording results.

- Without `EW4_HRE30_BATCH=1`, records individual planning, execution and commit times.
- Optional third argument: maximum committed AI steps. Batches may overshoot this count.
- Optional fourth argument: a session checkpoint from this exact fixture.
- `EW4_HRE30_STOP_FACTIONS=1`: stops at the first complete AI faction boundary,
  which is suitable for deterministic comparisons across different batch sizes.
- `EW4_HRE30_COMPARE_SLOT`: compares a completed replay to a client slot,
  excluding faction appearance only.

Compact batches can return zero committed steps while `planning=true`. Such a
response advances a private planning stage without changing durable RNG or revision.
The audit accepts these continuations. `steps.json` includes every request, allowing
latency percentiles and zero-commit request counts to be computed after the run.
Do not interpret the 250 ms batch budget as a hard deadline for a single search,
commit, autosave or full-view response.
