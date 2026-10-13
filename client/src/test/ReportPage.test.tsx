import { render, screen, waitFor } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import ReportPage from '../pages/ReportPage';
import type { RoomUsageReport } from '../api/types';

const report: RoomUsageReport = {
  generatedAt: '2026-03-10T09:00:00Z',
  lastDays: null,
  underusedThreshold: 0.5,
  minimumMeetingsForUsageVerdict: 3,
  summary: {
    rooms: 2,
    totalReservations: 9,
    completedMeetings: 7,
    cancelledReservations: 1,
    reservedHours: 40,
    actualHours: 21,
    wastedHours: 19,
    utilization: 0.42,
    underusedRooms: 1
  },
  rooms: [
    {
      roomId: 1,
      roomName: 'Aurora',
      roomCode: 'KA-201',
      floor: '2nd floor',
      capacity: 12,
      isActive: true,
      totalReservations: 6,
      activeReservations: 5,
      completedMeetings: 4,
      cancelledReservations: 1,
      reservedHours: 24,
      actualHours: 6,
      wastedHours: 18,
      bookedAttendees: 40,
      actualAttendees: 8,
      meetingsWithAttendance: 4,
      utilization: 0.2,
      isUnderused: true,
      verdict: 'Often booked, rarely used'
    },
    {
      roomId: 2,
      roomName: 'Cascade',
      roomCode: 'KA-305',
      floor: '3rd floor',
      capacity: 30,
      isActive: true,
      totalReservations: 3,
      activeReservations: 3,
      completedMeetings: 3,
      cancelledReservations: 0,
      reservedHours: 16,
      actualHours: 15,
      wastedHours: 1,
      bookedAttendees: 60,
      actualAttendees: 55,
      meetingsWithAttendance: 3,
      utilization: 0.917,
      isUnderused: false,
      verdict: 'Well used'
    }
  ]
};

function stubReportResponse() {
  vi.stubGlobal(
    'fetch',
    vi.fn().mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => report
    } as unknown as Response)
  );
}

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('ReportPage', () => {
  it('shows the totals and flags the room that is booked but rarely used', async () => {
    stubReportResponse();

    render(<ReportPage />);

    await waitFor(() => expect(screen.getByText('Aurora')).toBeInTheDocument());

    expect(screen.getByText('Often booked, rarely used')).toBeInTheDocument();
    expect(screen.getByText('Well used')).toBeInTheDocument();

    // Summary cards.
    expect(screen.getByText('Overall utilization').nextSibling).toHaveTextContent('42%');
    expect(screen.getByText('Total wasted').nextSibling).toHaveTextContent('19 h');

    // The underused room is highlighted in the table.
    const underusedRow = screen.getByText('Aurora').closest('tr');
    expect(underusedRow).toHaveClass('row-alert');
    expect(underusedRow).toHaveTextContent('20%');
  });

  it('explains the configurable threshold and offers the PDF export', async () => {
    stubReportResponse();

    render(<ReportPage />);

    await waitFor(() => expect(screen.getByText('Aurora')).toBeInTheDocument());

    expect(screen.getByText(/below/)).toHaveTextContent('50%');
    expect(screen.getByRole('button', { name: 'Export PDF' })).toBeEnabled();
  });

  it('shows an error instead of a table when the report cannot be loaded', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue({
        ok: false,
        status: 400,
        json: async () => ({
          code: 'VALIDATION_FAILED',
          message: 'lastDays must be a positive number of days.',
          errors: []
        })
      } as unknown as Response)
    );

    render(<ReportPage />);

    await waitFor(() =>
      expect(screen.getByRole('alert')).toHaveTextContent('lastDays must be a positive number of days.')
    );
  });
});
