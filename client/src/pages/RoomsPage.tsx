import { useEffect, useMemo, useState } from 'react';
import { api, ApiRequestError } from '../api/client';
import type { ApiError, Room, RoomFilters } from '../api/types';
import { EmptyState, ErrorAlert, Loading, Panel, StatusTag } from '../components/Feedback';

const ALL_EQUIPMENT = ['Projector', 'TV screen', 'Video conferencing', 'Whiteboard'];

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
 * Rooms page: every meeting room with its identifier, capacity, floor, equipment and status,
 * plus filtering by minimum capacity, equipment and floor.
 */
export default function RoomsPage() {
  const [rooms, setRooms] = useState<Room[]>([]);
  const [filters, setFilters] = useState<RoomFilters>({ equipment: [] });
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<ApiError | null>(null);

  // The server does the filtering, which keeps the contract in one place for larger data sets.
  useEffect(() => {
    let cancelled = false;
    setLoading(true);

    api
      .getRooms(filters)
      .then((result) => {
        if (!cancelled) {
          setRooms(result);
          setError(null);
        }
      })
      .catch((cause) => {
        if (!cancelled) {
          setError(toApiError(cause));
          setRooms([]);
        }
      })
      .finally(() => {
        if (!cancelled) {
          setLoading(false);
        }
      });

    return () => {
      cancelled = true;
    };
  }, [filters]);

  const equipment = filters.equipment ?? [];
  const activeFilterCount = useMemo(() => {
    let count = 0;
    if (filters.minCapacity) count += 1;
    if (equipment.length > 0) count += 1;
    if (filters.floor) count += 1;
    if (filters.activeOnly) count += 1;
    return count;
  }, [filters, equipment.length]);

  const toggleEquipment = (item: string) => {
    setFilters((current) => {
      const selected = current.equipment ?? [];
      return {
        ...current,
        equipment: selected.includes(item)
          ? selected.filter((value) => value !== item)
          : [...selected, item]
      };
    });
  };

  return (
    <div className="page">
      <Panel title="Rooms">
        <div className="filters">
          <label className="field">
            <span>Minimum capacity</span>
            <input
              type="number"
              min={0}
              placeholder="Any"
              value={filters.minCapacity ?? ''}
              onChange={(event) =>
                setFilters((current) => ({
                  ...current,
                  minCapacity: event.target.value === '' ? undefined : Number(event.target.value)
                }))
              }
            />
          </label>

          <label className="field">
            <span>Floor / location</span>
            <input
              type="text"
              placeholder="e.g. 3rd floor"
              value={filters.floor ?? ''}
              onChange={(event) =>
                setFilters((current) => ({ ...current, floor: event.target.value || undefined }))
              }
            />
          </label>

          <div className="field field-grow">
            <span>Equipment (rooms must have all selected)</span>
            <div className="checkbox-row">
              {ALL_EQUIPMENT.map((item) => (
                <label key={item} className="checkbox">
                  <input
                    type="checkbox"
                    checked={equipment.includes(item)}
                    onChange={() => toggleEquipment(item)}
                  />
                  {item}
                </label>
              ))}
            </div>
          </div>

          <label className="checkbox field-inline">
            <input
              type="checkbox"
              checked={filters.activeOnly ?? false}
              onChange={(event) =>
                setFilters((current) => ({ ...current, activeOnly: event.target.checked }))
              }
            />
            Active rooms only
          </label>

          <button
            type="button"
            className="btn btn-ghost"
            disabled={activeFilterCount === 0}
            onClick={() => setFilters({ equipment: [] })}
          >
            Clear filters{activeFilterCount > 0 ? ` (${activeFilterCount})` : ''}
          </button>
        </div>

        <ErrorAlert error={error} />

        {loading ? (
          <Loading label="Loading rooms…" />
        ) : rooms.length === 0 ? (
          <EmptyState message="No rooms match these filters." />
        ) : (
          <div className="table-scroll">
            <table className="table">
              <thead>
                <tr>
                  <th>Room</th>
                  <th>Identifier</th>
                  <th className="num">Capacity</th>
                  <th>Floor / location</th>
                  <th>Equipment</th>
                  <th className="num">Active reservations</th>
                  <th>Status</th>
                </tr>
              </thead>
              <tbody>
                {rooms.map((room) => (
                  <tr key={room.id} className={room.isActive ? undefined : 'row-muted'}>
                    <td>
                      <strong>{room.name}</strong>
                    </td>
                    <td>
                      <code>{room.code}</code>
                    </td>
                    <td className="num">{room.capacity}</td>
                    <td>{room.floor}</td>
                    <td>
                      {room.equipment.length === 0 ? (
                        <span className="muted">None</span>
                      ) : (
                        <div className="tag-row">
                          {room.equipment.map((item) => (
                            <span key={item} className="chip">
                              {item}
                            </span>
                          ))}
                        </div>
                      )}
                    </td>
                    <td className="num">{room.activeReservations}</td>
                    <td>
                      <StatusTag status={room.isActive ? 'Active' : 'Inactive'} />
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}

        <p className="hint">
          Inactive rooms cannot be booked — the backend rejects them with code
          <code> R7_ROOM_INACTIVE</code>.
        </p>
      </Panel>
    </div>
  );
}
