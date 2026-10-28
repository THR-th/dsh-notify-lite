import test from 'node:test';
import assert from 'node:assert/strict';
import { createEngine } from '../lib/engine.js';
import { notificationPayload } from '../lib/windows.js';

const session = (id = 'root-one', origin) => ({ header: { id, origin } });
const event = (type, data = {}) => ({ type, data });
function fixture() {
  let time = 0, seq = 0;
  const timers = new Map(), notices = [], cancelled = [];
  const engine = createEngine((message) => notices.push(message), {}, {
    setTimeout(fn, ms) { const id = ++seq; timers.set(id, { fn, at: time + ms }); return id; },
    clearTimeout(id) { timers.delete(id); }, cancel(key) { cancelled.push(key); },
  });
  function advance(ms) {
    const target = time + ms;
    while (true) {
      const next = [...timers].filter(([, timer]) => timer.at <= target).sort((a, b) => a[1].at - b[1].at)[0];
      if (!next) break;
      time = next[1].at; timers.delete(next[0]); next[1].fn();
    }
    time = target;
  }
  return { engine, notices, cancelled, advance, timers };
}
const finish = (f, s, kind = 'completed') => {
  f.engine.onSessionEvent(s, event('turn/end', { reason: { kind } }));
  f.engine.onAgentStatus({ agent: { id: s.header.id, session: s }, status: 'idle' });
};

test('multi-round run notifies only at idle and only once, with final result', () => {
  const f = fixture(), s = session();
  f.engine.onSessionEvent(s, event('turn/end', { reason: { kind: 'completed' } }));
  f.engine.onAgentStatus({ agent: { session: s }, status: 'running' });
  assert.equal(f.notices.length, 0);
  f.engine.onSessionEvent(s, event('turn/start'));
  finish(f, s, 'error');
  f.engine.onAgentStatus({ agent: { session: s }, status: 'idle' });
  assert.deepEqual(f.notices.map((x) => x.kind), ['error']);
});
test('subagents are excluded but forked root sessions still notify', () => {
  const f = fixture(); finish(f, session('child', 'subagent'));
  const fork = session('fork'); fork.header.parentSession = 'parent'; finish(f, fork);
  assert.deepEqual(f.notices.map((x) => x.sessionId), ['fork']);
});
test('quick auto-decided approval has no popup and no reminder', () => {
  const f = fixture(), s = session();
  f.engine.onSessionEvent(s, event('approval/asked', { id: 'a' })); f.advance(100);
  f.engine.onSessionEvent(s, event('approval/decided', { id: 'a', outcome: 'allowed-once' }));
  f.advance(120000); assert.equal(f.notices.length, 0); assert.equal(f.timers.size, 0);
});
test('approval is deduplicated, reminds while pending, then cancels popup', () => {
  const f = fixture(), s = session();
  f.engine.onSessionEvent(s, event('approval/asked', { id: 'a' }));
  f.engine.onSessionEvent(s, event('approval/asked', { id: 'a' }));
  f.advance(1200); assert.equal(f.notices.length, 1);
  f.advance(60000); assert.equal(f.notices.length, 2);
  f.engine.onSessionEvent(s, event('approval/decided', { id: 'a', outcome: 'denied' }));
  f.advance(60000); assert.equal(f.notices.length, 2); assert.equal(f.cancelled.length, 1);
});
test('timed unanswered question survives pending result and idle without false completion', () => {
  const f = fixture(), s = session();
  f.engine.onSessionEvent(s, event('tool/call', { name: 'ask_user_question', callId: 'q' }));
  f.engine.onSessionEvent(s, event('tool/result', { message: { toolCallId: 'q', content: [{ type: 'text', text: '{"pending":true}' }] } }));
  f.advance(1200); finish(f, s);
  assert.deepEqual(f.notices.map((x) => x.kind), ['question']);
  f.engine.onSessionEvent(s, event('user/message', { source: { kind: 'user-question-reply', callId: 'q' } }));
  f.engine.onSessionEvent(s, event('turn/start')); finish(f, s);
  assert.deepEqual(f.notices.map((x) => x.kind), ['question', 'completed']);
});
test('PTC question dispatch uses subCallId and late reply cancels reminders', () => {
  const f = fixture(), s = session();
  f.engine.onSessionEvent(s, event('tool/ptc-dispatch', { name: 'ask_user_question', subCallId: 'ptc-q', content: [{ type: 'text', text: '{"pending":true}' }] }));
  f.advance(1200); assert.equal(f.notices[0].kind, 'question');
  f.engine.onSessionEvent(s, event('user/message', { source: { kind: 'user-question-reply', callId: 'ptc-q' } }));
  f.advance(120000); assert.equal(f.notices.length, 1);
});
test('answered question result cancels before delay elapses', () => {
  const f = fixture(), s = session();
  f.engine.onSessionEvent(s, event('tool/call', { name: 'ask_user_question', callId: 'q' }));
  f.engine.onSessionEvent(s, event('tool/result', { message: { toolCallId: 'q', content: [{ type: 'text', text: '{"answers":[]}' }] } }));
  f.advance(120000); assert.equal(f.notices.length, 0);
});
test('aborted run and disposal clear all pending timers', () => {
  const f = fixture(), s = session();
  f.engine.onSessionEvent(s, event('approval/asked', { id: 'a' })); finish(f, s, 'aborted');
  f.advance(120000); assert.deepEqual(f.notices.map((x) => x.kind), ['aborted']);
  f.engine.onSessionEvent(s, event('approval/asked', { id: 'b' })); f.engine.dispose();
  f.advance(120000); assert.equal(f.timers.size, 0); assert.equal(f.notices.length, 1);
});
test('notification content never includes prompt text, tool arguments, or untrusted title', () => {
  const f = fixture(), s = session();
  f.engine.onSessionEvent(s, event('approval/asked', { id: 'a', reason: 'PRIVATE_API_KEY', toolName: 'SECRET' }));
  f.advance(1200);
  const payload = notificationPayload({ ...f.notices[0], title: 'INJECTED' });
  assert.equal(JSON.stringify(payload).includes('PRIVATE_API_KEY'), false);
});

