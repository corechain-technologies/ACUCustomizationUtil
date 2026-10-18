# Agent instructions

## Codebase index (Repowise)

This repository is indexed by the shared Repowise MCP server (`repowise` tools in Hermes).
Pass `repo="acucustomizationutil"` on every Repowise call. Start with `get_overview`, use `get_context` /
`get_symbol` before reading files by hand, and `get_risk` before editing hotspot files.
`_meta.indexed_commit` vs `live_head` tells you whether the index is behind this checkout;
linked worktrees query the main checkout's index.
