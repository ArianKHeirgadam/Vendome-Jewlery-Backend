import type { LucideIcon } from "lucide-react";
import {
  BarChart3,
  Boxes,
  Calculator,
  FileText,
  Gem,
  HeartHandshake,
  IdCard,
  LayoutDashboard,
  Settings,
  ShoppingBag,
  Truck,
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
  { label: "ارتباط با مشتری", path: "/crm", icon: HeartHandshake },
  { label: "تنظیمات", path: "/settings", icon: Settings },
];

export function Sidebar({
  currentPath,
  isOpen,
  onNavigate,
  onClose,
}: SidebarProps) {
  return (
    <aside className={`sidebar ${isOpen ? "sidebar--open" : ""}`}>
      <div className="sidebar-mobile-heading">
        <span>بخش‌ها</span>
        <button type="button" aria-label="بستن منو" onClick={onClose}>
          <X size={20} />
        </button>
      </div>
      <nav aria-label="بخش‌ها">
        <p className="sidebar-label">بخش‌ها</p>
        <div className="nav-list">
          {navItems.map((item) => {
            const Icon = item.icon;
            const active = item.path === currentPath;
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
      <footer className="sidebar-footer">
        <p>مِزون وندوم · میدان وندوم، پاریس</p>
        <span>نسخه ۲.۴</span>
      </footer>
    </aside>
  );
}
