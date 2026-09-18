import type { LucideIcon } from "lucide-react";
import { BarChart3, Boxes, Calculator, CreditCard, FileText, Gem, Home, Package, Plus, Settings2, ShoppingBag, SlidersHorizontal, Truck, UserRoundPlus, Users, UserCog } from "lucide-react";

interface SidebarProps {
  currentPath: string;
  isOpen: boolean;
  onNavigate: (path: string) => void;
  onClose: () => void;
}
interface NavItem { label:string; path:string; icon:LucideIcon; }

const navItems:NavItem[]=[
  {label:"Dashboard",path:"/",icon:Home},
  {label:"Invoices",path:"/invoices",icon:FileText},
  {label:"Customers",path:"/customers",icon:Users},
  {label:"Products",path:"/products",icon:Gem},
  {label:"Inventory",path:"/inventory",icon:Boxes},
  {label:"Orders",path:"/orders",icon:ShoppingBag},
  {label:"Accounting",path:"/accounting",icon:Calculator},
  {label:"Reports",path:"/reports",icon:BarChart3},
  {label:"Employees",path:"/employees",icon:UserCog},
  {label:"Suppliers",path:"/suppliers",icon:Truck},
  {label:"Settings",path:"/settings",icon:SlidersHorizontal},
];

export function Sidebar({currentPath,isOpen,onNavigate,onClose}:SidebarProps){
  return <aside className={`sidebar ${isOpen?"sidebar--open":""}`}>
    <div className="dashboard-sidebar-brand">
      <strong>zarnorm</strong><span>INVOICING &amp; ACCOUNTS</span>
      <div className="dashboard-sidebar-shortcuts">
        <small>SPEED SHORTCUTS</small>
        <button type="button" onClick={()=>onNavigate("/orders/new")}><Plus size={16}/><span>New Invoice</span><kbd>F1</kbd></button>
        <button type="button" onClick={()=>onNavigate("/customers")}><UserRoundPlus size={16}/><span>New Customer</span><kbd>F2</kbd></button>
      </div>
    </div>
    <div className="sidebar-mobile-heading"><button type="button" aria-label="Close menu" onClick={onClose}>×</button></div>
    <nav aria-label="Main navigation"><div className="nav-list">{navItems.map(({label,path,icon:Icon})=>{
      const base=currentPath.split("?")[0]; const active=path==="/"?(base==="/"||base==="/dashboard"):path==="/invoices"&&base.startsWith("/orders/new")||base===path||base.startsWith(path+"/");
      return <a className={`nav-item ${active?"nav-item--active":""}`} href={path} key={path} aria-current={active?"page":undefined} onClick={e=>{e.preventDefault();onNavigate(path)}}><Icon size={18} strokeWidth={1.45}/><span>{label}</span></a>;
    })}</div></nav>
  </aside>;
}
