Event notification design adapted from dsh-notify-on-complete by Luozy (MIT).
Reference revision: 4cf3277c50cfeb930b75a8d5ecdfcf161b0246d3
https://github.com/pitetow/dsh-notify-on-complete/tree/4cf3277c50cfeb930b75a8d5ecdfcf161b0246d3

Local adaptation: JavaScript runtime without build/runtime dependencies; Windows
WPF attention cards; no HTTP routes or webhooks; no conversation text in cards;
auto-approval suppression, pending-interaction cancellation, duplicate handling,
per-session reminders, and no changes to approval/question providers.

Version-specific event shapes verified against the installed DSH 0.2.1-rc.2:
@deepseek-ai/dsh-agent-loop: agent/status becomes idle at quiescence.
@deepseek-ai/dsh-user-approval: session approval/asked and approval/decided events.
@deepseek-ai/dsh-user-questions: ask_user_question tool/call and tool/ptc-dispatch.

API sources:
https://nodejs.org/docs/latest-v24.x/api/child_process.html#child_processspawncommand-args-options
https://learn.microsoft.com/en-us/dotnet/api/system.windows.window.showactivated
1.1.0 local adaptation: replaced hidden PowerShell execution with a locally
compiled, fixed .NET Framework WPF helper and a private bounded JSON pipe.
Original chibi character generated with OpenAI image_gen using a user-supplied
style reference. No remote image lookup or character service at runtime.

Native window APIs:
https://learn.microsoft.com/dotnet/api/system.windows.window.showactivated
https://learn.microsoft.com/dotnet/api/system.windows.window.allowstransparency
https://learn.microsoft.com/dotnet/api/system.windows.window.dragmove
https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-showwindow
https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-setforegroundwindow
