import assert from 'node:assert/strict';

export async function lifecycle(b) {
 const { go, wait, click, set, ev, login, logout, capture, test, resetSession, viewport }=b;
 const q=JSON.stringify;
 const dialog='#workspace-dialog ';
 const closeOverlay=async()=>{await click(dialog+'[data-dialog-close]');await closed();};
 let sourcingTenderChecked=false, sourcingComplianceChecked=false, sourcingEvaluationChecked=false;
 let earlyClosureChecked=false;
 let documentClarificationChecked=false;
 let managerOverlayChecked=false, topOverlayChecked=false, departmentOverlayChecked=false;
 let focusedBidChecked=false;
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
   // The whole switch (reset, sign-in, navigation, identity check) is retried, so a stale session from the previous
   // user can never leak into the next step; the identity shown by the server is the final authority.
   for(let attempt=0;;attempt++){
     try {
       await resetSession();
       await login(role);
       await wait('!document.querySelector("#password-sign-in") && document.readyState==="complete"',role+' signed in');
       if(path)await go(path);
       await has('form[action*=Logout]');
       await wait('document.querySelector(".bar-profile small")?.textContent.toLowerCase().trim()==='+q(role.toLowerCase()+'@example.test'),role+' identity');
       return;
     }
     catch(error){ if(attempt===2) throw error; }
   }
 }
 async function fields(obj,root=''){for(const [name,value] of Object.entries(obj)) await set(root+'[name="'+name+'"]',String(value));}
 const iso=(minutes)=>new Date(Date.now()+minutes*60000).toISOString().slice(0,16);
 const idFrom=async()=>Number(await ev('location.pathname.split("/").at(-1)'));
 async function attachment(selector){
   await ev(`(()=>{const input=document.querySelector(${q(selector)}); const file=new File(['%PDF-1.4\\nSynthetic browser evidence\\n%%EOF'], 'evidence.pdf', {type:'application/pdf'}); const data=new DataTransfer();data.items.add(file);input.files=data.files;input.dispatchEvent(new Event('change',{bubbles:true}));})()`);
 }
 async function request(title){
   await as('DepartmentSmoke','/PurchaseRequisitions/MyRequests');
   await click('#main a[href*="/PurchaseRequisitions/Create"]'); await has('#main input[name=Title]');
   await fields({CategoryId:1,Title:title,Reason:'Synthetic purchase',TargetDate:iso(14400).slice(0,10)},'#main ');
   await submit('#main form');await words(title);await closed();
   for(const name of ['Monitor','Cable']){
     await modal('#main a[href*="/RequisitionItems/Create"]','input[name=ItemName]');
     await fields({ItemName:name,Quantity:2,Unit:'each',Specifications:'Synthetic specification'},dialog);
     await submit(dialog+'form');
     await wait('document.querySelector("#workspace-dialog[open]")?.innerText.includes('+q(name)+')','item saved in modal');
     await closeOverlay();await words(name);
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
   const tender=await ev(`Array.from(document.querySelectorAll('a[href*="/Tenders/Details/"]')).find(a=>a.closest('.record-card')?.innerText.includes(${q(title)}))?.href`);
   assert(tender,'Tender details link missing');
   await go(new URL(tender).pathname);return await idFrom();
 }
 async function configure(id){
   const twoCriteria=!sourcingTenderChecked;
   if(!sourcingTenderChecked){
     await go('/Tenders');
     const origin=await ev('location.href');
     await has('#main .dept-filter-dock.is-in-view');
     await has('#main .dept-directory-heading [data-result-count]');
     await modal('#main a[href="/Tenders/Details/'+id+'?panel=overview"]','.admin-record-hero');
     assert.equal(await ev('!!document.querySelector("#workspace-dialog .record-qr")'),false);
     await capture('sourcing-tender-modal');
     await click(dialog+'a[href*="/Tenders/Edit/"]');
     await has(dialog+'input[name=Title]');
     await capture('sourcing-nested-edit-modal');
     await click(dialog+'[data-dialog-back]');
     await has(dialog+'a[href*="/Tenders/Criteria/"]');
     await click(dialog+'a[href*="/Tenders/Criteria/"]');
     await has(dialog+'input[name=Name]');
     await click(dialog+'[data-dialog-back]');
     await has(dialog+'form[action*="/Publish/"]');
     assert.equal(await ev('!!document.querySelector('+q('#workspace-dialog a[href*="/Invitations/Create"]')+')'),false);
     assert.equal(await ev('location.href'),origin);
     await closeOverlay();
     await modal('#main .tender-card-quick-actions a[href="/Tenders/Criteria/'+id+'"]','[data-add-criterion]');
     assert.equal(await ev('location.href'),origin);
     await closeOverlay();
     sourcingTenderChecked=true;
     await go('/Tenders/Details/'+id);
   }
   await modal('a[href*="/Tenders/Criteria/"]','input[name=Name]');
   await fields({Name:'Quality',Type:'Technical',MaxScore:100,WeightPercent:twoCriteria?60:100},dialog);
   await submit(dialog+'form');await words('Quality');await closed();
   if(twoCriteria){
     await fields({Name:'Cost',Type:'Financial',MaxScore:100,WeightPercent:40},'#main [data-add-criterion] ');
     await submit('#main [data-add-criterion]');await words('Cost');await closed();
   }
   await go('/Tenders/Details/'+id);
   await submit('form[action*="/Publish/"]');await wait('document.querySelector("#main .badge")?.textContent.trim()==="Open"','published Open');
   await modal('#main a[href*="/Invitations/Create"]','input[name=selectedSuppliers]');
   assert.equal(await ev('document.querySelector("#workspace-dialog input[type=hidden][name=TenderId]").value'),String(id));
   await ev('document.querySelectorAll("#workspace-dialog input[name=selectedSuppliers]").forEach(i=>i.checked=true)');
   await fields({ExpiresAt:iso(90)},dialog);await click(dialog+'[data-send-invitations]');await closed();
   await modal('#main a[href*="/Invitations/Details/"]','.admin-record-hero');
   await closeOverlay();
   await go('/Tenders/Details/'+id);
 }
 async function bid(role,id,price){
   await as(role,'/SupplierWorkspace/Invitations');
   await has('#main .dept-filter-dock.is-in-view');
   await has('#main .dept-directory-heading [data-result-count]');
   const select='article';
   const article=await ev('Array.from(document.querySelectorAll("article")).findIndex(a=>a.innerText.includes('+q('TENDER #'+id+' ·')+'))');
   assert(article>=0,'Invitation missing');
   const row=select+':nth-of-type('+(article+1)+')';
   const origin=await ev('location.href');
   await modal('#main .record-list '+row+' a[data-workspace-dialog]','button[value=Accepted]');
   await capture('supplier-invitation-modal');
   await click(dialog+'button[value=Accepted]');
   await has(dialog+'a[href*="/Bids/Create"]');
   await click(dialog+'a[href*="/Bids/Create"]');
   await has(dialog+'[name=Currency]');
   await fields({Currency:'JOD',DeliveryDays:3,ValidityDays:7},dialog);
   await submit(dialog+'form');await has(dialog+'a[href*="/BidItems/Create"]');
   assert.equal(await ev('location.href'),origin);
   const bidId=Number(await ev('document.querySelector('+q('#workspace-dialog a[href*="/Bids/Edit/"]')+').pathname.split("/").at(-1)'));
   await capture('supplier-bid-modal');
   let bidOrigin=origin;
   const focused=!focusedBidChecked;
   const openBidPanel = (panel, field) => modal('#main a[href="/Bids/Details/'+bidId+'?panel='+panel+'"]',field);
   if(focused){
     await closeOverlay();await go('/Bids/MyBids');
     bidOrigin=await ev('location.href');
     await openBidPanel('prices','a[href*="/BidItems/Create"]');
     assert.equal(await ev('!!document.querySelector('+q('#workspace-dialog a[href*="/BidDocuments/Upload"]')+')'),false);
   }
   for(let n=0;n<2;n++){
     await click(dialog+'a[href*="/BidItems/Create"]');await has(dialog+'input[name=UnitPrice]');
     await fields({UnitPrice:price},dialog);await submit(dialog+'form');await has(dialog+'a[href*="/BidItems/Edit"]');
   }
   if(focused){
     await has(dialog+'h1');
     assert.equal(await ev('document.querySelector("#workspace-dialog-title").textContent'),'Item prices');
     await capture('supplier-focused-prices');
     await closeOverlay();
     await openBidPanel('documents','a[href*="/BidDocuments/Upload"]');
   }
   await click(dialog+'a[href*="/BidDocuments/Upload"]');await has(dialog+'input[type=file]');
   await capture('supplier-document-modal');
   await ev('const opt=document.querySelector("#workspace-dialog select[name=DocumentType]");opt.selectedIndex=1;');
   await attachment(dialog+'input[type=file]');await submit(dialog+'form');
   if(focused){
     await has(dialog+'a[href*="/BidDocuments/Download/"]');
     assert.equal(await ev('document.querySelector("#workspace-dialog-title").textContent'),'Bid documents');
     assert.equal(await ev('location.href'),bidOrigin);
     await capture('supplier-focused-documents');
     await closeOverlay();
     await openBidPanel('submission','form[action*="/Bids/Submit/"]');
     assert.equal(await ev('!!document.querySelector('+q('#workspace-dialog a[href*="/BidDocuments/Download/"]')+')'),false);
     await closeOverlay();focusedBidChecked=true;
   }else{
     await has(dialog+'form[action*="/Bids/Submit/"]');
     assert.equal(await ev('location.href'),origin);
   }
   await go('/Bids/Details/'+bidId);
   const submitBid='form[action*="/Bids/Submit/"] button[type=submit]';
   await has(submitBid);
   assert((await ev('document.querySelector('+q(submitBid)+').form.action')).endsWith('/'+bidId));
   await act(submitBid);
   await wait('!document.querySelector('+q(submitBid)+') && Array.from(document.querySelectorAll("#main .badge,#main .status-pill")).some(x=>x.textContent.trim()==="Submitted")','bid '+bidId+' submitted');
   return bidId;
 }
 async function review(id){
   if(!earlyClosureChecked){
     await as('SourcingOfficer','/Tenders');
     const origin=await ev('location.href');
     const card='#main .record-card:has(a[href="/Tenders/Details/'+id+'?panel=overview"])';
     assert.equal(await ev('!!document.querySelector('+q(card+' .tender-card-quick-actions a[href*="/Tenders/Criteria/"]')+')'),false);
     await modal('#main a[href="/Tenders/Details/'+id+'?panel=overview"]','form[action*="/Tenders/Close/"]');
     await capture('tender-overview-actions');
     await viewport(390);
     try {
       assert(await ev('document.querySelector("#workspace-dialog").scrollWidth <= document.querySelector("#workspace-dialog").clientWidth + 1'));
       await ev('document.querySelector("#workspace-dialog .tender-actions").scrollIntoView({block:"end"})');
       await capture('tender-mobile-actions');
     } finally { await viewport(1440); }
     await closeOverlay();
     const closeButton=card+' .tender-card-quick-actions form[action*="/Tenders/Close/"] button';
     await has(closeButton);
     await click(closeButton);
     await wait('document.querySelector("#workspace-confirm").open','early close confirmation');
     await click('#workspace-confirm button[value=cancel]');
     await wait('!document.querySelector("#workspace-confirm").open','cancel keeps bidding open');
     await has(closeButton);
     await act(closeButton);
     await has(card+'[data-status="Closed"]');
     assert.equal(await ev('!!document.querySelector('+q(closeButton)+')'),false);
     assert.equal(await ev('location.href'),origin);
     await capture('tender-card-closed-early');
     await go('/Compliance/Review?tenderId='+id);
     earlyClosureChecked=true;
     return;
   }
   await as('SourcingOfficer','/Compliance/Review?tenderId='+id);
   await ev(`document.querySelector('form[action*="/Compliance/Close"]').closest('details').open=true`);
   await click('input[name=confirmClose]');await submit('form[action*="/Compliance/Close"]');
   await words('Closed');
 }
 async function evaluate(id,bids){
   if(!sourcingComplianceChecked && bids.length){
     await go('/Compliance/Pending');
     const origin=await ev('location.href');
     await modal('#main a[href*="tenderId='+id+'"]','.admin-record-hero');
     await click(dialog+'a[href*="/MarkNonCompliant/"]');
     await has(dialog+'textarea[name=Reason]');
     await capture('sourcing-nested-compliance-modal');
     await click(dialog+'[data-dialog-back]');
     await has(dialog+'a[href*="/MarkNonCompliant/"]');
     assert.equal(await ev('location.href'),origin);
     await closeOverlay();
     await go('/Tenders');
     await modal('#main a[href="/Tenders/AssignCommittee/'+id+'"]','form[action*="/AssignCommittee/"]');
     await closeOverlay();
     await go('/Compliance/Review?tenderId='+id);
     sourcingComplianceChecked=true;
   }
   if(!documentClarificationChecked && bids.length===3){
     const bidId=bids[0];
     const link='#main a[href*="/Clarifications/Create"][href*="recordId='+bidId+'"]';
     await modal(link,'select[name=RequestedDocumentType]');
     const recipient=await ev('Array.from(document.querySelectorAll("#workspace-dialog select[name=Recipient] option")).find(o=>o.value).value');
     await fields({Recipient:recipient,Subject:'Additional insurance PDF',Description:'Attach the current insurance certificate.',RequestedDocumentType:'Certificate'},dialog);
     await submit(dialog+'form');
     await wait('document.querySelector("#workspace-dialog[open]")?.innerText.includes("Requested PDF: Certificate")','request saved in modal');
     const threadId=await ev('Number(document.querySelector("#workspace-dialog .admin-record-hero .eyebrow").textContent.match(/\\d+/)[0])');
     await as('Supplier','/Clarifications/Details/'+threadId);
     await has('#main input[name=attachment]');
     await attachment('#main input[name=attachment]');
     await submit('#main form[action*="/Answer/"]');
     await words('Awaiting approval');
     await capture('supplier-bid-clarification-reply');
     await as('SourcingOfficer','/Compliance/Review?tenderId='+id);
     await modal('#main a[href="/Clarifications/Details/'+threadId+'"]','form[action*="/AddAttachment/"]');
     await act(dialog+'form[action*="/AddAttachment/"] button');
     await wait('document.querySelector("#workspace-dialog[open]")?.innerText.includes("Document approved and added to the bid.")','officer approved attachment');
     await capture('officer-approved-bid-attachment');
     await closeOverlay();
     await go('/Compliance/Review?tenderId='+id);
     documentClarificationChecked=true;
   }
   for(const bidId of bids){await act('form[action*="/MarkCompliant/'+bidId+'"] button');await wait('!document.querySelector('+q('form[action*="/MarkCompliant/'+bidId+'"]')+')','compliance saved');}
   await go('/Tenders/AssignCommittee/'+id);await submit('form[action*="/AssignCommittee/"]');
   for(const role of ['DepartmentSmoke','TechnicalCommittee','FinancialCommittee','SourcingManager']){
     if(role==='DepartmentSmoke' && !departmentOverlayChecked){
       await as(role,'/PurchaseRequisitions/Committee');
       const origin=await ev('location.href');
       await modal('#main a[href="/CommitteeReviews/Details/'+id+'"]','.admin-record-hero');
       assert.equal(await ev('location.href'),origin);
       assert.equal(await ev('document.querySelector("[data-workspace-frame]").dataset.workspaceFrame'),'department');
       if(bids.length===3){
         await click(dialog+'a[href="/CommitteeReviews/Weights/'+id+'"]');
         await has(dialog+'[data-suggest-weights]:not([hidden])');
         await click(dialog+'[data-suggest-weights]');
         await wait('document.querySelector("#workspace-dialog [data-weight-status]")?.innerText.includes("Suggestion ready")','AI response shown as advice');
         assert.deepEqual(await ev('Array.from(document.querySelectorAll("#workspace-dialog [data-weight-value]"), input => Number(input.value)).sort()'),[35,65]);
         await ev('Array.from(document.querySelectorAll("#workspace-dialog .weight-plan-row")).find(r=>Number(r.dataset.proposed)===40).querySelector("input[type=checkbox]").click()');
         assert(await ev('document.querySelector("#workspace-dialog form[data-weight-plan] button[type=submit]").disabled'));
         await ev('const input=Array.from(document.querySelectorAll("#workspace-dialog .weight-plan-row")).find(r=>Number(r.dataset.proposed)===60).querySelector("[data-weight-value]");input.value="100";input.dispatchEvent(new Event("input",{bubbles:true}));');
         await submit(dialog+'form[data-weight-plan]');
         await wait('document.querySelector("#workspace-dialog")?.innerText.includes("Your criteria weights were saved.")','weights saved inside modal');
         await capture('reviewer-evaluation-weights');
         assert.equal(await ev('location.href'),origin);
       }
       await closeOverlay();
       departmentOverlayChecked=true;
     }
     if(role==='SourcingManager' && !managerOverlayChecked){
       await as(role,'/CommitteeReviews/Assignments');
       const origin=await ev('location.href');
       await has('#main .dept-filter-dock');
       await click('#main .dept-dock-button[aria-label="Search"]');
       await set('#main input[data-filter-search]','definitely-not-an-assignment');
       await ev('document.querySelector("#main input[data-filter-search]").dispatchEvent(new Event("input",{bubbles:true}))');
       await wait('document.querySelector("#main [data-result-count]").textContent==="0 records"','manager search');
       await click('#main [data-filter-reset]');
       await modal('#main a[href="/CommitteeReviews/Details/'+id+'"]','a[href="/CommitteeReviews/Bid/'+bids[0]+'"]');
       await click(dialog+'a[href="/CommitteeReviews/Bid/'+bids[0]+'"]');
       await has(dialog+'[data-checklist-choice]');
       await click(dialog+'.checklist-group [data-checklist-choice]');
       assert.match(await ev('document.querySelector("#workspace-dialog [data-checklist-total]").textContent'),/^[1-9]\d*% \/ 100%$/);
       await ev('document.querySelectorAll("#workspace-dialog [data-checklist-choice]:not(:checked)").forEach(input=>input.click())');
       assert.equal(await ev('document.querySelector("#workspace-dialog [data-checklist-total]").textContent'),'100% / 100%');
       await submit(dialog+'form[data-bid-checklist]');
       await wait('document.querySelector("#workspace-dialog[open]")?.innerText.includes("Your evaluation checklist was saved.")','checklist saved inside modal');
       await capture('manager-bid-checklist');
       await viewport(390);
       try {
         assert(await ev('document.querySelector("#workspace-dialog").scrollWidth <= document.querySelector("#workspace-dialog").clientWidth + 1'));
         await ev('document.querySelector("#workspace-dialog .bid-checklist").scrollIntoView({block:"start"})');
         await capture('manager-mobile-checklist');
       } finally { await viewport(1440); }
       await click(dialog+'[data-dialog-back]');
       await has(dialog+'a[href="/CommitteeReviews/Bid/'+bids[0]+'"]');
       await click(dialog+'a[href="/EvaluationWorkflow/Details/'+id+'"]');
       await has(dialog+'[data-dialog-back]:not([hidden])');
       await wait('document.querySelector("#workspace-dialog[open]")?.innerText.includes("Committee progress")','manager nested evaluation report');
       assert.equal(await ev('location.href'),origin);
       await closeOverlay();
       managerOverlayChecked=true;
     }
     await as(role,'/CommitteeReviews/Details/'+id);
     for(const [i,bidId] of bids.entries()){
       await go('/CommitteeReviews/Bid/'+bidId);await has('[data-checklist-choice]');
       if(i===0) await ev('document.querySelectorAll("#main [data-checklist-choice]:not(:checked)").forEach(input=>input.click())');
       await submit('#main form[data-bid-checklist]');await words('Your evaluation checklist was saved.');
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
   if(role.startsWith('Top') && !topOverlayChecked){
     await go('/EvaluationWorkflow');
     const origin=await ev('location.href');
     await has('#main .dept-filter-dock');
     await modal('#main a[href="/EvaluationWorkflow/Details/'+id+'?panel=decision"]','a[href*="/EvaluationWorkflow/Reject/"]');
     assert.equal(await ev('document.querySelector("#workspace-dialog").innerText.includes("Committee progress")'),false);
     await click(dialog+'a[href*="/EvaluationWorkflow/Reject/"]');
     await has(dialog+'textarea[name=comments]');
     await capture('top-committee-nested-rejection');
     await viewport(390);
     try {
       assert(await ev('document.querySelector("#workspace-dialog").scrollWidth <= document.querySelector("#workspace-dialog").clientWidth + 1'),'top decision dialog fits mobile');
       await capture('top-committee-mobile-rejection');
     } finally { await viewport(1440); }
     await click(dialog+'[data-dialog-cancel]');
     await has(dialog+'a[href*="/EvaluationWorkflow/Reject/"]');
     assert.equal(await ev('location.href'),origin);
     await closeOverlay();
     await go('/EvaluationWorkflow/Details/'+id);
     topOverlayChecked=true;
   }
   if(role==='SourcingOfficer' && !sourcingEvaluationChecked){
     await go('/EvaluationWorkflow');
     await modal('#main a[href="/EvaluationWorkflow/Details/'+id+'?panel=progress"]','.admin-record-hero');
     await closeOverlay();
     await go('/EvaluationWorkflow/Details/'+id);
     sourcingEvaluationChecked=true;
   }
   if(kind==='reject'){
      await modal('a[href*="/EvaluationWorkflow/Reject/"]','textarea[name=comments]');
      await fields({comments:'Documented synthetic rejection',password:'Synthetic-Browser!2046'},dialog);
      await click(dialog+'input[name=confirmDecision]');await submit(dialog+'form');
      await wait('document.querySelector("#workspace-dialog[open]")?.innerText.includes('+q(status)+')','rejection stays in modal');
      await closeOverlay();
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
     await click(dialog+'[data-send-invitations]');await closed();
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
