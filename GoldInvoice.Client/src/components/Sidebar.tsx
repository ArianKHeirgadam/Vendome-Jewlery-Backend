import type { LucideIcon } from "lucide-react";
import { FileText, LayoutDashboard, Plus, Settings, UserRoundPlus, Users, CreditCard, BarChart3 } from "lucide-react";

interface SidebarProps {
  currentPath: string;
  isOpen: boolean;
  onNavigate: (path: string) => void;
  onClose: () => void;
}
interface NavItem { label:string; path:string; icon:LucideIcon; }

const navItems:NavItem[]=[
  {label:"Dashboard",path:"/",icon:LayoutDashboard},
  {label:"Invoices",path:"/invoices",icon:FileText},
  {label:"Payments",path:"/payments",icon:CreditCard},
  {label:"Reports",path:"/reports",icon:BarChart3},
  {label:"Customers",path:"/customers",icon:Users},
  {label:"Settings",path:"/settings",icon:Settings},
];

export function Sidebar({currentPath,isOpen,onNavigate,onClose}:SidebarProps){
  return <aside className={`sidebar ${isOpen?"sidebar--open":""}`}>
    <div className="dashboard-sidebar-brand">
      <strong>zarnorm</strong><span>INVOICING &amp; ACCOUNTS</span>
      <div className="dashboard-sidebar-shortcuts">
        <small>SPEED SHORTCUTS</small>
        <button type="button" onClick={()=>onNavigate("/orders/new")}><Plus size={16}/><span>+ New Invoice</span><kbd>F1</kbd></button>
        <button type="button" onClick={()=>onNavigate("/customers")}><UserRoundPlus size={16}/><span>+ New Customer</span><kbd>F2</kbd></button>
      </div>
    </div>
    <div className="sidebar-mobile-heading"><button type="button" aria-label="Close menu" onClick={onClose}>×</button></div>
    <nav aria-label="Main navigation"><div className="nav-list">{navItems.map(({label,path,icon:Icon})=>{
      const base=currentPath.split("?")[0]; const active=path==="/"?(base==="/"||base==="/dashboard"):base===path||base.startsWith(path+"/");
      return <a className={`nav-item ${active?"nav-item--active":""}`} href={path} key={path} aria-current={active?"page":undefined} onClick={e=>{e.preventDefault();onNavigate(path)}}><Icon size={18} strokeWidth={1.45}/><span>{label}</span></a>;
    })}</div></nav>
  </aside>;
}
