import type {
  ApiError,
  BookingSettings,
  Reservation,
  ReservationFilters,
  ReservationInput,
  Room,
  RoomFilters,
  RoomUsageReport,
  User
} from './types';

/**
 * Base URL of the ASP.NET backend. Vite's dev server proxies /api to it during development,
 * so the default (empty string) works both behind the proxy and when the SPA is served by
 * the API itself.
 */
const BASE_URL = import.meta.env.VITE_API_BASE_URL ?? '';

/** An error carrying the backend's rule codes so the UI can show them precisely. */
export class ApiRequestError extends Error {
  readonly status: number;
  readonly payload: ApiError;

  constructor(status: number, payload: ApiError) {
    super(payload.message);
    this.name = 'ApiRequestError';
    this.status = status;
    this.payload = payload;
  }

  /** True when the failure is one or more broken booking rules (HTTP 400). */
  get isRuleViolation(): boolean {
    return this.status === 400;
  }

  get ruleMessages(): string[] {
    return this.payload.errors.map((error) => error.message);
  }
}

function buildQuery(params: Record<string, unknown>): string {
  const search = new URLSearchParams();

  for (const [key, value] of Object.entries(params)) {
    if (value === undefined || value === null || value === '' || value === false) {
      continue;
    }

    if (Array.isArray(value)) {
      if (value.length > 0) {
        search.set(key, value.join(','));
      }
      continue;
    }

    search.set(key, String(value));
  }

  const query = search.toString();
  return query ? `?${query}` : '';
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(`${BASE_URL}${path}`, {
    headers: { 'Content-Type': 'application/json' },
    ...init
  });

  if (!response.ok) {
    // The API always answers with the same error envelope; fall back to something readable
    // if a proxy or network layer produced the failure instead.
    let payload: ApiError;
    try {
      payload = (await response.json()) as ApiError;
    } catch {
      payload = {
        code: 'UNEXPECTED_ERROR',
        message: `Request failed with status ${response.status}.`,
        errors: []
      };
    }

    if (!payload.errors) {
      payload.errors = [];
    }

    throw new ApiRequestError(response.status, payload);
  }

  if (response.status === 204) {
    return undefined as T;
  }

  return (await response.json()) as T;
}

export const api = {
  getSettings: () => request<BookingSettings>('/api/settings'),

  getEquipment: () => request<string[]>('/api/equipment'),

  getRooms: (filters: RoomFilters = {}) =>
    request<Room[]>(
      `/api/rooms${buildQuery({
        minCapacity: filters.minCapacity,
        equipment: filters.equipment,
        floor: filters.floor,
        activeOnly: filters.activeOnly
      })}`
    ),

  getUsers: (search?: string) => request<User[]>(`/api/users${buildQuery({ search })}`),

  getReservations: (filters: ReservationFilters = {}) =>
    request<Reservation[]>(
      `/api/reservations${buildQuery({
        roomId: filters.roomId,
        organizerId: filters.organizerId,
        status: filters.status,
        from: filters.from,
        to: filters.to
      })}`
    ),

  createReservation: (input: ReservationInput) =>
    request<Reservation>('/api/reservations', {
      method: 'POST',
      body: JSON.stringify(input)
    }),

  updateReservation: (id: number, input: ReservationInput) =>
    request<Reservation>(`/api/reservations/${id}`, {
      method: 'PUT',
      body: JSON.stringify(input)
    }),

  cancelReservation: (id: number) =>
    request<Reservation>(`/api/reservations/${id}/cancel`, { method: 'POST' }),

  endMeeting: (id: number, actualAttendees: number | null) =>
    request<Reservation>(`/api/reservations/${id}/end`, {
      method: 'POST',
      body: JSON.stringify({ actualAttendees })
    }),

  getRoomUsageReport: (lastDays?: number) =>
    request<RoomUsageReport>(`/api/reports/room-usage${buildQuery({ lastDays })}`),

  /** URL of the printable report; the browser turns it into a PDF through the print dialog. */
  printableReportUrl: (lastDays?: number) =>
    `${BASE_URL}/api/reports/room-usage/print${buildQuery({ lastDays })}`
};
