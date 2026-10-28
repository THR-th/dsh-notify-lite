import test from 'node:test';
import assert from 'node:assert/strict';
import { EventEmitter } from 'node:events';
import { createWindowsSender, notificationPayload } from '../lib/windows.js';

function fixture({ throwFirst = false } = {}) {
  const launches = [], children = [], warnings = [], timers = new Map();
  let attempts = 0, seq = 0;
  const sender = createWindowsSender({ executable: 'C:\\untrusted\\other.exe' }, {
    spawn(file, args, options) {
      attempts++;
      if (throwFirst && attempts === 1) throw new Error('PRIVATE_SPAWN_ERROR');
      const child = new EventEmitter();
      child.pid = 8000 + attempts;
      child.writes = [];
      child.kills = 0;
      child.stdin = new EventEmitter();
      child.stdin.write = (line) => {
        if (child.throwWrite) throw new Error('PRIVATE_PIPE_ERROR');
        child.writes.push(line);
        return !child.backpressure;
      };
      child.stdin.end = () => { child.ended = true; };
      child.stdin.destroy = () => { child.destroyed = true; };
      child.stderr = new EventEmitter();
      child.stderr.resume = () => { child.stderrDrained = true; };
      child.kill = () => {
        child.kills++;
        if (!child.stopped) { child.stopped = true; child.emit('exit', 0); child.emit('close', 0); }
      };
      child.unref = () => {};
      launches.push({ file, args, options }); children.push(child);
      return child;
    },
    logger: { warn(message) { warnings.push(message); } },
    setTimeout(fn, delay) { const id = ++seq; timers.set(id, { fn, delay }); return id; },
    clearTimeout(id) { timers.delete(id); },
  });
  return {
    sender, launches, children, warnings, timers,
    runTimers() { for (const [id, timer] of [...timers]) { timers.delete(id); timer.fn(); } },
  };
}
const notice = (key = 'one', kind = 'completed') => ({ kind, key, sessionId: 'PRIVATE_SESSION', title: 'PRIVATE_TITLE', message: 'PRIVATE_PROMPT' });
const commands = (child) => child.writes.map((line) => JSON.parse(line));

test('sender launches one fixed native helper without shell or inherited credentials', () => {
  const secretName = 'DSH_NOTIFY_TEST_SECRET';
  const saved = process.env[secretName];
  process.env[secretName] = 'PRIVATE_API_KEY';
  const f = fixture();
  try {
    f.sender.send(notice()); f.sender.send(notice('two', 'approval')); f.sender.send(notice('three', 'question'));
    assert.equal(f.launches.length, 1);
    const launch = f.launches[0];
    assert.match(launch.file, /[\\/]native[\\/]DshNotifyPet\.exe$/);
    assert.equal(/powershell|cmd\.exe|other\.exe|https?:/i.test(launch.file), false);
    assert.deepEqual(launch.args, []);
    assert.equal(launch.options.shell, false); assert.equal(launch.options.windowsHide, true);
    assert.deepEqual(launch.options.stdio, ['pipe', 'ignore', 'pipe']);
    assert.deepEqual(Object.keys(launch.options.env).sort(), ['SystemRoot', 'TEMP', 'TMP', 'WINDIR']);
    assert.equal(launch.options.env.TEMP, 'D:\\DSH-data\\tmp');
    assert.equal(JSON.stringify(launch).includes('PRIVATE_API_KEY'), false);
    assert.equal(f.children[0].stderrDrained, true);
    assert.deepEqual(commands(f.children[0]).map((x) => x.kind), ['completed', 'approval', 'question']);
  } finally {
    if (saved === undefined) delete process.env[secretName]; else process.env[secretName] = saved;
    f.sender.dispose(); f.runTimers();
  }
});

test('wire input is bounded JSON data with whitelisted status and no conversation content', () => {
  const f = fixture();
  const key = 'opaque"\n; Start-Process malicious';
  f.sender.send(notice(key, 'untrusted-kind'));
  assert.equal(f.children[0].writes[0].split('\n').length, 2);
  assert.deepEqual(commands(f.children[0])[0], { op: 'show', kind: 'unknown', key, sound: true, seconds: 12 });
  assert.equal(f.children[0].writes[0].includes('PRIVATE_'), false);
  f.sender.send(notice('x'.repeat(1025))); f.sender.send(notice('')); f.sender.send({ key: 1 });
  assert.equal(f.children[0].writes.length, 1);
  assert.equal(notificationPayload(notice('approval', 'approval'), { sound: false }).sound, false);
  assert.equal(notificationPayload(notice('approval', 'approval')).seconds, 20);
  f.sender.dispose(); f.runTimers();
});

