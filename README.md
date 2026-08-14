# LogViewer

A WPF tail utility for text logs and Windows Event Logs, with live tailing, highlighting, bookmarks,
directory/wildcard watching, full-file search, block-diff/similarity analysis, and an optional embedded
MCP (Model Context Protocol) server so an AI agent can analyze the logs the app is tailing — discovering
recurring message patterns and ranking which functions/call-sites are repeatedly logging errors. The MCP
server is disabled by default; enable it and set its port in Settings. See `PLAN.md` for the full
architecture.
