import { useEffect, useState } from 'react';
import { api, ApiRequestError } from '../api/client';
import type { ApiError, RoomUsageReport } from '../api/types';
import { EmptyState, ErrorAlert, Loading, Panel } from '../components/Feedback';
import { formatDateTime } from '../utils/dates';

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

const RANGES = [
  { label: 'All time', value: null },
  { label: 'Last 30 days', value: 30 },
  { label: 'Last 90 days', value: 90 }
];

function percent(value: number | null): string {
  return value === null ? 'n/a' : `${Math.round(value * 100)}%`;
}

function hours(value: number): string {
  return `${value.toLocaleString(undefined, { maximumFractionDigits: 1 })} h`;
}

/**
 * Room usage report: shows which rooms are booked often but actually used very little, so the
 * office can spot rooms that could be given up or repurposed.
 */
export default function ReportPage() {
  const [report, setReport] = useState<RoomUsageReport | null>(null);
  const [lastDays, setLastDays] = useState<number | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<ApiError | null>(null);

  useEffect(() => {
    let cancelled = false;
    setLoading(true);

    api
      .getRoomUsageReport(lastDays ?? undefined)
      .then((result) => {
        if (!cancelled) {
          setReport(result);
          setError(null);
        }
      })
      .catch((cause) => {
        if (!cancelled) {
          setError(toApiError(cause));
          setReport(null);
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
  }, [lastDays]);

  /**
   * PDF export: the backend renders the same report as a print-ready HTML document and the
   * browser's print dialog produces the PDF ("Save as PDF"). No native PDF dependency, and the
   * output always matches the numbers on screen.
   */
  const exportPdf = () => {
    window.open(api.printableReportUrl(lastDays ?? undefined), '_blank', 'noopener');
  };

  return (
    <div className="page">
      <Panel
        title="Room usage report"
        actions={
          <div className="panel-actions">
            {RANGES.map((range) => (
              <button
                key={range.label}
                type="button"
                className={lastDays === range.value ? 'btn btn-small btn-active' : 'btn btn-small'}
                onClick={() => setLastDays(range.value)}
              >
                {range.label}
              </button>
            ))}
            <button type="button" className="btn btn-primary" onClick={exportPdf} disabled={!report}>
              Export PDF
            </button>
          </div>
        }
      >
        <ErrorAlert error={error} />

        {loading ? (
          <Loading label="Building the usage report…" />
        ) : !report ? (
          <EmptyState message="The report could not be loaded." />
        ) : (
          <>
            <div className="stat-row">
              <Stat label="Rooms" value={String(report.summary.rooms)} />
              <Stat label="Reservations" value={String(report.summary.totalReservations)} />
              <Stat label="Finished meetings" value={String(report.summary.completedMeetings)} />
              <Stat label="Reserved" value={hours(report.summary.reservedHours)} />
              <Stat label="Actually used" value={hours(report.summary.actualHours)} />
              <Stat label="Total wasted" value={hours(report.summary.wastedHours)} tone="bad" />
              <Stat label="Overall utilization" value={percent(report.summary.utilization)} />
              <Stat
                label="Underused rooms"
                value={String(report.summary.underusedRooms)}
                tone={report.summary.underusedRooms > 0 ? 'bad' : 'good'}
              />
            </div>

            <p className="hint">
              Generated {formatDateTime(report.generatedAt)} · a room counts as "often booked, rarely
              used" when utilization is below <strong>{Math.round(report.underusedThreshold * 100)}%</strong> over
              at least {report.minimumMeetingsForUsageVerdict} finished meetings with recorded attendance.
              Utilization is actual attendees divided by booked attendees.
            </p>

            <div className="table-scroll">
              <table className="table">
                <thead>
                  <tr>
                    <th>Room</th>
                    <th>Floor</th>
                    <th className="num">Capacity</th>
                    <th className="num">Reservations</th>
                    <th className="num">Finished</th>
                    <th className="num">Cancelled</th>
                    <th className="num">Reserved</th>
                    <th className="num">Used</th>
                    <th className="num">Wasted</th>
                    <th className="num">People booked / actual</th>
                    <th className="num">Utilization</th>
                    <th>Verdict</th>
                  </tr>
                </thead>
                <tbody>
                  {report.rooms.map((room) => (
                    <tr key={room.roomId} className={room.isUnderused ? 'row-alert' : undefined}>
                      <td>
                        <strong>{room.roomName}</strong>
                        <div className="muted">
                          <code>{room.roomCode}</code>
                          {room.isActive ? '' : ' · inactive'}
                        </div>
                      </td>
                      <td>{room.floor}</td>
                      <td className="num">{room.capacity}</td>
                      <td className="num">{room.totalReservations}</td>
                      <td className="num">{room.completedMeetings}</td>
                      <td className="num">{room.cancelledReservations}</td>
                      <td className="num">{hours(room.reservedHours)}</td>
                      <td className="num">{hours(room.actualHours)}</td>
                      <td className="num">{hours(room.wastedHours)}</td>
                      <td className="num">
                        {room.bookedAttendees} / {room.actualAttendees}
                      </td>
                      <td className="num">{percent(room.utilization)}</td>
                      <td>
                        <span className={room.isUnderused ? 'tag tag-inactive' : 'tag tag-active'}>
                          {room.verdict}
                        </span>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </>
        )}
      </Panel>
    </div>
  );
}

function Stat({ label, value, tone }: { label: string; value: string; tone?: 'bad' | 'good' }) {
  return (
    <div className={tone ? `stat stat-${tone}` : 'stat'}>
      <div className="stat-label">{label}</div>
      <div className="stat-value">{value}</div>
    </div>
  );
}
