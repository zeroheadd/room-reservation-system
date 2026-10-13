import { NavLink, Navigate, Route, Routes } from 'react-router-dom';
import RoomsPage from './pages/RoomsPage';
import UsersPage from './pages/UsersPage';
import ReservationsPage from './pages/ReservationsPage';
import ReportPage from './pages/ReportPage';

/**
 * Application shell: a small navigation rail plus the four pages.
 * There is no authentication in this app, as the task specifies.
 */
export default function App() {
  return (
    <div className="app">
      <header className="app-header">
        <div className="brand">
          <span className="brand-mark" aria-hidden="true">◪</span>
          <div>
            <h1>Room reservations</h1>
            <p className="brand-sub">Meeting rooms in the Kaunas office</p>
          </div>
        </div>

        <nav className="nav" aria-label="Main navigation">
          <NavLink to="/rooms" className={({ isActive }) => (isActive ? 'nav-link active' : 'nav-link')}>
            Rooms
          </NavLink>
          <NavLink to="/users" className={({ isActive }) => (isActive ? 'nav-link active' : 'nav-link')}>
            Users
          </NavLink>
          <NavLink to="/reservations" className={({ isActive }) => (isActive ? 'nav-link active' : 'nav-link')}>
            Reservations
          </NavLink>
          <NavLink to="/report" className={({ isActive }) => (isActive ? 'nav-link active' : 'nav-link')}>
            Usage report
          </NavLink>
        </nav>
      </header>

      <main className="app-main">
        <Routes>
          <Route path="/" element={<Navigate to="/rooms" replace />} />
          <Route path="/rooms" element={<RoomsPage />} />
          <Route path="/users" element={<UsersPage />} />
          <Route path="/reservations" element={<ReservationsPage />} />
          <Route path="/report" element={<ReportPage />} />
          <Route path="*" element={<Navigate to="/rooms" replace />} />
        </Routes>
      </main>

      <footer className="app-footer">
        Booking rules (duration, overlap, capacity, horizon) are validated in the backend on every
        create and edit — the UI only mirrors them.
      </footer>
    </div>
  );
}
