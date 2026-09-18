import { Eye } from "lucide-react";
import { useState } from "react";
import { useOperations } from "./features/operations/OperationsContext";
import type { Invoice } from "./features/operations/operations.types";
import { formatMoney } from "./lib/money";

function status(i:Invoice,d:ReturnType<typeof useOperations>["data"]){if(i.status==="Voided"||i.status==="Draft")return i.status;const p=d.payments.find(x=>x.id===i.paymentId||x.invoiceId===i.id);return p?.status==="Verified"?"Paid":new Date(i.issuedAt).getTime()<Date.now()-86400000?"Overdue":"Unpaid"}
export function BoltInvoicesPage({onNavigate}:{onNavigate:(path:string)=>void}){
 const{data}=useOperations();const[f,setF]=useState("All");const rows=data.invoices.filter(i=>f==="All"||status(i,data)===f).slice(0,100);
 return <main className="module-main bolt-page"><header className="page-header"><div><h1>Invoice History Ledger</h1><p>Access control, filtering, and printing records</p></div><button className="outline-action">Export Ledger</button></header><div className="header-divider"/>
 <section className="filter-bar panel"><strong>Filter By Status:</strong>{["All","Paid","Unpaid","Overdue"].map(x=><button key={x} className={`filter-chip ${f===x?"active":""}`} onClick={()=>setF(x)}>{x}</button>)}<span className="workflow-badge"><b>List Workflow</b> Instant Status Toggles</span></section>
 <section className="ledger-card panel"><div className="ledger-row ledger-heading"><span>DATE</span><span>ID</span><span>CUSTOMER</span><span>METAL CARAT WEIGHT</span><span className="align-right">VAL (+TAX)</span><span>STATUS</span><span>ACTIONS</span></div>
 {rows.map(i=><div className="ledger-row" key={i.id}><span className="ledger-date">{new Date(i.issuedAt).toLocaleDateString("en-US",{month:"short",day:"2-digit",year:"numeric"})}</span><span className="invoice-id">{i.invoiceNumber}</span><strong>{i.customerNameSnapshot||"Registered customer"}</strong><span className="muted">{i.items[0]?.productName||"—"}</span><span className="total">{formatMoney(i.grandTotalRials)}</span><span className={`status-badge ${status(i,data)==="Paid"?"paid":status(i,data)==="Overdue"?"overdue":""}`}>{status(i,data)}</span><button className="view-action" onClick={()=>onNavigate(`/invoices?open=${encodeURIComponent(i.id)}`)}>View <Eye size={14}/></button></div>)}</section></main>
}
