# Branch

A local app for organizing AI conversations into project maps, branching from specific messages, comparing paths, and merging selected context with source links.

Open source under the [MIT License](LICENSE). Contributions are welcome.

## Run

Requires Node.js 22 or later. No dependency installation is needed.

```sh
npm start
```

Open http://127.0.0.1:5180. On Windows, you can also double-click `start.cmd`. Keep the server running while you use the app. If it is already running, use its existing browser tab.

## Use

1. Create projects with the `+` beside **Your projects**. Create folders and move projects through **Project settings** by clicking the project name in the top breadcrumb.
2. Start a conversation from a project root using the bottom composer. Messages are saved as local notes; this composer does not generate AI replies by itself.
3. Open a conversation and choose **Branch here** on any message to create a fork through that exact point. **New branch** lets you choose the origin message. Inherited context stays frozen, and each inherited message links back to its source.
4. Select two nodes using **Select** in their previews or Shift-click on the map. **Compare** separates shared context from unique additions. **Merge** lets you choose source messages and write a handoff brief. It creates a new branch and keeps the originals.
5. Search across titles and messages, collapse branches, drag the background to pan, zoom or fit the map, edit node metadata, and archive or restore conversations. Drafts survive navigation within the page.
6. Use **Import conversations** for a ChatGPT `conversations.json` export, an individual exported conversation, a Branch workspace, or an original prototype workspace. Select which ChatGPT conversations to import. Text and branch relationships are preserved; non-text attachments are labeled but not copied. Imports add projects without replacing existing projects. The original prototype data can also be migrated from this browser when prompted.

## Storage and backups

Data lives in `.branch/workspace.json`, with the previous successful revision in `.branch/workspace.json.bak`. Both are excluded from Git. **Export workspace** saves a portable JSON copy. Conversation content stays local unless you explicitly connect an AI client and ask it to read that content.

The app prevents concurrent overwrites using revision checks and a single-writer lock. If another client changed the workspace, refresh and retry. If storage is corrupt, startup preserves the file and reports the error. Stop the server before manually restoring a backup. Do not delete a lock belonging to a running server.

## Connect Codex or another local MCP client

The bridge exposes five working tools: `branch_list_projects`, `branch_read_conversation`, `branch_create`, `branch_add_message`, and `branch_merge`. The app's **Connect AI client** dialog provides JSON configuration with the correct executable and file paths for this computer.

For Codex, run the following in this project directory to register the bridge. This is an optional user-run setup step; the app does not modify your Codex configuration automatically.

```powershell
codex mcp add branch --env BRANCH_URL=http://127.0.0.1:5180 -- node "$((Get-Location).Path)\mcp-server.mjs"
```

Keep `npm start` running. After your client loads the server, try:

> List my Branch projects. Read the conversation I select, help me continue it, and save your response to that Branch conversation.

The MCP bridge uses local stdio. ChatGPT-hosted installation and automatic access to existing ChatGPT/Codex chat history are not included. Import ChatGPT history through an export. The local web composer saves messages; AI replies require a connected client. Merging conversation context does not merge Git branches or modify original host chats.

Official setup reference: [MCP in Codex](https://learn.chatgpt.com/docs/extend/mcp).

## Verify

```sh
npm run check
npm test
```

Tests cover frozen branch context, selective merges, source integrity, ChatGPT fork imports, validation, disk persistence, backup behavior, revision conflicts, single-writer locking, and an end-to-end HTTP/MCP workflow. See [architecture](docs/architecture.md) for the data model, integration boundaries, and remaining work.
