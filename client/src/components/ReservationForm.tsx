import { useEffect, useState } from 'react';
import type { Reservation, ReservationInput, Room, User } from '../api/types';
import { fromDateTimeLocalValue, toDateTimeLocalValue } from '../utils/dates';

/** Default starting point for a new meeting: tomorrow at 09:00 local time. */
function defaultStart(): string {
  const tomorrow = new Date();
  tomorrow.setDate(tomorrow.getDate() + 1);
  tomorrow.setHours(9, 0, 0, 0);
  return toDateTimeLocalValue(tomorrow.toISOString());
}

function defaultEnd(): string {
  const tomorrow = new Date();
  tomorrow.setDate(tomorrow.getDate() + 1);
  tomorrow.setHours(10, 0, 0, 0);
  return toDateTimeLocalValue(tomorrow.toISOString());
}

/**
 * Create / edit form. It only checks that the fields are filled in - every booking rule
 * (R1-R7) is validated by the backend, which is the single source of truth, and its error
 * envelope is rendered by the caller.
 */
export function ReservationForm({
  rooms,
  users,
  reservation,
  onSubmit,
  onCancel
}: {
  rooms: Room[];
  users: User[];
  reservation: Reservation | null;
  onSubmit: (input: ReservationInput, id?: number) => Promise<boolean>;
  onCancel: () => void;
}) {
  const bookableRooms = rooms.filter((room) => room.isActive || room.id === reservation?.roomId);

  const [roomId, setRoomId] = useState<number>(reservation?.roomId ?? bookableRooms[0]?.id ?? 0);
  const [organizerId, setOrganizerId] = useState<number>(reservation?.organizerId ?? users[0]?.id ?? 0);
  const [title, setTitle] = useState(reservation?.title ?? '');
  const [notes, setNotes] = useState(reservation?.notes ?? '');
  const [start, setStart] = useState(
    reservation ? toDateTimeLocalValue(reservation.start) : defaultStart()
  );
  const [end, setEnd] = useState(reservation ? toDateTimeLocalValue(reservation.end) : defaultEnd());
  const [attendees, setAttendees] = useState<number>(reservation?.attendees ?? 2);
  const [saving, setSaving] = useState(false);
  const [localError, setLocalError] = useState<string | null>(null);

  // Keep the room list valid if the reference data arrives after the form opened.
  useEffect(() => {
    if (!roomId && bookableRooms.length > 0) {
      setRoomId(bookableRooms[0].id);
    }
    if (!organizerId && users.length > 0) {
      setOrganizerId(users[0].id);
    }
  }, [bookableRooms, organizerId, roomId, users]);

  const selectedRoom = rooms.find((room) => room.id === roomId) ?? null;

  const handleSubmit = async (event: React.FormEvent) => {
    event.preventDefault();
    setLocalError(null);

    if (!roomId || !organizerId) {
      setLocalError('Pick a room and an organizer.');
      return;
    }

    if (!title.trim()) {
      setLocalError('Give the meeting a title.');
      return;
    }

    const startIso = fromDateTimeLocalValue(start);
    const endIso = fromDateTimeLocalValue(end);

    if (!startIso || !endIso) {
      setLocalError('Provide a start and an end date.');
      return;
    }

    if (new Date(endIso) <= new Date(startIso)) {
      setLocalError('The end time must be after the start time.');
      return;
    }

    setSaving(true);
    const ok = await onSubmit(
      {
        roomId,
        organizerId,
        title: title.trim(),
        start: startIso,
        end: endIso,
        attendees,
        notes: notes.trim() === '' ? null : notes.trim()
      },
      reservation?.id
    );
    setSaving(false);

    if (!ok) {
      // The caller surfaces the backend's rule errors; nothing else to do here.
      return;
    }
  };

  return (
    <form className="form" onSubmit={handleSubmit}>
      <h3>{reservation ? `Edit reservation #${reservation.id}` : 'New reservation'}</h3>

      <div className="form-grid">
        <label className="field">
          <span>Room</span>
          <select value={roomId} onChange={(event) => setRoomId(Number(event.target.value))}>
            {bookableRooms.map((room) => (
              <option key={room.id} value={room.id}>
                {room.name} ({room.code}) — seats {room.capacity}
              </option>
            ))}
          </select>
        </label>

        <label className="field">
          <span>Organizer</span>
          <select value={organizerId} onChange={(event) => setOrganizerId(Number(event.target.value))}>
            {users.map((user) => (
              <option key={user.id} value={user.id}>
                {user.fullName}
              </option>
            ))}
          </select>
        </label>

        <label className="field field-wide">
          <span>Meeting title</span>
          <input
            type="text"
            value={title}
            maxLength={200}
            placeholder="e.g. Sprint planning"
            onChange={(event) => setTitle(event.target.value)}
          />
        </label>

        <label className="field">
          <span>Start</span>
          <input type="datetime-local" value={start} onChange={(event) => setStart(event.target.value)} />
        </label>

        <label className="field">
          <span>End</span>
          <input type="datetime-local" value={end} onChange={(event) => setEnd(event.target.value)} />
        </label>

        <label className="field">
          <span>Attendees</span>
          <input
            type="number"
            min={1}
            max={selectedRoom?.capacity ?? 999}
            value={attendees}
            onChange={(event) => setAttendees(Number(event.target.value))}
          />
          {selectedRoom && <small className="muted">Room seats {selectedRoom.capacity}.</small>}
        </label>

        <label className="field field-wide">
          <span>Notes (optional)</span>
          <textarea
            rows={2}
            value={notes ?? ''}
            onChange={(event) => setNotes(event.target.value)}
          />
        </label>
      </div>

      {localError && <div className="alert alert-error">{localError}</div>}

      <div className="form-actions">
        <button type="submit" className="btn btn-primary" disabled={saving}>
          {saving ? 'Saving…' : reservation ? 'Save changes' : 'Create reservation'}
        </button>
        <button type="button" className="btn btn-ghost" onClick={onCancel} disabled={saving}>
          Cancel
        </button>
      </div>

      <p className="hint">
        The backend validates duration, room and organizer overlaps, the start being in the future,
        capacity, the booking horizon and the room being active. Violations come back as rule codes.
      </p>
    </form>
  );
}
