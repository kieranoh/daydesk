/**
 * Structured Logger Utility
 * Provides logging with different levels and automatic masking of sensitive data.
 */

type LogLevel = 'DEBUG' | 'INFO' | 'WARN' | 'ERROR';

const MASKED = '***MASKED***';
const CIRCULAR = '[Circular]';

const SENSITIVE_KEYS = new Set([
  'args',
  'argument',
  'arguments',
  'attachment',
  'authorization',
  'body',
  'checklist',
  'content',
  'cookie',
  'email',
  'location',
  'note',
  'password',
  'request',
  'response',
  'secret',
  'session',
  'title',
  'token',
  'uid',
  'url',
  'virtual_user_attendees',
  'x-csrf-token',
  '_session_id',
]);

const SENSITIVE_KEY_PATTERN = /(password|passwd|secret|token|csrf|cookie|session|authorization|email|\buid\b|content|title|note|location|url|attachment|checklist|virtual_user_attendees|body|response|args?)/i;

function isSensitiveKey(key: string): boolean {
  const normalized = key.toLowerCase();
  return SENSITIVE_KEYS.has(normalized) || SENSITIVE_KEY_PATTERN.test(normalized);
}

function sanitizeString(value: string): string {
  return value
    .replace(/("(?:password|passwd|secret|token|csrf|cookie|session|authorization|email|uid|content|title|note|location|url)"\s*:\s*")[^"]*(")/gi, `$1${MASKED}$2`)
    .replace(/(_session_id=)[^;\s]+/gi, `$1${MASKED}`)
    .replace(/(x-csrf-token\s*[:=]\s*)[^\s,;}]+/gi, `$1${MASKED}`)
    .replace(/(authorization\s*[:=]\s*bearer\s+)[^\s,;}]+/gi, `$1${MASKED}`)
    .replace(/\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b/gi, MASKED);
}

function sanitizeError(error: Error, seen: WeakSet<object>): Record<string, unknown> {
  const sanitized: Record<string, unknown> = {
    name: error.name,
    message: sanitizeString(error.message),
  };

  const statusCode = (error as { statusCode?: unknown }).statusCode;
  if (typeof statusCode === 'number') {
    sanitized.statusCode = statusCode;
  }

  const response = (error as { response?: unknown }).response;
  if (response !== undefined) {
    sanitized.response = MASKED;
  }

  for (const [key, value] of Object.entries(error)) {
    if (key in sanitized) continue;
    sanitized[key] = isSensitiveKey(key) ? MASKED : sanitizeForLogging(value, seen);
  }

  return sanitized;
}

export function sanitizeForLogging(data: unknown, seen: WeakSet<object> = new WeakSet()): unknown {
  if (typeof data === 'string') {
    return sanitizeString(data);
  }

  if (data === null || typeof data !== 'object') {
    return data;
  }

  if (data instanceof Error) {
    return sanitizeError(data, seen);
  }

  if (seen.has(data)) {
    return CIRCULAR;
  }
  seen.add(data);

  if (Array.isArray(data)) {
    return data.map((item) => sanitizeForLogging(item, seen));
  }

  const sanitized: Record<string, unknown> = {};
  for (const [key, value] of Object.entries(data)) {
    sanitized[key] = isSensitiveKey(key) ? MASKED : sanitizeForLogging(value, seen);
  }

  seen.delete(data);
  return sanitized;
}

export function summarizeToolArguments(args: unknown): Record<string, unknown> {
  if (!args || typeof args !== 'object' || Array.isArray(args)) {
    return { argument_type: Array.isArray(args) ? 'array' : typeof args };
  }

  const argumentKeys = Object.keys(args).sort();
  return {
    argument_count: argumentKeys.length,
    argument_keys: argumentKeys,
  };
}

class Logger {
  private level: LogLevel;

  constructor(level: LogLevel = 'INFO') {
    this.level = level;
  }

  private shouldLog(level: LogLevel): boolean {
    const levels: LogLevel[] = ['DEBUG', 'INFO', 'WARN', 'ERROR'];
    return levels.indexOf(level) >= levels.indexOf(this.level);
  }

  private log(level: LogLevel, message: string, meta?: unknown): void {
    if (!this.shouldLog(level)) return;

    const timestamp = new Date().toISOString();
    const sanitizedMeta = meta === undefined ? undefined : sanitizeForLogging(meta);

    const logEntry = {
      timestamp,
      level,
      message,
      ...(sanitizedMeta !== undefined && { meta: sanitizedMeta }),
    };

    const output = JSON.stringify(logEntry);

    // MCP uses stdout for JSON-RPC, so log to stderr.
    console.error(output);
  }

  debug(message: string, meta?: unknown): void {
    this.log('DEBUG', message, meta);
  }

  info(message: string, meta?: unknown): void {
    this.log('INFO', message, meta);
  }

  warn(message: string, meta?: unknown): void {
    this.log('WARN', message, meta);
  }

  error(message: string, meta?: unknown): void {
    this.log('ERROR', message, meta);
  }
}

// Export singleton instance.
export const logger = new Logger(
  (process.env.LOG_LEVEL as LogLevel) || 'INFO'
);
