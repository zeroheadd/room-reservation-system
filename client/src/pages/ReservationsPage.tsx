import { useEffect, useMemo, useState } from 'react';
import { api, ApiRequestError } from '../api/client';
import type {
  ApiError,
  BookingSettings,
  Reservation,
  ReservationFilters,
  ReservationInput,
  ReservationStatus,
  Room,
  User
} from '../api/types';
import { EmptyState, ErrorAlert, InfoBar, Loading, Panel, StatusTag } from '../components/Feedback';
import { ReservationForm } from '../components/ReservationForm';
import { RoomCalendar } from '../components/RoomCalendar';
import { formatDateTime, startOfWeek, toDateTimeLocalValue } from '../utils/dates';

const STATUSES: ReservationStatus[] = ['Booked', 'Completed', 'Cancelled'];

function toApiError(error: unknown): ApiError {
  if (error instanceof ApiRequestError) {
    return error.payload;
  }

  return {
    code: 'UNEXPECTED_ERROR',
    message: error instanceof Error ? error.message : 'Something went wrong.',
    errors: []
  };
}

/**
 * Reservations page: the reservation list with room, organizer, title, dates, attendees and
 * status; filtering by room, organizer, status and date range; and the create / cancel /
 * end-early actions. All validation happens in the backend and the rule errors are shown.
 */
