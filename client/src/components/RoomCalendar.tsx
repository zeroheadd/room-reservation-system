import type { Reservation } from '../api/types';
import {
  addDays,
  formatDay,
  formatTime,
  startOfDay,
  startOfWeek,
  toDayKey
} from '../utils/dates';

/**
 * Week timeline for a single room. Reservations are laid out by their real start and end
 * times, so overlapping or back-to-back bookings are visible at a glance - the optional
 * "calendar/timeline view" from the task brief.
 */
export function RoomCalendar({
  weekStart,
  onChangeWeek,
  reservations
}: {
  weekStart: Date;
  onChangeWeek: (next: Date) => void;
  reservations: Reservation[];
}) {
  // The visible band always covers office hours; meetings outside it are clamped so they
  // still show up instead of silently disappearing.
  const dayStartHour = 7;
  const dayEndHour = 20;
  const totalHours = dayEndHour - dayStartHour;

  const days = Array.from({ length: 7 }, (_, index) => addDays(weekStart, index));
  const hourMarks = Array.from({ length: totalHours + 1 }, (_, index) => dayStartHour + index);

  const byDay = new Map<string, Reservation[]>();
  for (const reservation of reservations) {
    if (reservation.status === 'Cancelled') {
      continue;
    }

    const key = toDayKey(reservation.start);
    byDay.set(key, [...(byDay.get(key) ?? []), reservation]);
  }

  return (
    <div className="calendar">
      <div className="calendar-toolbar">
        <button type="button" className="btn btn-ghost" onClick={() => onChangeWeek(addDays(weekStart, -7))}>
          ← Previous week
        </button>
        <strong>
          {weekStart.toLocaleDateString(undefined, { day: '2-digit', month: 'short', year: 'numeric' })}
          {' – '}
          {addDays(weekStart, 6).toLocaleDateString(undefined, { day: '2-digit', month: 'short', year: 'numeric' })}
        </strong>
        <button type="button" className="btn btn-ghost" onClick={() => onChangeWeek(addDays(weekStart, 7))}>
          Next week →
        </button>
        <button
          type="button"
          className="btn btn-ghost"
          onClick={() => onChangeWeek(startOfWeek(new Date()))}
        >
          This week
        </button>
      </div>

      <div className="calendar-grid">
        <div className="calendar-hours">
          <div className="calendar-hours-head" />
          {hourMarks.map((hour) => (
            <div key={hour} className="calendar-hour">
              {String(hour).padStart(2, '0')}:00
            </div>
          ))}
        </div>

        {days.map((day) => {
          const key = toDayKey(startOfDay(day).toISOString());
          const items = byDay.get(key) ?? [];
          const isToday = key === toDayKey(new Date().toISOString());

          return (
            <div key={key} className={isToday ? 'calendar-day today' : 'calendar-day'}>
              <div className="calendar-day-head">{formatDay(day)}</div>
              <div className="calendar-day-body" style={{ height: `${totalHours * 44}px` }}>
                {items.map((reservation) => {
                  const start = new Date(reservation.start);
                  const end = new Date(reservation.end);

                  const startOffset =
                    (start.getHours() + start.getMinutes() / 60 - dayStartHour) / totalHours;
                  const endOffset = (end.getHours() + end.getMinutes() / 60 - dayStartHour) / totalHours;

                  const top = Math.max(0, Math.min(1, startOffset)) * 100;
                  const bottom = Math.max(0, Math.min(1, endOffset)) * 100;
                  const height = Math.max(3, bottom - top);

                  const multiDay = toDayKey(reservation.start) !== toDayKey(reservation.end);

                  return (
                    <div
                      key={reservation.id}
                      className={`calendar-event status-${reservation.status.toLowerCase()}`}
                      style={{ top: `${top}%`, height: `${height}%` }}
                      title={`${reservation.title} — ${formatTime(reservation.start)}–${formatTime(reservation.end)} (${reservation.organizerName})`}
                    >
                      <span className="calendar-event-time">
                        {formatTime(reservation.start)}
                        {multiDay ? ' →' : `–${formatTime(reservation.end)}`}
                      </span>
                      <span className="calendar-event-title">{reservation.title}</span>
                    </div>
                  );
                })}
              </div>
            </div>
          );
        })}
      </div>
    </div>
  );
}
