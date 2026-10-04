import { NavLink, Outlet } from 'react-router-dom';
import { SessionBox } from './AppLayout';

const NAV = [
  { to: '/vendor', label: 'Dashboard', end: true },
  { to: '/vendor/tenders', label: 'Tenders' },
  { to: '/vendor/loads', label: 'Loads' },
  { to: '/vendor/pods', label: 'POD' },
  { to: '/vendor/drivers', label: 'Drivers' },
  { to: '/vendor/capacity', label: 'Capacity' },
];

/** Transporter portal shell. Simpler than the internal screens, and laid out for phones: the nav wraps into a row. */
export function VendorLayout() {
  return (
    <div className="vendor-shell">
      <header className="vendor-top">
        <span className="brand">LogiVue · Transporter portal</span>
        <SessionBox />
      </header>
      <nav className="vendor-nav" aria-label="Portal">
        {NAV.map((item) => (
          <NavLink key={item.to} to={item.to} end={item.end} className={({ isActive }) => (isActive ? 'nav-link active' : 'nav-link')}>
            {item.label}
          </NavLink>
        ))}
      </nav>
      <main className="vendor-content">
        <Outlet />
      </main>
    </div>
  );
}
