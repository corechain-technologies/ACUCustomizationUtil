# Opt-in process logging policy and isolated regression harness

## Contract

Set exactly `ACU_CONSOLE_ONLY=true` in the ACU child process environment **before startup**. Unset or exactly `false` preserves legacy sinks and levels. Every other observable value (including casing/whitespace variants) is rejected before constructing any sink; Program prints a fixed diagnostic to stderr and returns 1 before building the host. Do not set an empty string: some launchers/.NET normalize empty to unset. This is one dedicated process setting, not a generic YAML/environment configuration interface; no user/machine environment writes are needed.

Both builders consult the setting on every call. Keep it fixed throughout the process: this is not a live logger migration API, and cannot dispose previously retained loggers or undo previous disk writes. `Build` retains Information console output but omits the file sink. `BuildSilent` retains Debug level and the global-logger assignment, but has no sinks under opt-in. It deliberately does not add console output: its only caller, `ProcessHelper`, already prints child output through Spectre.Console, and a console sink would duplicate it. The only normal builder caller is Program before host/parser creation. Existing global logger replacement/lifetime behavior is unchanged.

This policy covers these Serilog builders only. It neither sanitizes output nor prevents other application/child-process/configuration writes. Wrappers must treat stdout/stderr as potentially sensitive; no-disk logging is NOT sanitized output. No wrapper, ERP behavior, installed binaries, or original project build targets are changed.

## Containment / review gate

**New runs require a reviewed containment lane.** The bounded Linux matrix was independently reviewed and executed; [RESULTS.md](RESULTS.md) records original-source failures and patched-source passes. Source review alone is not runtime evidence. The controlled project compiles only Harness.cs, the real SerilogBuilder.cs and real StringExtentions.cs. No original Program, Host, service, command, ERP client, real config, child process or custom target is compiled. Direct package pins match production: Serilog.Extensions.Hosting 9.0.0, Console 6.0.0, File 6.0.0; net10.0.

The harness accepts an existing absolute task-owned scratch directory, creates one GUID-named subtree there, and retains synthetic evidence. It changes only its own process environment/current directory, creates synthetic fixtures, invokes real builders, emits synthetic messages/exceptions, closes loggers and compares filesystem snapshots. It reads only its synthetic fixture files. It does not delete anything or invoke a shell/network. SDK restore is a separate explicit public NuGet operation; subsequent execution can be offline. Review the full source, project and any inherited Directory.Build.* / NuGet.Config before approval. Copy into a clean task-owned guest tree, not beneath an unreviewed project hierarchy.

## Matrix and expected evidence

The same harness is used with original and fixed logger source. Each normal/silent path covers unset, false, true, and invalid TRUE/1/leading-space/typo settings, crossed with empty directory, existing synthetic rolling/unrolled files, and directories blocking the expected rolling file names. It verifies default file creation/content/levels, console versus silent behavior, invalid rejection and unchanged global logger, and no changed file names/content/last-write timestamps under true or invalid. Blocked file paths plus Serilog SelfLog detect attempted file-sink construction even if a file cannot be written. Existing files are seeded for yesterday/today/tomorrow to cover a midnight rollover. Only synthetic diagnostics are captured, in memory; reports contain case names and assertion failures, not real logs.

Original source should fail true and invalid cases while passing default positive controls. Fixed source should pass all cases. Inspect each JSON case plus the final total/failed record: aggregate exit code alone is insufficient. The Program rejection branch is source-reviewed only, intentionally excluded from native execution. No full ACU integration, Windows runtime parity, syscall-level no-open proof, transient write detection, or child-output integration claim is made.

## Isolated commands (after approval only)

Export the original logger with `git show de14878bd3790860fc900bb22423845413b4d8e4:src/ACUCustomizationUtil/Builders/Log/SerilogBuilder.cs` into a task-owned comparison file without altering the worktree. Transfer it as `original/SerilogBuilder.cs` and just the harness/project and linked fixed source files into an explicitly owned isolated guest tree, preserving relative paths. Never transfer real config or build the original ACU project.

Inside the authorized ubuntu-scratch guest, after verifying that tree and its ancestor build configuration:

```sh
cd /absolute/task-owned/logger-fixture
mkdir -p home tmp packages results original
export HOME="$PWD/home" TMPDIR="$PWD/tmp" DOTNET_CLI_HOME="$PWD/home"
export NUGET_PACKAGES="$PWD/packages" DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
DOTNET=/usr/local/bin/dotnet
PROJECT=tests/ConsoleOnlyLogging/ConsoleOnlyLogging.csproj
# Explicit authorized public dependency provisioning, not ACU execution:
"$DOTNET" restore "$PROJECT" --source https://api.nuget.org/v3/index.json
# Red: same harness, original logger; expect exit 1 from assertions, NOT a build failure.
"$DOTNET" build "$PROJECT" --no-restore -t:Rebuild -p:LoggerSource="$PWD/original/SerilogBuilder.cs" -o "$PWD/red-bin"
"$DOTNET" "$PWD/red-bin/ConsoleOnlyLogging.dll" "$PWD/results" > "$PWD/red.jsonl"
# Green: force recompilation with the fixed logger; expect exit 0 and zero failed cases.
"$DOTNET" build "$PROJECT" --no-restore -t:Rebuild -o "$PWD/green-bin"
"$DOTNET" "$PWD/green-bin/ConsoleOnlyLogging.dll" "$PWD/results" > "$PWD/green.jsonl"
```

These commands describe the sequence, not the complete containment harness. Apply
global MSBuild properties disabling inherited directory imports, package build/
analyzer assets, shared compilation and workload resolution. Use a source-free
NuGet config/private cache for offline builds, pinned SDK global.json, private
scratch, a network/PID namespace and an aggregate supervisor. Inspect individual
JSON cases and exit codes without stopping on the expected red result. Preserve
source/package hashes and reports; no wider platform or full-CLI acceptance is
asserted by this recipe.
