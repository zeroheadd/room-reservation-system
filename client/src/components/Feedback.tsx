import type { ReactNode } from 'react';
import type { ApiError } from '../api/types';

/** A neutral card wrapper used across pages. */
export function Panel({
  title,
  actions,
  children
}: {
  title?: string;
  actions?: ReactNode;
  children: ReactNode;
}) {
  return (
    <section className="panel">
      {(title || actions) && (
        <div className="panel-head">
          {title && <h2>{title}</h2>}
          {actions && <div className="panel-actions">{actions}</div>}
        </div>
      )}
      {children}
    </section>
  );
}

/**
 * Shows the API error envelope. Booking rule violations are listed individually so the user
 * sees exactly which rules were broken, which is the behaviour the task asks for.
 */
export function ErrorAlert({ error }: { error: ApiError | null }) {
  if (!error) {
    return null;
  }

  const violations = error.errors ?? [];
  const showList = violations.length > 1;

  return (
    <div className="alert alert-error" role="alert">
      <strong>{error.message}</strong>
      {showList && (
        <ul>
          {violations.map((violation) => (
            <li key={`${violation.code}-${violation.message}`}>
              <code>{violation.code}</code> {violation.message}
            </li>
          ))}
        </ul>
      )}
      {!showList && violations.length === 1 && <div className="alert-code">{violations[0].code}</div>}
    </div>
  );
}

/** A short informational strip, e.g. the booking limits. */
export function InfoBar({ children }: { children: ReactNode }) {
  return <div className="info-bar">{children}</div>;
}

/** Coloured pill for room and reservation statuses. */
export function StatusTag({ status }: { status: string }) {
  const key = status.toLowerCase().replace(/\s+/g, '-');
  return <span className={`tag tag-${key}`}>{status}</span>;
}

export function EmptyState({ message }: { message: string }) {
  return <p className="empty-state">{message}</p>;
}

export function Loading({ label = 'Loading…' }: { label?: string }) {
  return <p className="loading">{label}</p>;
}
