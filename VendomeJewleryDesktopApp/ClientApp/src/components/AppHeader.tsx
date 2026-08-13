import { FormEvent, useState } from "react";
import {
  Bell,
  Languages,
  Menu,
  Moon,
  Search,
  Settings,
  Sun,
} from "lucide-react";
import type { ProfileSummary } from "../features/dashboard/dashboard.types";

interface AppHeaderProps {
  profile: ProfileSummary;
  theme: "light" | "dark";
  onNavigate: (path: string) => void;
  onNotice: (message: string) => void;
  onToggleSidebar: () => void;
  onToggleTheme: () => void;
}

export function AppHeader({
  profile,
  theme,
  onNavigate,
  onNotice,
  onToggleSidebar,
  onToggleTheme,
}: AppHeaderProps) {
  const [search, setSearch] = useState("");

  const submitSearch = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (!search.trim()) return;
    onNotice("جست‌وجوی سراسری پس از اتصال API فعال می‌شود.");
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
          className="header-icon-button"
          type="button"
          aria-label="زبان"
          onClick={() => onNotice("نسخهٔ فارسی برای رابط دسکتاپ فعال است.")}
        >
          <Languages size={18} strokeWidth={1.55} />
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
        <button
          className="profile-button"
          type="button"
          aria-label="پروفایل من"
          onClick={() => onNotice("پروفایل کاربر پس از اتصال احراز هویت فعال می‌شود.")}
        >
          <span className="profile-copy">
            <strong>{profile.displayName}</strong>
            <small>{profile.role}</small>
          </span>
          <span className="avatar">{profile.initials}</span>
        </button>
      </div>
    </header>
  );
}
