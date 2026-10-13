import { describe, expect, it } from 'vitest';
import {
  addDays,
  formatTime,
  fromDateTimeLocalValue,
  startOfWeek,
  toDateTimeLocalValue,
  toDayKey
} from '../utils/dates';

describe('date helpers', () => {
  it('round-trips a local datetime input value through UTC', () => {
    const input = '2026-03-10T09:30';
    const iso = fromDateTimeLocalValue(input);

    expect(iso).toMatch(/Z$/);
    expect(toDateTimeLocalValue(iso)).toBe(input);
  });

  it('returns empty strings for missing or invalid input', () => {
    expect(toDateTimeLocalValue(null)).toBe('');
    expect(toDateTimeLocalValue('not-a-date')).toBe('');
    expect(fromDateTimeLocalValue('')).toBe('');
  });

  it('adds days without mutating the original date', () => {
    const original = new Date('2026-03-10T00:00:00Z');
    const next = addDays(original, 5);

    expect(next.getTime()).not.toBe(original.getTime());
    expect(original.getUTCDate()).toBe(10);
  });

  it('finds the Monday that starts the week', () => {
    // 2026-03-11 is a Wednesday; the week starts on Monday 2026-03-09.
    const week = startOfWeek(new Date(2026, 2, 11, 15, 30));

    expect(week.getDay()).toBe(1);
    expect(week.getDate()).toBe(9);
    expect(week.getHours()).toBe(0);
  });

  it('derives a stable day key from an ISO timestamp', () => {
    const key = toDayKey(new Date(2026, 2, 10, 23, 59).toISOString());
    expect(key).toBe('2026-03-10');
  });

  it('formats a time for the calendar', () => {
    const noon = new Date(2026, 2, 10, 12, 5);
    expect(formatTime(noon.toISOString())).toMatch(/12:05/);
  });
});
