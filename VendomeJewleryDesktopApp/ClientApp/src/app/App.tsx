import { useEffect, useState } from "react";
import { AppHeader } from "../components/AppHeader";
import { MarketRail } from "../components/MarketRail";
import { Sidebar } from "../components/Sidebar";
import { dashboardMock } from "../features/dashboard/dashboard.mock";
import { DashboardPage } from "../features/dashboard/DashboardPage";

type Theme = "light" | "dark";

const isDesktopShell = window.location.hostname === "desktop.vendome.example";

function getInitialTheme(): Theme {
  const stored = window.localStorage.getItem("vendome-theme");
  return stored === "dark" ? "dark" : "light";
}

function normalizePath(pathname: string) {
  return pathname === "/dashboard" ||
    pathname === "/fa" ||
    pathname === "/index.html"
    ? "/"
    : pathname;
}

function getCurrentPath() {
  const locationPath = isDesktopShell
    ? window.location.hash.replace(/^#/, "")
    : window.location.pathname;

  return normalizePath(locationPath || "/");
}

export function App() {
  const [theme, setTheme] = useState<Theme>(getInitialTheme);
  const [currentPath, setCurrentPath] = useState(getCurrentPath);
  const [sidebarOpen, setSidebarOpen] = useState(false);
  const [notice, setNotice] = useState<string | null>(null);

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

    const timeout = window.setTimeout(() => setNotice(null), 3200);
    return () => window.clearTimeout(timeout);
  }, [notice]);

  const navigate = (path: string) => {
    if (path === "/" || path === "/dashboard") {
      const target = isDesktopShell ? `#${path}` : path;
      window.history.pushState({}, "", target);
      setCurrentPath("/");
      setSidebarOpen(false);
      return;
    }

    setNotice("این بخش در مرحلهٔ بعد به API واقعی متصل می‌شود.");
    setSidebarOpen(false);
  };

  return (
    <div className="app-shell">
      <AppHeader
        profile={dashboardMock.profile}
        theme={theme}
        onNavigate={navigate}
        onNotice={setNotice}
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
      <MarketRail market={dashboardMock.market} />
      <DashboardPage
        snapshot={dashboardMock}
        onAction={() =>
          setNotice("اتصال فرم فاکتور به API، مرحلهٔ بعدی همین مسیر است.")
        }
      />

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
