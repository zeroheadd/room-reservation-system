import { useEffect, useState } from 'react';
import { api, ApiRequestError } from '../api/client';
import type { ApiError, User } from '../api/types';
import { EmptyState, ErrorAlert, Loading, Panel } from '../components/Feedback';

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
 * Users page: first name, last name and identifier plus how many active reservations
 * each person currently has.
 */
export default function UsersPage() {
  const [users, setUsers] = useState<User[]>([]);
  const [search, setSearch] = useState('');
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<ApiError | null>(null);

  useEffect(() => {
    let cancelled = false;
    setLoading(true);

    // Small debounce so typing in the search box does not fire a request per keystroke.
    const timer = window.setTimeout(() => {
      api
        .getUsers(search || undefined)
        .then((result) => {
          if (!cancelled) {
            setUsers(result);
            setError(null);
          }
        })
        .catch((cause) => {
          if (!cancelled) {
            setError(toApiError(cause));
            setUsers([]);
          }
        })
        .finally(() => {
          if (!cancelled) {
            setLoading(false);
          }
        });
    }, 200);

    return () => {
      cancelled = true;
      window.clearTimeout(timer);
    };
  }, [search]);

  const totalActive = users.reduce((sum, user) => sum + user.activeReservations, 0);

  return (
    <div className="page">
      <Panel
        title="Users"
        actions={
          <span className="muted">
            {users.length} {users.length === 1 ? 'person' : 'people'} · {totalActive} active reservations
          </span>
        }
      >
        <div className="filters">
          <label className="field field-grow">
            <span>Search by name, identifier, e-mail or department</span>
            <input
              type="search"
              placeholder="e.g. Auste, EMP-0001, Engineering"
              value={search}
              onChange={(event) => setSearch(event.target.value)}
            />
          </label>
        </div>

        <ErrorAlert error={error} />

        {loading ? (
          <Loading label="Loading users…" />
        ) : users.length === 0 ? (
          <EmptyState message="No users match this search." />
        ) : (
          <div className="table-scroll">
            <table className="table">
              <thead>
                <tr>
                  <th>First name</th>
                  <th>Last name</th>
                  <th>Identifier</th>
                  <th>Department</th>
                  <th>E-mail</th>
                  <th className="num">Active reservations</th>
                </tr>
              </thead>
              <tbody>
                {users.map((user) => (
                  <tr key={user.id}>
                    <td>{user.firstName}</td>
                    <td>{user.lastName}</td>
                    <td>
                      <code>{user.code}</code>
                    </td>
                    <td>{user.department}</td>
                    <td>
                      <a href={`mailto:${user.email}`}>{user.email}</a>
                    </td>
                    <td className="num">
                      <strong>{user.activeReservations}</strong>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}

        <p className="hint">
          An "active reservation" is one that is booked or finished — cancelled reservations are
          not counted.
        </p>
      </Panel>
    </div>
  );
}
