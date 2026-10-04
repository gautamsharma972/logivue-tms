import { useState } from 'react';
import { NavLink, Outlet } from 'react-router-dom';
import { getToken, setToken } from '../core/config';

const NAV = [
  { to: '/transporters', label: 'Transporters' },
  { to: '/transporters/rankings', label: 'Ranking' },
  { to: '/transporters/benchmark', label: 'Benchmark' },
  { to: '/tenders', label: 'Tenders' },
  { to: '/placements', label: 'Vehicle placement' },
  { to: '/alerts', label: 'Alerts' },
  { to: '/vendor', label: 'Vendor portal' },
];

/** Internal TMS shell. Desktop-first; the sidebar collapses to a top bar on narrow screens via CSS. */
export function AppLayout() {
  return (
    <div className="app-shell">
      <aside className="sidebar">
        <div className="brand">LogiVue TMS</div>
        <nav aria-label="Main">
          {NAV.map((item) => (
            <NavLink key={item.to} to={item.to} end={item.to === '/transporters'} className={({ isActive }) => (isActive ? 'nav-link active' : 'nav-link')}>
              {item.label}
            </NavLink>
          ))}
        </nav>
        <SessionBox />
      </aside>
      <main className="content">
        <Outlet />
      </main>
    </div>
  );
}

/**
 * Development session: paste a token issued by the identity provider. Stored for this tab only. Production uses the
 * identity provider's sign-in flow instead.
 */
export function SessionBox() {
  const [open, setOpen] = useState(false);
  const [draft, setDraft] = useState('');
  const signedIn = Boolean(getToken());
  return (
    <div className="session">
      <button type="button" className="link-button" onClick={() => setOpen(!open)}>
        {signedIn ? 'Session: token set' : 'Session: sign in'}
      </button>
      {open && (
        <div className="session-form">
          <textarea aria-label="Bearer token" rows={3} value={draft} onChange={(e) => setDraft(e.target.value)} placeholder="Paste a bearer token" />
          <div className="toolbar">
            <button type="button" onClick={() => { setToken(draft.trim() || null); setDraft(''); setOpen(false); window.location.reload(); }}>Use token</button>
            <button type="button" onClick={() => { setToken(null); window.location.reload(); }}>Clear</button>
          </div>
        </div>
      )}
    </div>
  );
}
