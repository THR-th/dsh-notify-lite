import { spawn } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const helperPath = fileURLToPath(new URL('../native/DshNotifyPet.exe', import.meta.url));
const kinds = new Set(['completed', 'approval', 'question', 'error', 'aborted', 'max-tokens', 'test', 'unknown']);
const queueLimit = 10;
const validKey = (key) => typeof key === 'string' && key.length > 0 && key.length <= 1024;

// The helper owns all visible copy. Only fixed status identifiers and opaque
// cancellation keys cross the private pipe; no conversation text is included.
export function notificationPayload(event, options = {}) {
  if (!validKey(event?.key)) return undefined;
  const kind = kinds.has(event?.kind) ? event.kind : 'unknown';
  return {
    op: 'show', kind, key: event.key, sound: options.sound !== false,
    seconds: ['approval', 'question'].includes(kind) ? 20 : 12,
  };
}

export function createWindowsSender(options = {}, dependencies = {}) {
  const launch = dependencies.spawn ?? spawn;
  const later = dependencies.setTimeout ?? setTimeout;
  const stop = dependencies.clearTimeout ?? clearTimeout;
  const logger = dependencies.logger;
  const queue = [];
  let active;
  let disposed = false;

  const warn = (message) => {
    try { logger?.warn?.(message); } catch { /* Logging must not affect DSH. */ }
  };
  const killOwn = (state) => {
    try { state.child.kill(); } catch { /* Already exited or inaccessible. */ }
  };
  const fail = (state) => {
    if (state.failed || state.closed) return;
    state.failed = true;
    queue.length = 0;
    warn('dsh-notify-lite: notification helper could not receive a notification');
    try { state.child.stdin?.destroy(); } catch { /* Ignore pipe shutdown errors. */ }
    killOwn(state);
  };

  const flush = () => {
    const state = active;
    if (disposed || !state || state.closed || state.failed || state.waitingDrain) return;
    while (queue.length && active === state && !state.failed && !state.closed) {
      const command = queue.shift();
      try {
        // JSON.stringify escapes embedded quotes and newlines; the helper reads
        // data rather than shell syntax. A false return still accepts this line.
        if (!state.child.stdin.write(JSON.stringify(command) + '\n')) {
          state.waitingDrain = true;
          return;
        }
      } catch {
        fail(state);
        return;
      }
    }
  };

  const start = () => {
    if (disposed || active) return;
    try {
      // Do not inherit API keys or other credentials from the DSH environment.
      const systemRoot = process.env.SystemRoot || process.env.WINDIR || 'C:\\Windows';
      const child = launch(helperPath, [], {
        shell: false, windowsHide: true,
        stdio: ['pipe', 'ignore', 'pipe'],
        env: { SystemRoot: systemRoot, WINDIR: systemRoot, TEMP: 'D:\\DSH-data\\tmp', TMP: 'D:\\DSH-data\\tmp' },
      });
      const state = { child, failed: false, closed: false, waitingDrain: false, shutdownTimer: undefined };
      active = state;
      const finish = (code) => {
        if (state.closed) return;
        state.closed = true;
        if (state.shutdownTimer !== undefined) stop(state.shutdownTimer);
        if (active === state) { active = undefined; queue.length = 0; }
        if (code && !state.failed && !disposed) warn('dsh-notify-lite: notification helper stopped unexpectedly');
        // Deliberately do not restart here: only a later event may start again.
      };
      child.once('exit', finish);
      child.once('close', finish);
      child.on('error', () => {
        fail(state);
        // A process that never spawned has no exit event, only error/close.
        if (child.pid === undefined) finish();
      });
      child.stdin?.on('error', () => fail(state));
      child.stdin?.on('drain', () => {
        if (active !== state || state.closed || state.failed || disposed) return;
        state.waitingDrain = false;
        flush();
      });
      // Drain without exposing the helper's raw output or blocking its pipe.
      child.stderr?.on('error', () => {});
      child.stderr?.resume?.();
      child.unref?.();
      if (!child.stdin || typeof child.stdin.write !== 'function') fail(state);
    } catch {
      active = undefined;
      queue.length = 0;
      warn('dsh-notify-lite: notification helper could not start');
    }
  };

  const enqueue = (command) => {
    if (queue.length >= queueLimit) {
      // Preserve pending cancellation commands whenever a show can be dropped.
      const firstShow = queue.findIndex((entry) => entry.op === 'show');
      queue.splice(firstShow >= 0 ? firstShow : 0, 1);
    }
    queue.push(command);
  };

  return {
    send(event) {
      if (disposed) return;
      const payload = notificationPayload(event, options);
      if (!payload || active?.failed || active?.closed) return;
      // Update an unsent reminder rather than growing the transport queue.
      const pending = queue.findIndex((command) => command.op === 'show' && command.key === payload.key);
      if (pending >= 0) queue.splice(pending, 1);
      enqueue(payload);
      start();
      flush();
    },
    cancel(key) {
      if (disposed || !validKey(key)) return;
      for (let i = queue.length - 1; i >= 0; i--) if (queue[i].key === key) queue.splice(i, 1);
      if (!active || active.failed || active.closed) return;
      enqueue({ op: 'cancel', key });
      flush();
    },
    dispose() {
      if (disposed) return;
      disposed = true;
      queue.length = 0;
      const state = active;
      if (!state || state.closed) return;
      // Quit follows any already accepted pipe writes. Nothing is replayed.
      try {
        if (!state.failed) state.child.stdin?.write(JSON.stringify({ op: 'quit' }) + '\n');
        state.child.stdin?.end();
      } catch { fail(state); }
      if (state.closed) return;
      state.shutdownTimer = later(() => {
        if (!state.closed) killOwn(state);
      }, 1000);
      state.shutdownTimer?.unref?.();
    },
  };
}