export default function ReservationsPage() {
  const [reservations, setReservations] = useState<Reservation[]>([]);
  const [rooms, setRooms] = useState<Room[]>([]);
  const [users, setUsers] = useState<User[]>([]);
  const [settings, setSettings] = useState<BookingSettings | null>(null);
  const [filters, setFilters] = useState<ReservationFilters>({});
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<ApiError | null>(null);
  const [success, setSuccess] = useState<string | null>(null);

  const [editing, setEditing] = useState<Reservation | null>(null);
  const [showForm, setShowForm] = useState(false);
  const [busyId, setBusyId] = useState<number | null>(null);

  const [calendarRoomId, setCalendarRoomId] = useState<number | null>(null);
  const [weekStart, setWeekStart] = useState(() => startOfWeek(new Date()));
  const [calendarReservations, setCalendarReservations] = useState<Reservation[]>([]);

  // Reference data: rooms, users and the configurable limits.
  useEffect(() => {
    Promise.all([api.getRooms(), api.getUsers(), api.getSettings()])
      .then(([roomList, userList, bookingSettings]) => {
        setRooms(roomList);
        setUsers(userList);
        setSettings(bookingSettings);
        setCalendarRoomId((current) => current ?? roomList.find((room) => room.isActive)?.id ?? null);
      })
      .catch((cause) => setError(toApiError(cause)));
  }, []);

  const reload = useMemo(
    () => async () => {
      setLoading(true);
      try {
        setReservations(await api.getReservations(filters));
        setError(null);
      } catch (cause) {
        setError(toApiError(cause));
        setReservations([]);
      } finally {
        setLoading(false);
      }
    },
    [filters]
  );

  useEffect(() => {
    void reload();
  }, [reload]);

  // The calendar loads a week of reservations for the selected room.
  useEffect(() => {
    if (calendarRoomId === null) {
      return;
    }

    const from = weekStart;
    const to = new Date(weekStart);
    to.setDate(to.getDate() + 7);

    api
      .getReservations({ roomId: calendarRoomId, from: from.toISOString(), to: to.toISOString() })
      .then(setCalendarReservations)
      .catch((cause) => setError(toApiError(cause)));
  }, [calendarRoomId, weekStart, reservations]);

  const runAction = async (id: number, action: () => Promise<Reservation>, message: string) => {
    setBusyId(id);
    setError(null);
    setSuccess(null);
    try {
      await action();
      setSuccess(message);
      await reload();
    } catch (cause) {
      setError(toApiError(cause));
    } finally {
      setBusyId(null);
    }
  };

  const handleSubmit = async (input: ReservationInput, id?: number) => {
    setError(null);
    setSuccess(null);
    try {
      if (id === undefined) {
        await api.createReservation(input);
        setSuccess('Reservation created.');
      } else {
        await api.updateReservation(id, input);
        setSuccess('Reservation updated.');
      }

      setShowForm(false);
      setEditing(null);
      await reload();
      return true;
    } catch (cause) {
      // Rule violations come back as a 400 with the rule codes; show them and keep the form open.
      setError(toApiError(cause));
      return false;
    }
  };

  const endMeeting = async (reservation: Reservation) => {
    const answer = window.prompt(
      `How many people actually attended "${reservation.title}"?\n` +
        `(Booked: ${reservation.attendees}. Leave empty to close the meeting without recording attendance.)`,
      String(reservation.attendees)
    );

    // Cancelled prompt means "do nothing".
    if (answer === null) {
      return;
    }

    const actual = answer.trim() === '' ? null : Number(answer);
    if (actual !== null && (Number.isNaN(actual) || actual < 0)) {
      setError({ code: 'VALIDATION_FAILED', message: 'Attendance must be a non-negative number.', errors: [] });
      return;
    }

    await runAction(
      reservation.id,
      () => api.endMeeting(reservation.id, actual),
      'Meeting closed. The actual attendance now feeds the room usage report.'
    );
  };

  const clearFilters = () => setFilters({});
  const activeFilterCount = Object.values(filters).filter((value) => value !== undefined && value !== '').length;
  const calendarRoom = rooms.find((room) => room.id === calendarRoomId) ?? null;

  return (
    <div className="page">
      {settings && (
        <InfoBar>
          Rules in force: max <strong>{settings.maxReservationDurationDays} days</strong> per reservation,
          booking horizon <strong>{settings.bookingHorizonDays} days</strong>, start must be in the future,
          capacity and overlapping room/organizer bookings are checked in the backend.
        </InfoBar>
      )}

      <Panel title="Reservations" actions={
        <button
          type="button"
          className="btn btn-primary"
          onClick={() => {
            setEditing(null);
            setShowForm(true);
            setError(null);
            setSuccess(null);
          }}
        >
          New reservation
        </button>
      }>
        <div className="filters">
          <label className="field">
            <span>Room</span>
            <select
              value={filters.roomId ?? ''}
              onChange={(event) =>
                setFilters((current) => ({
                  ...current,
                  roomId: event.target.value === '' ? undefined : Number(event.target.value)
                }))
              }
            >
              <option value="">All rooms</option>
              {rooms.map((room) => (
                <option key={room.id} value={room.id}>
                  {room.name} ({room.code})
                </option>
              ))}
            </select>
          </label>

          <label className="field">
            <span>Organizer</span>
            <select
              value={filters.organizerId ?? ''}
              onChange={(event) =>
                setFilters((current) => ({
                  ...current,
                  organizerId: event.target.value === '' ? undefined : Number(event.target.value)
                }))
              }
            >
              <option value="">All organizers</option>
              {users.map((user) => (
                <option key={user.id} value={user.id}>
                  {user.fullName}
                </option>
              ))}
            </select>
          </label>

          <label className="field">
            <span>Status</span>
            <select
              value={filters.status ?? ''}
              onChange={(event) =>
                setFilters((current) => ({
                  ...current,
                  status: (event.target.value || undefined) as ReservationStatus | undefined
                }))
              }
            >
              <option value="">Any status</option>
              {STATUSES.map((status) => (
                <option key={status} value={status}>
                  {status}
                </option>
              ))}
            </select>
          </label>

          <label className="field">
            <span>From</span>
            <input
              type="datetime-local"
              value={filters.from ? toDateTimeLocalValue(filters.from) : ''}
              onChange={(event) =>
                setFilters((current) => ({
                  ...current,
                  from: event.target.value ? new Date(event.target.value).toISOString() : undefined
                }))
              }
            />
          </label>

          <label className="field">
            <span>To</span>
            <input
              type="datetime-local"
              value={filters.to ? toDateTimeLocalValue(filters.to) : ''}
              onChange={(event) =>
                setFilters((current) => ({
                  ...current,
                  to: event.target.value ? new Date(event.target.value).toISOString() : undefined
                }))
              }
            />
          </label>

          <button type="button" className="btn btn-ghost" disabled={activeFilterCount === 0} onClick={clearFilters}>
            Clear filters{activeFilterCount > 0 ? ` (${activeFilterCount})` : ''}
          </button>
        </div>

        <ErrorAlert error={error} />
        {success && <div className="alert alert-success">{success}</div>}

        {(showForm || editing) && (
          <ReservationForm
            rooms={rooms}
            users={users}
            reservation={editing}
            onSubmit={handleSubmit}
            onCancel={() => {
              setShowForm(false);
              setEditing(null);
              setError(null);
            }}
          />
        )}

        {loading ? (
          <Loading label="Loading reservations…" />
        ) : reservations.length === 0 ? (
          <EmptyState message="No reservations match these filters." />
        ) : (
          <div className="table-scroll">
            <table className="table">
              <thead>
                <tr>
                  <th>Room</th>
                  <th>Organizer</th>
                  <th>Meeting</th>
                  <th>Start</th>
                  <th>End</th>
                  <th className="num">Attendees</th>
                  <th>Status</th>
                  <th className="actions-col">Actions</th>
                </tr>
              </thead>
              <tbody>
                {reservations.map((reservation) => {
                  const isBusy = busyId === reservation.id;
                  const started = new Date(reservation.start) <= new Date();

                  return (
                    <tr key={reservation.id}>
                      <td>
                        <strong>{reservation.roomName}</strong>
                        <div className="muted">
                          <code>{reservation.roomCode}</code>
                        </div>
                      </td>
                      <td>{reservation.organizerName}</td>
                      <td>
                        {reservation.title}
                        {reservation.notes && <div className="muted">{reservation.notes}</div>}
                        {reservation.actualAttendees !== null && (
                          <div className="muted">
                            Actual attendance: {reservation.actualAttendees}
                          </div>
                        )}
                      </td>
                      <td>{formatDateTime(reservation.start)}</td>
                      <td>{formatDateTime(reservation.end)}</td>
                      <td className="num">{reservation.attendees}</td>
                      <td>
                        <StatusTag status={reservation.status} />
                      </td>
                      <td className="actions-col">
                        <div className="row-actions">
                          <button
                            type="button"
                            className="btn btn-small"
                            disabled={isBusy || reservation.status !== 'Booked'}
                            onClick={() => {
                              setEditing(reservation);
                              setShowForm(false);
                              setError(null);
                              setSuccess(null);
                            }}
                          >
                            Edit
                          </button>
                          <button
                            type="button"
                            className="btn btn-small"
                            disabled={isBusy || reservation.status !== 'Booked'}
                            onClick={() =>
                              void runAction(
                                reservation.id,
                                () => api.cancelReservation(reservation.id),
                                'Reservation cancelled. The slot is free again.'
                              )
                            }
                          >
                            Cancel
                          </button>
                          <button
                            type="button"
                            className="btn btn-small"
                            disabled={isBusy || reservation.status !== 'Booked' || !started}
                            title={started ? 'Close this meeting and record attendance' : 'The meeting has not started yet'}
                            onClick={() => void endMeeting(reservation)}
                          >
                            End early
                          </button>
                        </div>
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        )}
      </Panel>

      <Panel
        title="Room timeline"
        actions={
          <label className="field-inline">
            <span className="muted">Room</span>
            <select
              value={calendarRoomId ?? ''}
              onChange={(event) => setCalendarRoomId(event.target.value === '' ? null : Number(event.target.value))}
            >
              {rooms.map((room) => (
                <option key={room.id} value={room.id}>
                  {room.name} ({room.code})
                </option>
              ))}
            </select>
          </label>
        }
      >
        {calendarRoom ? (
          <>
            <p className="hint">
              {calendarRoom.name} · {calendarRoom.floor} · seats {calendarRoom.capacity}
              {calendarRoom.isActive ? '' : ' · inactive'}
            </p>
            <RoomCalendar
              weekStart={weekStart}
              onChangeWeek={setWeekStart}
              reservations={calendarReservations}
            />
          </>
        ) : (
          <EmptyState message="Select a room to see its week." />
        )}
      </Panel>
    </div>
  );
}
