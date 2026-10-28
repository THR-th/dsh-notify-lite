/**
 * Event aggregation adapted from Luozy/dsh-notify-on-complete (MIT).
 * See NOTICE.md for pinned upstream and installed DSH source references.
 */
export function createEngine(notify, options = {}, dependencies = {}) {
  const later = dependencies.setTimeout ?? setTimeout;
  const stop = dependencies.clearTimeout ?? clearTimeout;
  const repeatMs = Math.max(30, Math.min(600, Number(options.repeatSeconds) || 60)) * 1000;
  const delayMs = dependencies.delayMs ?? 1200;
  const ended = new Map();
  const roots = new Set();
  const pending = new Map();
  let disposed = false;

  const rootId = (session) => {
    if (!session || session.header?.origin === 'subagent') return null;
    const id = session.header?.id ?? session.id;
    return typeof id === 'string' && id.length > 0 && id.length <= 200 ? id : null;
  };
  const keyOf = (sid, kind, id) => JSON.stringify([sid, kind, String(id)]);
  const cancel = (key) => {
    const item = pending.get(key);
    if (!item) return;
    stop(item.timer);
    pending.delete(key);
    dependencies.cancel?.(key);
  };
  const clearSession = (sid) => {
    for (const [key, item] of pending) if (item.sid === sid) cancel(key);
  };
  const safeNotify = (payload) => {
    try { notify(payload); } catch { dependencies.onError?.(); }
  };
  const startPending = (sid, kind, id) => {
    if (id === undefined || id === null || String(id).length > 200) return;
    const key = keyOf(sid, kind, id);
    if (pending.has(key)) return;
    const item = { sid, kind, timer: undefined };
    const fire = () => {
      if (disposed || !pending.has(key)) return;
      safeNotify({ kind, sessionId: sid, key });
      item.timer = later(fire, repeatMs);
      item.timer?.unref?.();
    };
    pending.set(key, item);
    item.timer = later(fire, delayMs);
    item.timer?.unref?.();
  };
  const isPendingResult = (content) => {
    if (!Array.isArray(content)) return false;
    const text = content.find((block) => block?.type === 'text')?.text;
    if (typeof text !== 'string' || text.length > 64000) return false;
    try { return JSON.parse(text)?.pending === true; } catch { return false; }
  };

  return {
    onSessionEvent(session, event) {
      if (disposed) return;
      const sid = rootId(session);
      if (!sid || !event) return;
      roots.add(sid);
      const data = event.data ?? {};
      switch (event.type) {
        case 'turn/start':
          ended.delete(sid);
          break;
        case 'turn/end': {
          const kind = typeof data.reason?.kind === 'string' ? data.reason.kind : 'unknown';
          ended.set(sid, kind);
          if (['error', 'aborted', 'max-tokens'].includes(kind)) clearSession(sid);
          break;
        }
        case 'approval/asked':
          startPending(sid, 'approval', data.id);
          break;
        case 'approval/decided':
          cancel(keyOf(sid, 'approval', data.id));
          break;
        case 'tool/call':
          if (data.name === 'ask_user_question') startPending(sid, 'question', data.callId);
          break;
        case 'tool/ptc-dispatch':
          if (data.name === 'ask_user_question' && !data.isError && isPendingResult(data.content)) {
            startPending(sid, 'question', data.subCallId);
          }
          break;
        case 'tool/result': {
          const message = data.message ?? data;
          if (!isPendingResult(message.content) && data.error?.code !== 'TOOL_OUTCOME_UNKNOWN') {
            cancel(keyOf(sid, 'question', message.toolCallId ?? data.callId));
          }
          break;
        }
        case 'user/message':
          if (data.source?.kind === 'user-question-reply') {
            cancel(keyOf(sid, 'question', data.source.callId));
          }
          break;
      }
    },
    onAgentStatus(payload) {
      if (disposed || payload?.status !== 'idle') return;
      const agent = payload.agent;
      if (agent?.session?.header?.origin === 'subagent') return;
      const sid = rootId(agent?.session) ?? agent?.id;
      if (!roots.has(sid) || !ended.has(sid)) return;
      const kind = ended.get(sid);
      ended.delete(sid);
      // A timed question can leave the agent idle while awaiting a late reply.
      if (kind === 'blocked' || [...pending.values()].some((item) => item.sid === sid)) return;
      safeNotify({ kind, sessionId: sid, key: JSON.stringify([sid, 'finished']) });
    },
    onSessionDisposed(session) {
      const sid = rootId(session);
      if (!sid) return;
      clearSession(sid);
      roots.delete(sid);
      ended.delete(sid);
    },
    dispose() {
      disposed = true;
      for (const key of [...pending.keys()]) cancel(key);
      ended.clear();
      roots.clear();
    },
  };
}
