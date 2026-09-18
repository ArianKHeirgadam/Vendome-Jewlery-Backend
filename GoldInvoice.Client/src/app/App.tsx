import { useCallback, useEffect, useMemo, useState } from "react";
import { AppHeader } from "../components/AppHeader";
import { MarketRail } from "../components/MarketRail";
import { Sidebar } from "../components/Sidebar";
import { AuthenticationSplash, LoginPage, RecoveryCodesPage } from "../features/auth/AuthPages";
import { useAuthentication } from "../features/auth/AuthContext";
import { buildDashboardSnapshot } from "../features/dashboard/buildDashboardSnapshot";
import { DashboardPage } from "../features/dashboard/DashboardPage";
import type { ProfileSummary } from "../features/dashboard/dashboard.types";
import { type IntegrationEvent, useIntegrationRealtime } from "../features/integration/useIntegrationRealtime";
import { OperationsProvider, useOperations } from "../features/operations/OperationsContext";
import { OperationsRouter } from "../features/operations/OperationsPages";
import { useLocale, type AppLanguage } from "../i18n/LocaleContext";
import { BoltCustomersPage } from "../BoltCustomersPage";
import { BoltInvoicesPage } from "../BoltInvoicesPage";
import { BoltNewInvoicePage } from "../BoltNewInvoicePage";
import { BoltPaymentsPage } from "../BoltPaymentsPage";
import { BoltReportsPage } from "../BoltReportsPage";
import { BoltSettingsPage } from "../BoltSettingsPage";

type Theme="light"|"dark";
function getInitialTheme():Theme{return window.localStorage.getItem("vendome-theme")==="dark"?"dark":"light";}
function normalizePath(pathname:string){return ["/","/dashboard","/fa","/index.html"].includes(pathname)?"/":pathname;}
function getCurrentPath(){const hash=window.location.hash.replace(/^#/,"");return normalizePath(hash.startsWith("/")?hash:window.location.pathname);}
function roleLabel(roles:string[]){if(roles.includes("Owner"))return"مالک مجموعه";if(roles.includes("Admin"))return"مدیر مجموعه";if(roles.includes("Employee"))return"کارمند";return"مشتری";}
function initials(name:string){const p=name.trim().split(/\s+/).filter(Boolean);return p.length?`${p[0][0]||""}${p[1]?.[0]||""}`:"V";}
function eventNotice(event:IntegrationEvent){switch(event.eventType){case"invoice.created.v1":return"فاکتور جدید ثبت شد؛ اطلاعات داشبورد قابل به‌روزرسانی است.";case"inventory.changed.v1":return"موجودی انبار تغییر کرد؛ دادهٔ تازه از API دریافت می‌شود.";case"order.status-changed.v1":return"وضعیت یکی از سفارش‌ها تغییر کرد.";case"market-price.updated.v1":return"نرخ معتبر جدید بازار ثبت شد.";default:return"یک تغییر جدید از سرور دریافت شد.";}}
export function App(){const auth=useAuthentication();const{language,setLanguage}=useLocale();useEffect(()=>{if(language!=="en")setLanguage("en")},[language,setLanguage]);if(auth.status==="booting")return <AuthenticationSplash/>;if(auth.recoveryCodes)return <RecoveryCodesPage/>;if(auth.status==="anonymous")return <LoginPage/>;return <OperationsProvider><AuthenticatedApplication language={language}/></OperationsProvider>;}
function AuthenticatedApplication({language}:{language:AppLanguage}){const auth=useAuthentication();const operations=useOperations();const[theme,setTheme]=useState<Theme>(getInitialTheme);const[currentPath,setCurrentPath]=useState(getCurrentPath);const[sidebarOpen,setSidebarOpen]=useState(false);const[notice,setNotice]=useState<string|null>(null);const handleIntegrationEvent=useCallback((event:IntegrationEvent)=>{setNotice(eventNotice(event));void operations.refresh()},[operations.refresh]);const realtimeStatus=useIntegrationRealtime({auth,onEvent:handleIntegrationEvent});const profile=useMemo<ProfileSummary>(()=>{const name=auth.user?.displayName||auth.user?.email||"کاربر وندوم";return{displayName:name,role:roleLabel(auth.user?.roles??[]),initials:initials(name)}},[auth.user]);const dashboard=useMemo(()=>buildDashboardSnapshot(operations.data,profile),[operations.data,profile,language]);useEffect(()=>{document.documentElement.dataset.theme=theme;window.localStorage.setItem("vendome-theme",theme)},[theme]);useEffect(()=>{const sync=()=>setCurrentPath(getCurrentPath());window.addEventListener("popstate",sync);window.addEventListener("hashchange",sync);return()=>{window.removeEventListener("popstate",sync);window.removeEventListener("hashchange",sync)}},[]);useEffect(()=>{if(!notice)return;const t=window.setTimeout(()=>setNotice(null),4200);return()=>window.clearTimeout(t)},[notice]);const navigate=(path:string)=>{const destination=path==="/"?"/dashboard":path;window.location.hash=destination;setCurrentPath(normalizePath(path));setSidebarOpen(false)};const isDashboard=currentPath==="/"||currentPath==="/dashboard";const render=()=>{if(isDashboard)return <DashboardPage snapshot={dashboard} onNavigate={navigate}/>;if(currentPath==="/orders/new")return <BoltNewInvoicePage onNavigate={navigate} onNotice={setNotice}/>;
  if(currentPath==="/invoices")return <BoltInvoicesPage onNavigate={navigate}/>;if(currentPath==="/payments")return <BoltPaymentsPage/>;if(currentPath==="/reports")return <BoltReportsPage/>;if(currentPath==="/customers")return <BoltCustomersPage onNavigate={navigate}/>;if(currentPath==="/settings")return <BoltSettingsPage onNotice={setNotice}/>;return <OperationsRouter path={currentPath} onNavigate={navigate} onNotice={setNotice}/>;};return <div className="app-shell app-shell--bolt" data-language={language}><AppHeader profile={profile} theme={theme} realtimeStatus={realtimeStatus} onNavigate={navigate} onNotice={setNotice} onLogout={auth.logout} onToggleSidebar={()=>setSidebarOpen(x=>!x)} onToggleTheme={()=>setTheme(x=>x==="light"?"dark":"light")}/><Sidebar currentPath={currentPath} isOpen={sidebarOpen} onNavigate={navigate} onClose={()=>setSidebarOpen(false)}/><MarketRail market={dashboard.market}/>{render()}{sidebarOpen&&<button className="sidebar-backdrop" type="button" aria-label="بستن منو" onClick={()=>setSidebarOpen(false)}/>}<div className={`toast ${notice?"toast--visible":""}`} role="status" aria-live="polite">{notice}</div></div>;}
