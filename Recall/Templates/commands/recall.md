---
description: Search semantic memory for recent work context
argument-hint: [[time period] [search query]]
---

Parse arguments to extract optional time period and search query from: $ARGUMENTS

Time period formats: 15m (15 minutes), 1h (1 hour), 2d (2 days), 1w (1 week)
Examples:
- `/recall 15m` - Last 15 minutes of work
- `/recall 1h authentication` - Last hour's work on authentication
- `/recall bug fix` - Search for "bug fix" across all memories

Use the recall MCP tool to search memories. If a time period is specified, filter results to only show memories from that timeframe. If no arguments provided, search for recent work and important decisions.

Present results in a clear, concise format highlighting:
- What was done
- Why it matters
- Key decisions made
- Current blockers or next steps

Focus on actionable context that helps resume work quickly.
