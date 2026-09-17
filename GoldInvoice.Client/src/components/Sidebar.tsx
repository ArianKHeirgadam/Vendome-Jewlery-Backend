import type { LucideIcon } from "lucide-react";
import {
  BarChart3,
  Boxes,
  Calculator,
  FileText,
  Gem,
  IdCard,
  LayoutDashboard,
  Plus,
  Settings,
  ShoppingBag,
  Truck,
  UserRoundPlus,
  Users,
  X,
} from "lucide-react";

interface SidebarProps {
  currentPath: string;
  isOpen: boolean;
  onNavigate: (path: string) => void;
  onClose: () => void;
}

interface NavItem {
  label: string;
  path: string;
  icon: LucideIcon;
}

const navItems: NavItem[] = [
  { label: "داشبورد", path: "/", icon: LayoutDashboard },
  { label: "فاکتورها", path: "/invoices", icon: FileText },
  { label: "مشتریان", path: "/customers", icon: Users },
  { label: "محصولات", path: "/products", icon: Gem },
  { label: "انبار", path: "/inventory", icon: Boxes },
  { label: "سفارش‌ها", path: "/orders", icon: ShoppingBag },
  { label: "حسابداری", path: "/accounting", icon: Calculator },
  { label: "گزارش‌ها", path: "/reports", icon: BarChart3 },
  { label: "کارکنان", path: "/employees", icon: IdCard },
  { label: "تأمین‌کنندگان", path: "/suppliers", icon: Truck },
  { label: "تنظیمات", path: "/settings", icon: Settings },
];

const dashboardNavItems: NavItem[] = [
  { label: "Dashboard", path: "/", icon: LayoutDashboard },
  { label: "Invoices", path: "/invoices", icon: FileText },
  { label: "Customers", path: "/customers", icon: Users },
  { label: "Settings", path: "/settings", icon: Settings },
];

export function Sidebar({
  currentPath,
  isOpen,
  onNavigate,
  onClose,
}: SidebarProps) {
  const isDashboard = currentPath === "/" || currentPath === "/dashboard";
  const items = isDashboard ? dashboardNavItems : navItems;

  return (
    <aside className={`sidebar ${isOpen ? "sidebar--open" : ""}`}>
      {isDashboard && (
        <div className="dashboard-sidebar-brand">
          <strong>zarn</strong>
          <span>INVOICING &amp; ACCOUNTS</span>
          <div className="dashboard-sidebar-shortcuts">
            <small>SPEED SHORTCUTS</small>
            <button type="button" onClick={() => onNavigate("/orders/new")}>
              <Plus size={16} />
              <span>+ New Invoice</span>
              <kbd>F1</kbd>
            </button>
            <button type="button" onClick={() => onNavigate("/customers") }>
              <UserRoundPlus size={16} />
              <span>+ New Customer</span>
              <kbd>F2</kbd>
            </button>
          </div>
        </div>
      )}

      <div className="sidebar-mobile-heading">
        <span>Sections</span>
        <button type="button" aria-label="Close menu" onClick={onClose}>
          <X size={20} />
        </button>
      </div>

      <nav aria-label={isDashboard ? "Dashboard navigation" : "Sections"}>
        {!isDashboard && <p className="sidebar-label">بخش‌ها</p>}
        <div className="nav-list">
          {items.map((item) => {
            const Icon = item.icon;
            const basePath = currentPath.split("?")[0];
            const active = item.path === "/"
              ? basePath === "/" || basePath === "/dashboard"
              : basePath === item.path || basePath.startsWith(`${item.path}/`);
            return (
              <a
                className={`nav-item ${active ? "nav-item--active" : ""}`}
                href={item.path}
                key={item.path}
                aria-current={active ? "page" : undefined}
                onClick={(event) => {
                  event.preventDefault();
                  onNavigate(item.path);
                }}
              >
                <Icon size={18} strokeWidth={1.45} aria-hidden="true" />
                <span>{item.label}</span>
              </a>
            );
          })}
        </div>
      </nav>

      {!isDashboard && (
        <footer className="sidebar-footer">
          <p>مِزون وندوم · میدان وندوم، پاریس</p>
          <span>نسخه ۲.۴</span>
        </footer>
      )}
    </aside>
  );
}
