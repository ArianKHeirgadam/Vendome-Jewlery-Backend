import { useCallback, useEffect, useMemo, useState } from "react";
import { AppHeader } from "../components/AppHeader";
import { MarketRail } from "../components/MarketRail";
import { Sidebar } from "../components/Sidebar";
import {
  AuthenticationSplash,
  LoginPage,
  RecoveryCodesPage,
} from "../features/auth/AuthPages";
import { useAuthentication } from "../features/auth/AuthContext";
import { buildDashboardSnapshot } from "../features/dashboard/buildDashboardSnapshot";
import { DashboardPage } from "../features/dashboard/DashboardPage";
import type { ProfileSummary } from "../features/dashboard/dashboard.types";
import {
  type IntegrationEvent,
  useIntegrationRealtime,
} from "../features/integration/useIntegrationRealtime";
import { OperationsProvider, useOperations } from "../features/operations/OperationsContext";
import { OperationsRouter } from "../features/operations/OperationsPages";

type Theme = "light" | "dark";

function getInitialTheme(): Theme {
  const stored = window.localStorage.getItem("vendome-theme");
  return stored === "dark" ? "dark" : "light";
}

function normalizePath(pathname: string) {
  return ["/", "/dashboard", "/fa", "/index.html"].includes(pathname)
    ? "/"
    : pathname;
}

function getCurrentPath() {
  const hashPath = window.location.hash.replace(/^#/, "");
  return normalizePath(hashPath.startsWith("/") ? hashPath : window.location.pathname);
}

function roleLabel(roles: string[]): string {
  if (roles.includes("Owner")) return "مالک مجموعه";
  if (roles.includes("Admin")) return "مدیر مجموعه";
  return "مشتری";
}

function makeInitials(displayName: string): string {
  const pieces = displayName.trim().split(/\s+/).filter(Boolean);
  if (!pieces.length) return "V";
  return `${pieces[0][0] ?? ""}${pieces[1]?.[0] ?? ""}`;
}

function eventNotice(event: IntegrationEvent): string {
  switch (event.eventType) {
    case "invoice.created.v1":
      return "فاکتور جدید ثبت شد؛ اطلاعات داشبورد قابل به‌روزرسانی است.";
    case "inventory.changed.v1":
      return "موجودی انبار تغییر کرد؛ دادهٔ تازه از API دریافت می‌شود.";
    case "order.status-changed.v1":
      return "وضعیت یکی از سفارش‌ها تغییر کرد.";
    case "market-price.updated.v1":
      return "نرخ معتبر جدید بازار ثبت شد.";
    default:
      return "یک تغییر جدید از سرور دریافت شد.";
  }
}

export function App() {
  const auth = useAuthentication();

  if (auth.status === "booting") return <AuthenticationSplash />;
  if (auth.recoveryCodes) return <RecoveryCodesPage />;
  if (auth.status === "anonymous") return <LoginPage />;
  return (
    <OperationsProvider>
      <AuthenticatedApplication />
    </OperationsProvider>
  );
}

function AuthenticatedApplication() {
  const auth = useAuthentication();
  const operations = useOperations();
  const [theme, setTheme] = useState<Theme>(getInitialTheme);
  const [currentPath, setCurrentPath] = useState(getCurrentPath);
  const [sidebarOpen, setSidebarOpen] = useState(false);
  const [notice, setNotice] = useState<string | null>(null);

  const handleIntegrationEvent = useCallback((event: IntegrationEvent) => {
    setNotice(eventNotice(event));
    void operations.refresh();
  }, [operations.refresh]);
  const realtimeStatus = useIntegrationRealtime({
    auth,
    onEvent: handleIntegrationEvent,
  });

  const profile = useMemo<ProfileSummary>(() => {
    const displayName = auth.user?.displayName || auth.user?.email || "کاربر وندوم";
    return {
      displayName,
      role: roleLabel(auth.user?.roles ?? []),
      initials: makeInitials(displayName),
    };
  }, [auth.user]);
  const dashboard = useMemo(
    () => buildDashboardSnapshot(operations.data, profile),
    [operations.data, profile],
  );

  useEffect(() => {
    document.documentElement.dataset.theme = theme;
    window.localStorage.setItem("vendome-theme", theme);
  }, [theme]);

  useEffect(() => {
    const handleLocationChange = () => setCurrentPath(getCurrentPath());
    window.addEventListener("popstate", handleLocationChange);
    window.addEventListener("hashchange", handleLocationChange);
    return () => {
      window.removeEventListener("popstate", handleLocationChange);
      window.removeEventListener("hashchange", handleLocationChange);
    };
  }, []);

  useEffect(() => {
    if (!notice) return;
    const timeout = window.setTimeout(() => setNotice(null), 4200);
    return () => window.clearTimeout(timeout);
  }, [notice]);

  const navigate = (path: string) => {
    const destination = path === "/" ? "/dashboard" : path;
    window.location.hash = destination;
    setCurrentPath(normalizePath(path));
    setSidebarOpen(false);
  };

  const isDashboard = currentPath === "/" || currentPath === "/dashboard";

  return (
    <div className="app-shell">
      <AppHeader
        profile={profile}
        theme={theme}
        realtimeStatus={realtimeStatus}
        onNavigate={navigate}
        onNotice={setNotice}
        onLogout={auth.logout}
        onToggleSidebar={() => setSidebarOpen((open) => !open)}
        onToggleTheme={() =>
          setTheme((activeTheme) =>
            activeTheme === "light" ? "dark" : "light",
          )
        }
      />
      <Sidebar
        currentPath={currentPath}
        isOpen={sidebarOpen}
        onNavigate={navigate}
        onClose={() => setSidebarOpen(false)}
      />
      {isDashboard ? (
        <>
          <MarketRail market={dashboard.market} />
          <DashboardPage snapshot={dashboard} onNavigate={navigate} />
        </>
      ) : (
        <OperationsRouter
          path={currentPath}
          onNavigate={navigate}
          onNotice={setNotice}
        />
      )}

      {sidebarOpen && (
        <button
          className="sidebar-backdrop"
          type="button"
          aria-label="بستن منو"
          onClick={() => setSidebarOpen(false)}
        />
      )}

      <div
        className={`toast ${notice ? "toast--visible" : ""}`}
        role="status"
        aria-live="polite"
      >
        {notice}
      </div>
    </div>
  );
}
