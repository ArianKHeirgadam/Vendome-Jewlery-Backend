import { FormEvent, useState } from "react";
import {
  Bell,
  ChevronDown,
  Languages,
  LogOut,
  Menu,
  Moon,
  Search,
  Settings,
  Sun,
  UserRound,
  Wifi,
  WifiOff,
} from "lucide-react";
import type { ProfileSummary } from "../features/dashboard/dashboard.types";
import type { RealtimeStatus } from "../features/integration/useIntegrationRealtime";
import { useLocale } from "../i18n/LocaleContext";

interface AppHeaderProps {
  profile: ProfileSummary;
  theme: "light" | "dark";
  realtimeStatus: RealtimeStatus;
  onNavigate: (path: string) => void;
  onNotice: (message: string) => void;
  onLogout: () => Promise<void>;
  onToggleSidebar: () => void;
  onToggleTheme: () => void;
}

export function AppHeader({
  profile,
  theme,
  realtimeStatus,
  onNavigate,
  onNotice,
  onLogout,
  onToggleSidebar,
  onToggleTheme,
}: AppHeaderProps) {
  const { language, toggleLanguage } = useLocale();
  const [search, setSearch] = useState("");
  const [profileMenuOpen, setProfileMenuOpen] = useState(false);

  const submitSearch = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (!search.trim()) return;
    onNavigate(`/search?q=${encodeURIComponent(search.trim())}`);
  };

  return (
    <header className="app-header">
      <div className="header-brand-wrap">
        <button
          className="mobile-menu-button"
          type="button"
          aria-label="باز کردن منو"
          onClick={onToggleSidebar}
        >
          <Menu aria-hidden="true" />
        </button>
        <a
          className="brand"
          href="/"
          onClick={(event) => {
            event.preventDefault();
            onNavigate("/");
          }}
        >
          <span className="brand-latin">VENDÔME</span>
          <span className="brand-divider" aria-hidden="true" />
          <span className="brand-fa">مِزون وندوم</span>
        </a>
      </div>

      <form className="global-search" role="search" onSubmit={submitSearch}>
        <Search size={18} strokeWidth={1.7} aria-hidden="true" />
        <input
          type="search"
          aria-label="جست‌وجوی سراسری"
          placeholder="جست‌وجوی فاکتور، مشتری، کد کالا…"
          value={search}
          onChange={(event) => setSearch(event.target.value)}
        />
      </form>

      <div className="header-actions">
        <span
          className={`realtime-status realtime-status--${realtimeStatus}`}
          title="وضعیت همگام‌سازی لحظه‌ای"
        >
          {realtimeStatus === "connected" ? (
            <Wifi size={15} aria-hidden="true" />
          ) : (
            <WifiOff size={15} aria-hidden="true" />
          )}
          <span>
            {realtimeStatus === "connected" && "همگام"}
            {realtimeStatus === "connecting" && "در حال اتصال"}
            {realtimeStatus === "reconnecting" && "اتصال مجدد"}
            {realtimeStatus === "offline" && "آفلاین"}
          </span>
        </span>
        <button
          className="header-icon-button notification-button"
          type="button"
          aria-label="اعلان‌ها"
          onClick={() => onNotice("اعلان جدیدی ندارید.")}
        >
          <Bell size={18} strokeWidth={1.55} />
          <span className="notification-dot" />
        </button>
        <button
          className="header-icon-button language-button"
          type="button"
          aria-label={language === "fa" ? "تغییر به انگلیسی" : "تغییر به فارسی"}
          title={language === "fa" ? "English" : "فارسی"}
          onClick={toggleLanguage}
        >
          <Languages size={18} strokeWidth={1.55} />
          <span>{language === "fa" ? "EN" : "فا"}</span>
        </button>
        <button
          className="header-icon-button"
          type="button"
          aria-label={theme === "light" ? "تغییر به حالت تیره" : "تغییر به حالت روشن"}
          onClick={onToggleTheme}
        >
          {theme === "light" ? (
            <Moon size={18} strokeWidth={1.55} />
          ) : (
            <Sun size={18} strokeWidth={1.55} />
          )}
        </button>
        <button
          className="header-icon-button"
          type="button"
          aria-label="تنظیمات"
          onClick={() => onNavigate("/settings")}
        >
          <Settings size={18} strokeWidth={1.55} />
        </button>
        <div className="profile-menu-wrap">
          <div className="profile-control">
            <button
              className="profile-button"
              type="button"
              aria-label="رفتن به پروفایل مدیر"
              onClick={() => {
                setProfileMenuOpen(false);
                onNavigate("/profile");
              }}
            >
            <span className="profile-copy">
              <strong>{profile.displayName}</strong>
              <small>{profile.role}</small>
            </span>
            <span className="avatar">{profile.initials}</span>
            </button>
            <button
              className="profile-menu-toggle"
              type="button"
              aria-label="باز کردن منوی حساب"
              aria-expanded={profileMenuOpen}
              onClick={() => setProfileMenuOpen((open) => !open)}
            >
              <ChevronDown size={14} className="profile-chevron" />
            </button>
          </div>
          {profileMenuOpen && (
            <div className="profile-menu" role="menu">
              <button
                type="button"
                role="menuitem"
                onClick={() => {
                  setProfileMenuOpen(false);
                  onNavigate("/profile");
                }}
              >
                <UserRound size={16} /> پروفایل مدیر
              </button>
              <button
                type="button"
                role="menuitem"
                onClick={() => {
                  setProfileMenuOpen(false);
                  void onLogout();
                }}
              >
                <LogOut size={16} /> خروج امن
              </button>
            </div>
          )}
        </div>
      </div>
    </header>
  );
}
