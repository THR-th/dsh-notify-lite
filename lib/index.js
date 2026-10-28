import { createEngine } from './engine.js';
import { createWindowsSender } from './windows.js';

export const name = 'dsh-notify-lite';
export const inject = [];

export function bindObservers(ctx, engine) {
  ctx.on('session/event', (session, event) => engine.onSessionEvent(session, event));
  ctx.on('agent/status', (payload) => engine.onAgentStatus(payload));
  ctx.on('session/disposed', (session) => engine.onSessionDisposed(session));
}

export function apply(ctx, config = {}) {
  if (process.platform !== 'win32' || config.enabled === false) return;
  // No service injection is needed; errors contain no event or credential data.
  const logger = { warn: (message) => console.warn(message) };
  const sender = createWindowsSender(config, { logger });
  const engine = createEngine((event) => sender.send(event), config, {
    cancel: (key) => sender.cancel(key),
    onError: () => logger.warn('dsh-notify-lite: notification delivery failed'),
  });
  // Passive session observers only; no approval/request interceptor or grant API.
  bindObservers(ctx, engine);
  ctx.effect(() => () => { engine.dispose(); sender.dispose(); }, 'dsh-notify-lite: cleanup');
  if (config.testOnStart === true) {
    const timer = setTimeout(() => sender.send({ kind: 'test', key: 'startup-test', sessionId: '' }), 3000);
    timer.unref();
    ctx.effect(() => () => clearTimeout(timer), 'dsh-notify-lite: test timer');
  }
}
