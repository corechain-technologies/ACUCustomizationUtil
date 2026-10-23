# Console-only logger: reviewed component results

The opt-in source change was exercised in an isolated Linux guest after an
independent containment review. No installed ACU binary, original ACU project,
Program/Host/services, real configuration, or ERP operation was executed.

## Observed matrix

| Logger source | Cases | Passed | Failed | Application exit |
|---|---:|---:|---:|---:|
| Original `de14878bd3790860fc900bb22423845413b4d8e4` | 42 | 12 | 30 | 1 |
| Patched logger | 42 | 42 | 0 | 0 |

The same harness and real Serilog dependencies were used for both sources.
Individual case names and totals were parsed and checked, not inferred from the
exit code. The original retained all unset/false positive controls and failed
the opt-in/invalid-setting cases. The patched logger passed normal/silent modes,
console INFO/ERROR/DEBUG expectations, invalid-setting identity, unchanged global
logger on rejection, existing-file preservation and blocked-file sink checks.

Original logger SHA-256:
`6433879063abfbbbe173b5a3e58f06beb62623c89da6abc7c58e8cdbb1216564`

Patched logger SHA-256:
`e8a20fd07d54f5c87c9441ac11c8ec0d4f496d81d5979d01f73091946c568edc`

Input archive SHA-256:
`f0b4d488f9f04e4234817cee0c20429358e69830c65283f0e016c9adb5e8d676`

## Controls

- SDK 10.0.302, net10.0, explicit selected compile items only.
- Public package provisioning used an empty dependency project first; executable
  restore/build/run then used source-free NuGet configuration and private copies
  of the provisioned cache under a network namespace.
- Fresh private HOME/TMP, disabled inherited Directory.Build/Directory.Packages
  imports, excluded package build/analyzer assets, no shared compiler server.
- An outer PID namespace with kill-child behavior and a 240-second aggregate
  deadline bounded each run. Both runs completed without timeout.
- All 399 staged source/package donor files were hash-identical afterward.
- Case directories have numeric prefixes so case-variant flag tests cannot alias
  directories on a case-insensitive filesystem.

The Program catch was narrowed to the dedicated flag's `ArgumentException.ParamName`;
unrelated argument failures are not mislabeled. That Program branch remains
source-reviewed only because the harness intentionally excludes the full entrypoint.

## Limits

This proves the two real Serilog builders' behavior on the tested Linux runtime,
not full CLI/parser parity, Windows behavior, production transitive-package
identity, child-output integration, syscall-level no-open behavior, or transient
write absence. Console-only is not output sanitization. The setting must be
exactly `true` before startup and remain fixed throughout the process; no existing
logger migration or disposal guarantee was added. The patch is source-only and
has not replaced an installed executable or activated a context ACU adapter.