test('cancel removes pending transport entries and never starts a helper on its own', () => {
  const f = fixture();
  f.sender.cancel('absent'); assert.equal(f.launches.length, 0);
  f.sender.send(notice());
  f.sender.cancel('one');
  assert.deepEqual(commands(f.children[0]), [notificationPayload(notice()), { op: 'cancel', key: 'one' }]);
  assert.equal(f.launches.length, 1);
  f.sender.cancel('x'.repeat(1025)); assert.equal(f.children[0].writes.length, 2);
  f.sender.dispose(); f.runTimers();
});

test('backpressure bounds unsent writes to ten and cancellation wins over an unsent show', () => {
  const f = fixture();
  f.sender.send(notice('initial'));
  const child = f.children[0];
  child.backpressure = true;
  f.sender.send(notice('blocked'));
  for (let i = 0; i < 20; i++) f.sender.send(notice('queued-' + i));
  f.sender.cancel('queued-19');
  assert.equal(child.writes.length, 2);
  child.backpressure = false; child.stdin.emit('drain');
  const rest = commands(child).slice(2);
  assert.equal(rest.length, 10);
  assert.equal(rest.some((x) => x.op === 'show' && x.key === 'queued-19'), false);
  assert.deepEqual(rest.at(-1), { op: 'cancel', key: 'queued-19' });
  f.sender.dispose(); f.runTimers();
});

test('a normal helper exit restarts only on a later event', () => {
  const f = fixture();
  f.sender.send(notice()); f.children[0].emit('exit', 2); f.children[0].emit('close', 2);
  assert.equal(f.launches.length, 1);
  f.sender.cancel('one'); assert.equal(f.launches.length, 1);
  f.sender.send(notice('later'));
  assert.equal(f.launches.length, 2);
  assert.deepEqual(commands(f.children[1]), [notificationPayload(notice('later'))]);
  f.sender.dispose(); f.runTimers();
});

test('stdin and process errors cannot escape into DSH or produce automatic retries', () => {
  const f = fixture();
  f.sender.send(notice());
  assert.doesNotThrow(() => f.children[0].stdin.emit('error', new Error('PRIVATE_PIPE_ERROR')));
  assert.equal(f.children[0].kills, 1); assert.equal(f.launches.length, 1);
  f.sender.send(notice('second'));
  assert.equal(f.launches.length, 2);
  assert.doesNotThrow(() => f.children[1].emit('error', new Error('PRIVATE_SPAWN_ERROR')));
  assert.doesNotThrow(() => f.children[1].emit('error', new Error('PRIVATE_KILL_ERROR')));
  assert.equal(f.launches.length, 2);
  assert.equal(f.warnings.join(' ').includes('PRIVATE_'), false);
  f.sender.send(notice('third')); assert.equal(f.launches.length, 3);
  f.sender.dispose(); f.runTimers();
});

test('synchronous launch or write failure is dropped and the next event may recover', () => {
  const f = fixture({ throwFirst: true });
  assert.doesNotThrow(() => f.sender.send(notice()));
  assert.equal(f.launches.length, 0);
  f.sender.send(notice('retry')); assert.equal(f.launches.length, 1);
  f.children[0].throwWrite = true;
  assert.doesNotThrow(() => f.sender.send(notice('failed-write')));
  assert.equal(f.children[0].kills, 1); assert.equal(f.launches.length, 1);
  f.sender.send(notice('final')); assert.equal(f.launches.length, 2);
  assert.deepEqual(commands(f.children[1]), [notificationPayload(notice('final'))]);
  f.sender.dispose(); f.runTimers();
});

test('dispose sends quit, ends stdin, then only terminates its own child after one second', () => {
  const f = fixture();
  f.sender.send(notice()); const child = f.children[0];
  f.sender.dispose(); f.sender.dispose(); f.sender.send(notice('ignored')); f.sender.cancel('one');
  assert.deepEqual(commands(child).at(-1), { op: 'quit' });
  assert.equal(child.ended, true); assert.equal(child.kills, 0);
  assert.equal(f.launches.length, 1); assert.equal(f.timers.size, 1);
  assert.equal([...f.timers.values()][0].delay, 1000);
  f.runTimers(); assert.equal(child.kills, 1);
  const idle = fixture(); idle.sender.dispose(); assert.equal(idle.launches.length, 0);
});

test('graceful helper shutdown cancels the forced termination timer', () => {
  const f = fixture(); f.sender.send(notice()); f.sender.dispose();
  f.children[0].emit('exit', 0); assert.equal(f.timers.size, 0);
  f.runTimers(); assert.equal(f.children[0].kills, 0);
});
