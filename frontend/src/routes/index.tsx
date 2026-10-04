import { createBrowserRouter, Navigate } from 'react-router-dom';
import { AppLayout } from '../layouts/AppLayout';
import { VendorLayout } from '../layouts/VendorLayout';
import { AlertsPage } from '../modules/transporter-management/pages/AlertsPage';
import { TenderDetailsPage } from '../modules/transporter-management/pages/TenderDetailsPage';
import { TenderListPage } from '../modules/transporter-management/pages/TenderListPage';
import { TransporterBenchmarkPage } from '../modules/transporter-management/pages/TransporterBenchmarkPage';
import { TransporterDetailsPage } from '../modules/transporter-management/pages/TransporterDetailsPage';
import { TransporterListPage } from '../modules/transporter-management/pages/TransporterListPage';
import { TransporterRankingPage } from '../modules/transporter-management/pages/TransporterRankingPage';
import { VehiclePlacementPage } from '../modules/transporter-management/pages/VehiclePlacementPage';
import {
  VendorCapacityPage,
  VendorDashboardPage,
  VendorDriversPage,
  VendorLoadsPage,
  VendorPodsPage,
  VendorTendersPage,
} from '../modules/vendor-portal/pages/VendorPages';

export const router = createBrowserRouter([
  {
    element: <AppLayout />,
    children: [
      { index: true, element: <Navigate to="/transporters" replace /> },
      { path: 'transporters', element: <TransporterListPage /> },
      { path: 'transporters/rankings', element: <TransporterRankingPage /> },
      { path: 'transporters/benchmark', element: <TransporterBenchmarkPage /> },
      { path: 'transporters/:id', element: <TransporterDetailsPage /> },
      { path: 'tenders', element: <TenderListPage /> },
      { path: 'tenders/:id', element: <TenderDetailsPage /> },
      { path: 'placements', element: <VehiclePlacementPage /> },
      { path: 'alerts', element: <AlertsPage /> },
    ],
  },
  {
    path: 'vendor',
    element: <VendorLayout />,
    children: [
      { index: true, element: <VendorDashboardPage /> },
      { path: 'tenders', element: <VendorTendersPage /> },
      { path: 'loads', element: <VendorLoadsPage /> },
      { path: 'pods', element: <VendorPodsPage /> },
      { path: 'drivers', element: <VendorDriversPage /> },
      { path: 'capacity', element: <VendorCapacityPage /> },
    ],
  },
]);
