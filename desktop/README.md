# Branch for Codex desktop (Windows)

This is a native WPF overlay for the Codex desktop app. It draws the conversation map directly in a Windows window that can follow Codex's central content area. It uses no WebView, browser, localhost site, or HTTP server. Codex's own project sidebar and message composer remain exposed around the overlay. Press **Ctrl+Shift+Space** to hide or restore the map. If that shortcut is already taken, the taskbar remains the fallback.

The overlay is a companion process, not an injected modification to Codex. The MCP desktop bridge adds a command to open it from Codex. It does not claim an undocumented native panel extension API.

## Start

Requirements: Windows, .NET 8 Desktop Runtime, and Codex CLI (the Codex-bundled executable is detected automatically when available). Building from source also requires the .NET 8 SDK. There are no NuGet package dependencies.

Double-click `desktop/start-desktop.cmd`, or build and launch:

```powershell
dotnet build desktop/Branch.Desktop/Branch.Desktop.csproj -c Release --configfile desktop/Branch.Desktop/NuGet.Config
Start-Process desktop/Branch.Desktop/bin/Release/net8.0-windows/Branch.Desktop.exe
```

The first launch reads local saved chat metadata through Codex App Server. Choose a project and then a conversation to inspect completed user/assistant messages. Select your Codex window in the window picker and enable **Follow Codex**. **Alignment** adjusts the space reserved for the native sidebar, top bar, and composer. Detach by clearing **Follow Codex**. The overlay is an owned window, not a system-wide always-on-top window. The layout follows the selected host; it does not automatically switch projects when the user changes Codex's native sidebar.

Click **Open in Codex** to open the original native chat and hide the map. Continue using Codex's existing composer. Multiple Codex windows require choosing which window to follow. Project grouping uses the working directory reported by the local App Server; custom sidebar sections and remote-host chats are not mapped in this version.

## Branch and merge

- **Branch after this turn** creates a real native Codex fork through a completed turn. Codex's documented API provides turn boundaries; this overlay does not promise forks midway through a user/assistant turn.
- Select two cards and choose **Compare** to view shared message identities and unique additions. Text with different IDs is not assumed to be shared history.
- Select two to eight cards and choose **Merge selected**. Review source messages, deselect anything you do not want to carry, and optionally write a handoff brief. **Create chat and send context** creates a new native chat and sends exactly the reviewed context, starting an AI reply using your account. It is not triggered automatically by viewing or comparing chats. The new chat starts with read-only sandbox settings and an instruction to avoid tools. This client rejects server-initiated approval/tool requests it cannot handle.
- Merge source links are saved in `%LOCALAPPDATA%\Branch\desktop.json`. The submitted initial message contains source chat IDs, turn IDs, and the selected text. Later changes in source chats do not rewrite that context. Original chats and Git worktrees remain unchanged.

Only completed user/assistant text is copied into the review. Non-text attachments are marked; raw reasoning and tool output are omitted. A merge is limited to 180,000 characters and requires at least one selected message from each source. Large histories may still exceed a model's context limit; the app reports the resulting error rather than truncating silently.

## Open the overlay through Codex

Register the local desktop MCP bridge:

```powershell
powershell -File desktop/install-codex-plugin.ps1
```

This builds the overlay and registers `branch-desktop` in your Codex MCP configuration. After Codex loads it, ask **“Open my Branch conversation map.”** The bridge exposes only launch and status tools. It does not expose chat contents to an unrelated chat. Manual registration is also possible:

```powershell
codex mcp add branch-desktop -- node "C:\absolute\path\to\branch-conversation-map\desktop\plugin.mjs"
```

This is a local desktop integration, not a submission to the universal plugin directory. It needs no lifecycle hooks, host UI patching, or changes to Codex's application files.

## Verify

```powershell
Start-Process desktop/Branch.Desktop/bin/Release/net8.0-windows/Branch.Desktop.exe -ArgumentList '--self-test','.branch/native-tests.json' -Wait
Start-Process desktop/Branch.Desktop/bin/Release/net8.0-windows/Branch.Desktop.exe -ArgumentList '--check-codex','.branch/native-connection.json' -Wait
```

The first command runs 13 native model/parser/geometry checks. The second performs a read-only handshake, lists chats, and previews a chat in the current project when available. It sends no messages and creates no chats. Reports contain counts rather than chat contents. `--preview PATH.png` renders a sample-data native layout for visual QA. `--demo` opens a sample-data window. Live fork and merge writes require separate interactive verification; they are not exercised against your real chats by these checks.

Official integration references: [Codex App Server](https://learn.chatgpt.com/docs/app-server), [desktop deep links](https://learn.chatgpt.com/docs/reference/commands), [local MCP setup](https://learn.chatgpt.com/docs/extend/mcp).
