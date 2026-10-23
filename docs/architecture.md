# Branch 0.2

This document describes the original web proof of concept. The primary product is now the [native Codex desktop overlay](../desktop/README.md), which uses WPF and Codex App Server directly and does not depend on this HTTP architecture.

The browser renders the workspace. A loopback-only Node HTTP server owns persistence. The stdio MCP bridge calls that same HTTP API, so AI clients and the browser cannot independently overwrite the workspace file.

## Data and context

A version 2 workspace contains folders and projects. Each project has exactly one project root and a directed acyclic graph of conversation nodes. Each message has a stable ID. A node has its own messages and a frozen copy of inherited messages with their original source node IDs.

Branch creation copies context only through the chosen message and records its origin. A fork of inherited context still points to the original message owner. Later additions to a source conversation do not alter descendants.

Merge creation accepts source nodes and an explicit list of message IDs. It deduplicates shared messages by ID, preserves selected text verbatim, records source snapshots, and optionally appends a user-written merge brief. Each source must contribute at least one selected message, which may be shared history. Source transcripts remain separate; this is a context merge, not a Git merge or an automatic rewrite of historical messages. AI synthesis is not implemented.

Comparison uses message IDs to distinguish shared history from unique additions. It does not infer semantic equivalence or detect contradictory claims. Source links allow manual review. Merge nodes indicate when source conversations have grown after their snapshots.

## Persistence

The canonical file is `.branch/workspace.json`. Every command runs against a clone of the current state. Graph validation runs before saving. A temporary file is written, the prior workspace is copied to `.bak`, and the temporary file is renamed into place. State is published only after a successful write. A process lock prevents two app servers from writing the same workspace. Stale locks from terminated processes are recovered when the old process is no longer running.

Commands require an expected revision. A stale client receives HTTP 409 and must reload before retrying. Rejected commands do not update the stored revision. Corrupt files cause startup to fail with an explanation instead of being overwritten. The backup contains the previous successful revision; workspace exports are the portable backup mechanism.

## Local access

The server binds to 127.0.0.1 and accepts only its loopback host names. Cross-origin browser requests are rejected. Writes require JSON plus a custom client header. Only an explicit set of frontend assets is served; workspace files are unavailable through static routes. The local MCP bridge and other local applications can access the API. This is a single-user local application, not an authenticated multi-user service.

The app has no external runtime dependencies, remote font loading, analytics, or inference calls. Conversation import parses user-selected JSON locally. Imports are additive, never a silent workspace replacement. Version 2 workspaces and the original prototype format are supported. A ChatGPT `conversations.json` import traverses actual message parent relationships, not timestamps or array order, preserving forks caused by edited messages. Text is preserved; non-text parts receive visible placeholders. It does not copy attachments or private account credentials. Import format is based on exported data structures and is guarded by validation; unusual or future formats may require an adapter update.

## Integration boundary

The MCP stdio bridge provides list, read, branch, add-message, and merge tools. It neither reads unrelated host chats nor sends messages into them. An AI client may read a Branch conversation, generate a response through its own model, and save that response using `branch_add_message`.

The web composer currently saves local messages; it does not itself run a model. ChatGPT-hosted plugin installation, authenticated remote transport, automatic host-history synchronization, and replacing native host UI are not implemented. The local bridge is the first working integration surface. Codex App Server is a possible future adapter for selected native threads; connecting it requires explicit lifecycle and approval handling, rather than reusing undocumented local databases.

Official integration references:

- [MCP in Codex](https://learn.chatgpt.com/docs/extend/mcp)
- [Plugin architecture](https://developers.openai.com/plugins/concepts/plugins)
- [Codex App Server](https://learn.chatgpt.com/docs/app-server)

## Verification

`npm test` exercises context boundaries, multi-parent merges, source integrity, imports, graph validation, disk recovery behavior, writer locking, concurrent revision checks, and an end-to-end HTTP/MCP workflow. `npm run check` checks syntax. Browser visual and interaction checks are separate from these tests.
