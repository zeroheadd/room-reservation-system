/**
 * Shared API types. These mirror the DTOs returned by the ASP.NET backend
 * (see src/RoomReservation.Api/Dtos) so the compiler keeps both sides honest.
 */

export interface Room {
  id: number;
  name: string;
  code: string;
  capacity: number;
  floor: string;
  equipment: string[];
  isActive: boolean;
  activeReservations: number;
  totalReservations: number;
}

export interface User {
  id: number;
  firstName: string;
  lastName: string;
  fullName: string;
  code: string;
  email: string;
  department: string;
  activeReservations: number;
  totalReservations: number;
}

export type ReservationStatus = 'Booked' | 'Completed' | 'Cancelled';

export interface Reservation {
  id: number;
  roomId: number;
  roomName: string;
  roomCode: string;
  organizerId: number;
  organizerName: string;
  title: string;
  notes: string | null;
  start: string;
  end: string;
  durationHours: number;
  attendees: number;
  status: ReservationStatus;
  actualAttendees: number | null;
  completedAt: string | null;
  cancelledAt: string | null;
  createdAt: string;
}

export interface RoomUsage {
  roomId: number;
  roomName: string;
  roomCode: string;
  floor: string;
  capacity: number;
  isActive: boolean;
  totalReservations: number;
  activeReservations: number;
  completedMeetings: number;
  cancelledReservations: number;
  reservedHours: number;
  actualHours: number;
  wastedHours: number;
  bookedAttendees: number;
  actualAttendees: number;
  meetingsWithAttendance: number;
  utilization: number | null;
  isUnderused: boolean;
  verdict: string;
}

export interface RoomUsageSummary {
  rooms: number;
  totalReservations: number;
  completedMeetings: number;
  cancelledReservations: number;
  reservedHours: number;
  actualHours: number;
  wastedHours: number;
  utilization: number;
  underusedRooms: number;
}

export interface RoomUsageReport {
  generatedAt: string;
  lastDays: number | null;
  underusedThreshold: number;
  minimumMeetingsForUsageVerdict: number;
  summary: RoomUsageSummary;
  rooms: RoomUsage[];
}

/** The configurable limits from appsettings.json, surfaced so the UI can explain the rules. */
export interface BookingSettings {
  maxReservationDurationDays: number;
  bookingHorizonDays: number;
  underusedThreshold: number;
  minimumMeetingsForUsageVerdict: number;
}

export interface ReservationInput {
  roomId: number;
  organizerId: number;
  title: string;
  start: string;
  end: string;
  attendees: number;
  notes: string | null;
}

/** A single rule violation: a stable code plus a message meant for the user. */
export interface RuleError {
  code: string;
  message: string;
}

/** The uniform error envelope returned by every failing endpoint. */
export interface ApiError {
  code: string;
  message: string;
  errors: RuleError[];
}

export interface ReservationFilters {
  roomId?: number;
  organizerId?: number;
  status?: ReservationStatus;
  from?: string;
  to?: string;
}

export interface RoomFilters {
  minCapacity?: number;
  equipment?: string[];
  floor?: string;
  activeOnly?: boolean;
}
