import assert from 'node:assert/strict';

export async function lifecycle(b) {
 const { go, wait, click, set, ev, login, logout, capture, test, resetSession }=b;
 const q=JSON.stringify;
 const dialog='#workspace-dialog ';
 const submit=(s='form')=>click(s+' button[type=submit]');
 const has=s=>wait('!!document.querySelector('+q(s)+')',s);
 const words=x=>wait('document.querySelector("#main")?.innerText.includes('+q(x)+')',x);
 const closed=()=>wait('!document.querySelector("#workspace-dialog")?.open','dialog closed');
 const choose=async (s,v)=>set(s,v);
 const modal=async(s,field)=>{await has(s);await click(s);await has(dialog+field);};
 const confirm=async()=>{if(await ev('document.querySelector("#workspace-confirm")?.open'))await click('#workspace-confirm button[value=confirm]');};
 async function act(s){await click(s);await new Promise(r=>setTimeout(r,250));await confirm();}
 async function as(role,path){
   await wait('document.readyState==="complete"','settled before switching to '+role);
   for(let attempt=0;;attempt++){
     await resetSession();
     try { await login(role); break; }
     catch(error){ if(attempt===2) throw error; }
   }
   await wait('!document.querySelector("#password-sign-in") && document.readyState==="complete"',role+' signed in');
   if(path)await go(path);
   await has('form[action*=Logout]');
   await wait('document.querySelector(".bar-profile small")?.textContent.toLowerCase().trim()==='+q(role.toLowerCase()+'@example.test'),role+' identity');
 }
 async function fields(obj,root=''){for(const [name,value] of Object.entries(obj)) await set(root+'[name="'+name+'"]',String(value));}
 const iso=(minutes)=>new Date(Date.now()+minutes*60000).toISOString().slice(0,16);
 const idFrom=async()=>Number(await ev('location.pathname.split("/").at(-1)'));
 async function attachment(selector){
   await ev(`(()=>{const input=document.querySelector(${q(selector)}); const file=new File(['%PDF-1.4\\nSynthetic browser evidence\\n%%EOF'], 'evidence.pdf', {type:'application/pdf'}); const data=new DataTransfer();data.items.add(file);input.files=data.files;input.dispatchEvent(new Event('change',{bubbles:true}));})()`);
 }
 async function request(title){
   await as('DepartmentSmoke','/PurchaseRequisitions/MyRequests');
   await modal('#main a[data-workspace-dialog][href*=Create]','input[name=Title]');
   await fields({Title:title,Reason:'Synthetic purchase',TargetDate:iso(14400).slice(0,10)},dialog);
   await submit(dialog+'form');await words(title);await closed();
   for(const name of ['Monitor','Cable']){
     await modal('a[href*="/RequisitionItems/Create"]','input[name=ItemName]');
     await fields({ItemName:name,Quantity:2,Unit:'each',Specifications:'Synthetic specification'},dialog);
     await submit(dialog+'form');await closed();await words(name);
   }
   const id=await idFrom();
   await submit('form[action*="/Submit/"]');await words('Under Review');
   await as('SourcingOfficer','/RequisitionReviews/Details/'+id);
   await set('textarea[name=Notes]','Approved for browser QA');
   await click('button[formaction*="/ApproveAndCreateTender/"]');
   await has(dialog+'input[name=Title]');
   return id;
 }
 async function makeTender(title){
   await fields({Title:title,Description:'Synthetic tender',Terms:'Test terms',OpenDate:iso(-5),CloseDate:iso(180)},dialog);
   await submit(dialog+'form');await closed();await words(title);
   const tender=await ev(`Array.from(document.querySelectorAll('a[href*="/Tenders/Details/"]')).find(a=>a.closest('tr')?.innerText.includes(${q(title)}))?.href`);
   assert(tender,'Tender details link missing');
   await go(new URL(tender).pathname);return await idFrom();
 }
 async function configure(id){
   await modal('a[href*="/Tenders/Criteria/"]','input[name=Name]');
   await fields({Name:'Quality',Type:'Technical',MaxScore:100,WeightPercent:100},dialog);
   await submit(dialog+'form');await words('Quality');await closed();
   await go('/Tenders/Details/'+id);
   await modal('#main a[href*="/Invitations/Create"]','input[name=selectedSuppliers]');
   assert.equal(await ev('document.querySelector("#workspace-dialog select[name=TenderId]").value'),String(id));
   await ev('document.querySelectorAll("#workspace-dialog input[name=selectedSuppliers]").forEach(i=>i.checked=true)');
   await fields({ExpiresAt:iso(90)},dialog);await submit(dialog+'form');await closed();
   await go('/Tenders/Details/'+id);
   await submit('form[action*="/Publish/"]');await wait('document.querySelector("#main .badge")?.textContent.trim()==="Open"','published Open');
 }
 async function bid(role,id,price){
   await as(role,'/SupplierWorkspace/Invitations');
   const select='article';
   const article=await ev('Array.from(document.querySelectorAll("article")).findIndex(a=>a.innerText.includes('+q('TENDER #'+id+' ·')+'))');
   assert(article>=0,'Invitation missing');
   const row=select+':nth-of-type('+(article+1)+')';
   await click(row+' button[value=Accepted]');
   await modal(row+' a[href*="/Bids/Create"]','[name=Currency]');
   await fields({Currency:'JOD',DeliveryDays:3,ValidityDays:7},dialog);
   await submit(dialog+'form');await closed();await has('a[href*="/BidItems/Create"]');
   const bidId=await idFrom();
   for(let n=0;n<2;n++){
     await modal('a[href*="/BidItems/Create"]','input[name=UnitPrice]');
     await fields({UnitPrice:price},dialog);await submit(dialog+'form');await closed();
   }
   await modal('a[href*="/BidDocuments/Upload"]','input[type=file]');
   await ev('const opt=document.querySelector("#workspace-dialog select[name=DocumentType]");opt.selectedIndex=1;');
   await attachment(dialog+'input[type=file]');await submit(dialog+'form');await closed();
   const submitBid='form[action*="/Bids/Submit/"] button[type=submit]';
   await has(submitBid);
   assert((await ev('document.querySelector('+q(submitBid)+').form.action')).endsWith('/'+bidId));
   await act(submitBid);
   await wait('!document.querySelector('+q(submitBid)+') && Array.from(document.querySelectorAll("#main .badge,#main .status-pill")).some(x=>x.textContent.trim()==="Submitted")','bid '+bidId+' submitted');
   return bidId;
 }
 async function review(id){
   await as('SourcingOfficer','/Compliance/Review?tenderId='+id);
   await ev(`document.querySelector('form[action*="/Compliance/Close"]').closest('details').open=true`);
   await click('input[name=confirmClose]');await submit('form[action*="/Compliance/Close"]');
   await words('Closed');
 }
 async function evaluate(id,bids){
   for(const bidId of bids){await act('form[action*="/MarkCompliant/'+bidId+'"] button');await wait('!document.querySelector('+q('form[action*="/MarkCompliant/'+bidId+'"]')+')','compliance saved');}
   await go('/Tenders/AssignCommittee/'+id);await submit('form[action*="/AssignCommittee/"]');
   for(const role of ['DepartmentSmoke','TechnicalCommittee','FinancialCommittee','SourcingManager']){
     await as(role,'/CommitteeReviews/Details/'+id);
     for(const [i,bidId] of bids.entries()){
       await go('/CommitteeReviews/Bid/'+bidId);await has('input[name=Score]');
       await fields({Score:90-i*10,Notes:'Recorded in Chrome'});
       await submit('form[action*="/SaveScore/"]');await words('Your review score was saved.');
     }
     await go('/CommitteeReviews/Details/'+id);
     await act('form[action*="/SubmitEvaluation/"] button');await words('Your scores are read-only');
   }
   await go('/EvaluationWorkflow/Details/'+id);await words('4 / 4 submitted');
   await fields({RecommendedBidId:bids[0],Summary:'Highest normalized score'});
   await attachment('form[action*="/Upload/"] input[type=file]');await submit('form[action*="/Upload/"]');
   await words('Pending Officer Approval');
 }
 async function decision(role,id,status,kind='approve'){
   await as(role,'/EvaluationWorkflow/Details/'+id);
   if(kind==='reject'){
      await modal('a[href*="/EvaluationWorkflow/Reject/"]','textarea[name=comments]');
      await fields({comments:'Documented synthetic rejection',password:'Synthetic-Browser!2046'},dialog);
      await click(dialog+'input[name=confirmDecision]');await submit(dialog+'form');
   } else {
      await fields({password:'Synthetic-Browser!2046'});await click('input[name=confirmDecision]');
      await click('form[action*="/Approve/"] button[type=submit]');
   }
   await words(status);
   if(role.startsWith('Top') && kind==='approve') await words(status==='Awarded'?'Tender awarded.':'Vote recorded.');
 }
 async function prepare(title,roles){
   const rid=await request(title+' requisition'), id=await makeTender(title);
   await configure(id);
   const bids=[];for(const [index,role] of roles.entries())bids.push(await bid(role,id,25+index*5));
   await review(id);
   return {rid,id,bids};
 }
 await test('P6-A-award',async()=>{
   const {id,bids}=await prepare('Phase 6 Award',['Supplier','Supplier2','Supplier3']);
   await evaluate(id,bids);
   await decision('SourcingOfficer',id,'Pending Manager Approval');
   await decision('SourcingManager',id,'Pending Top Committee');
   for(const [i,role] of ['TopCommittee','Top2','Top3','Top4'].entries())
     await decision(role,id,i===3?'Awarded':'Pending Top Committee');
   await capture('P6-A-awarded');
   await as('Supplier2','/Bids/MyBids');
   const foreign=await ev(`fetch('/Bids/Details/${bids[0]}').then(r=>r.status)`);
   assert([403,404].includes(foreign));
   await as('Supplier','/Bids/Details/'+bids[0]);
   const doc=await ev(`document.querySelector('a[href*="/BidDocuments/Download/"]')?.pathname`);
   assert(doc);
   await as('Supplier2','/Bids/MyBids');
   assert([403,404].includes(await ev('fetch('+q(doc)+').then(r=>r.status)')));
   await as('Admin','/AdminDashboard');
   for(const [i,role] of ['Supplier','Supplier2','Supplier3'].entries()){
     await as(role,'/Bids/Details/'+bids[i]);await words(i===0?'Result: Awarded':'Result: Unsuccessful');
   }
 });
 for(const count of [2,1,0]) await test(count===2?'P6-B-rounds':'P6-B-rounds-'+count,async()=>{
   const {id,bids}=await prepare(count===2?'Phase 6 Rounds':'Phase 6 Rounds '+count,['Supplier','Supplier2'].slice(0,count));
   for(let round=2;round<=3;round++){
     await go('/Tenders/Details/'+id);
     await words('Evaluation is blocked');
     await modal('a[href*="/Tenders/Reopen/"]','input[name=closeDate]');
     await fields({closeDate:iso(180),reason:'Not enough bids'},dialog);await submit(dialog+'form');await closed();
     await words('Round '+round+' of 3');
     await modal('#main a[href*="/Invitations/Create"]','input[name=selectedSuppliers]');
     assert.equal(await ev('document.querySelectorAll("#workspace-dialog input[name=selectedSuppliers]").length'),3-count);
     await click(dialog+'input[name=selectedSuppliers]');await fields({ExpiresAt:iso(90)},dialog);
     await submit(dialog+'form');await closed();
     await review(id);
   }
   await go('/Tenders/Details/'+id);await words(count?'Bid minimum met':'Evaluation is blocked');await words('All three rounds have been used');
   const token=await ev('document.querySelector("form[action*=Logout] input[name=__RequestVerificationToken]").value');
   await ev(`fetch('/Tenders/Reopen/${id}',{method:'POST',body:new URLSearchParams({__RequestVerificationToken:${q(token)},closeDate:${q(iso(180))},reason:'Invalid fourth attempt'})}).then(r=>r.text())`);
   await go('/Tenders/Details/'+id);await words('Round 3 of 3');
   await capture(count===2?'P6-B-three-rounds':'P6-B-three-rounds-'+count);
   if(!count){
     await ev(`fetch('/Tenders/AssignCommittee/${id}',{method:'POST',body:new URLSearchParams({__RequestVerificationToken:${q(token)}})}).then(r=>r.text())`);
     return;
   }
   for(const bidId of bids){await go('/Compliance/Review?tenderId='+id);await act('form[action*="/MarkCompliant/'+bidId+'"] button');await wait('!document.querySelector('+q('form[action*="/MarkCompliant/'+bidId+'"]')+')','compliance saved');}
   await go('/Tenders/AssignCommittee/'+id);await submit('form[action*="/AssignCommittee/"]');await wait('location.pathname==="/Tenders" || location.pathname==="/Tenders/Index"','assignment response');await go('/Tenders/Details/'+id);await words('Under Evaluation');
 });
 await test('P6-C-cancellation',async()=>{
   const {id,rid,bids}=await prepare('Phase 6 Cancel',['Supplier','Supplier2','Supplier3']);
   await evaluate(id,bids);
   await decision('SourcingOfficer',id,'Pending Manager Approval');
   await decision('SourcingManager',id,'Pending Top Committee');
   await decision('TopCommittee',id,'Cancelled','reject');
   await capture('P6-C-cancelled');
   await as('Top2','/EvaluationWorkflow/Details/'+id);assert.equal(await ev(`!!document.querySelector('form[action*="/Approve/"]')`),false);
   await as('SourcingManager','/EvaluationWorkflow/Details/'+id);
   await submit('form[action*="/ReturnToOfficer/"]');await words('Ready to Retender');
   await as('SourcingOfficer','/EvaluationWorkflow/Details/'+id);
   await modal('#main a[href*="/Tenders/Create"]','input[name=Title]');
   assert.equal(await ev('document.querySelector("#workspace-dialog select[name=PurchaseRequisitionId]").value'),String(rid));
   await makeTender('Phase 6 Replacement');
   await capture('P6-C-replacement');
 });
}
