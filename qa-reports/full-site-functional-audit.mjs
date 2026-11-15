import assert from 'node:assert/strict';

export async function additionalFunctionalAudit({go,wait,click,set,ev,login,logout,capture,call,test}) {
  const q=JSON.stringify, dialog='#workspace-dialog[open] ';
  const words=text=>wait('document.querySelector("main")?.innerText.includes('+q(text)+')',text);
  const has=selector=>wait('!!document.querySelector('+q(selector)+')',selector);
  const show=(selector,text)=>wait('document.querySelector('+q(selector)+')?.innerText.includes('+q(text)+')',text);
  const key=async(name,code,character='')=>{
    await call('Input.dispatchKeyEvent',{type:character?'keyDown':'rawKeyDown',key:name,code:name,
      windowsVirtualKeyCode:code,nativeVirtualKeyCode:code,...(character?{text:character,unmodifiedText:character}:{})});
    await call('Input.dispatchKeyEvent',{type:'keyUp',key:name,code:name,windowsVirtualKeyCode:code,nativeVirtualKeyCode:code});
  };
  async function as(role,route){
    if(await ev('!!document.querySelector("form[action*=Logout]")'))await logout();
    await login(role);
    await wait('document.readyState==="complete" && document.querySelector(".bar-profile small")?.textContent.trim().toLowerCase()==='+q(role.toLowerCase()+'@example.test'),role+' login');
    await go(route);
  }
  async function close(){
    await click(dialog+'[data-dialog-close]');
    await wait('!document.querySelector("#workspace-dialog").open','modal closed');
  }
  let requestId;
  await test('QA-purchase-request-return-resubmit-reject',async()=>{
    await as('RequestingDepartment','/PurchaseRequisitions/MyRequests');
    const href=await ev(`[...document.querySelectorAll('.record-card a[href*="/PurchaseRequisitions/Details/"][href$="panel=overview"]')]
      .find(a=>a.closest('.record-card').innerText.includes('Phase 4 browser draft'))?.pathname`);
    assert(href,'Submitted browser request is available');
    requestId=href.split('/').at(-1);
    await as('SourcingOfficer','/RequisitionReviews/Details/'+requestId);
    await set('textarea[name=Notes]','Please update the requested quantities.');
    await click('button[formaction*="/Return/"]');
    await wait('location.pathname.endsWith("/Pending")','request returned');
    await as('RequestingDepartment',href);
    await words('Changes requested');await words('Please update the requested quantities.');
    await click('a[href*="/PurchaseRequisitions/Edit/"]');await has(dialog+'input[name=Title]');
    await set(dialog+'input[name=Title]','QA corrected purchase request');
    await click(dialog+'form button[type=submit]');await show(dialog+'#workspace-dialog-title','QA corrected purchase request');
    await close();await words('QA corrected purchase request');
    await has('form[action*="/Submit/"]');
    await click('form[action*="/Submit/"] button');await words('Under Review');
    await as('SourcingOfficer','/RequisitionReviews/Details/'+requestId);
    await set('textarea[name=Notes]','Synthetic final rejection.');
    await click('button[formaction*="/Reject/"]');
    await wait('location.pathname.endsWith("/Pending")','request rejected');
    await as('RequestingDepartment',href);
    await words('Rejected');await words('Synthetic final rejection.');
    assert.equal(await ev('!!document.querySelector(\'a[href*="/PurchaseRequisitions/Edit/"]\')'),false);
    await capture('qa-request-rejected');
  });
  await test('QA-notification-read-opens-linked-record',async()=>{
    assert(requestId,'A reviewed request was located');
    await as('RequestingDepartment','/Notifications');
    const selector='.record-card:has(button[aria-label*="Purchase request Rejected"])';
    await show(selector,'Unread');
    await click(selector+' button');
    await show(dialog+'[data-dialog-content]','Synthetic final rejection.');
    assert.equal(await ev('location.pathname'),'/Notifications');
    await close();
    await show(selector,'Read');
  });
  await test('QA-department-create-switch-and-details',async()=>{
    await as('Admin','/Departments');
    await click('#main a[href="/Departments/Create"]');await has(dialog+'input[name=IsActive]');
    const selector=dialog+'input[name=IsActive]';
    if(await ev('document.querySelector('+q(selector)+').checked'))await click(selector);
    await set(dialog+'input[name=Name]','QA design operations');
    await set(dialog+'textarea[name=Description]','Disposable audit entry');
    await click(dialog+'form button[type=submit]');await show(dialog+'#workspace-dialog-title','QA design operations');
    await close();await words('QA design operations');
    const details='.record-card a[aria-label*="QA design operations"]';
    await show('.record-card:has(a[aria-label*="QA design operations"])','Inactive');
    await click(details);await show(dialog+'[data-dialog-content]','Disposable audit entry');
    await click(dialog+'a[href*="/Departments/Edit/"]');await has(selector);
    assert.equal(await ev('document.querySelector('+q(selector)+').checked'),false);
    await click(selector);
    await click(dialog+'form button[type=submit]');
    await wait('!!document.querySelector("#workspace-dialog[open] .status-pill") && document.querySelector("#workspace-dialog[open] .status-pill").innerText.trim()==="Active"','active department details');
    await close();
    await wait('document.querySelector(\'.record-card:has(a[aria-label*="QA design operations"])\')?.dataset.status==="active"','active department list');
  });
  await test('QA-employee-search-no-results-and-reset',async()=>{
    await as('Admin','/Employees');
    const search='[data-dept-search-dialog]';
    await click('[data-dept-search-open]');await has(search+'[open]');
    const enter=async value=>ev(`(()=>{const e=document.querySelector(${q(search+' input[data-filter-search]')});e.value=${q(value)};e.dispatchEvent(new Event('input',{bubbles:true}));})()`);
    await enter('no-matching-employee-qa');await has('[data-filter-empty]:not([hidden])');
    await enter('TechnicalCommittee');
    await wait('document.querySelectorAll(".record-card:not([hidden])").length===1','one matching employee');
    await click('[data-dept-search-close]');
    await click('[data-filter-reset]');
    await wait('document.querySelectorAll(".record-card:not([hidden])").length>1','all employees restored');
  });
  await test('QA-header-account-click-and-mobile-search',async()=>{
    await as('Admin','/AdminDashboard');
    await click('[data-header-menu-toggle="profile"]');await has('#stms-profile-menu:not([hidden])');
    await click('[data-header-menu-toggle="profile"]');await has('#stms-profile-menu[hidden]');
    await call('Emulation.setDeviceMetricsOverride',{width:390,height:740,deviceScaleFactor:1,mobile:false});
    try{
      await click('.stms-search-trigger');await has('#stms-search-dialog[open]');
      await ev(`(()=>{const e=document.querySelector('#stms-workspace-search');e.value='employees';e.dispatchEvent(new Event('input',{bubbles:true}));})()`);
      await has('#stms-search-results a[href*="/Employees"]');
      await click('#stms-search-results a[href*="/Employees"]');await words('Employees');
      await wait('location.pathname==="/Employees" && !document.querySelector("#workspace-dialog").open','search uses page mode');
    }finally{
      await call('Emulation.setDeviceMetricsOverride',{width:1440,height:900,deviceScaleFactor:1,mobile:false});
    }
  });
  await test('QA-profile-photo-upload-and-remove',async()=>{
    await as('Admin','/Profile');
    await ev(`(() => {
      const d=new DataTransfer();
      const bytes=Uint8Array.from(atob('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aDioAAAAASUVORK5CYII='),c=>c.charCodeAt(0));
      d.items.add(new File([bytes],'synthetic.png',{type:'image/png'}));
      const e=document.querySelector('#photo');e.files=d.files;e.dispatchEvent(new Event('change',{bubbles:true}));
    })()`);
    await has('[data-photo-preview]:not([hidden])');
    await click('form[action*="/Upload"] [data-photo-save]');
    await has('form[action*="/Remove"] button');
    await click('form[action*="/Remove"] button');
    await wait('!document.querySelector(\'form[action*="/Remove"]\') && !!document.querySelector("#photo")','photo removed');
  });
  await test('QA-filter-enter-and-escape',async()=>{
    await as('Admin','/Employees');
    const control='.dept-filter-dock.is-in-view .dept-dock-button[aria-haspopup]';
    await ev('document.querySelector(".dept-directory-heading").scrollIntoView({block:"center",behavior:"instant"})');
    await has(control);
    await call('Input.dispatchMouseEvent',{type:'mouseMoved',x:0,y:0});
    await ev('document.querySelector('+q(control)+').focus()');
    await key('Enter',13,'\r');
    await has(control+'[aria-expanded=true]');
    await key('Escape',27);
    await has(control+'[aria-expanded=false]');
  });
  await test('QA-modal-focus-return',async()=>{
    await as('Admin','/Employees');
    const trigger='#main a[href="/Employees/Create"]';
    await ev('document.querySelector('+q(trigger)+').focus()');
    await key('Enter',13,'\r');
    await has(dialog+'input[name=FullName]');
    await key('Escape',27);
    await wait('!document.querySelector("#workspace-dialog").open && document.activeElement.matches('+q(trigger)+')','focus returned to modal trigger');
  });
  await test('QA-admin-chart-period-and-keyboard',async()=>{
    await as('Admin','/AdminDashboard');
    const charts='[data-procurement-chart]';
    await has(charts+' [data-month]');
    assert(await ev('[...document.querySelectorAll(".admin-metric-link strong[data-count]")].every(e=>Number(e.textContent)===Number(e.dataset.count))'));
    await click(charts+' a[href*="months=12"]');
    await wait('document.querySelectorAll("[data-month]").length === 12','12 months rendered from SQL');
    const point=charts+' [data-month]:first-child';
    await ev('document.querySelector('+q(point)+').focus()');
    await key('Enter',13,'\r');
    await has(point+'[aria-pressed=true]');
    assert(await ev('document.querySelector("[data-chart-selection]").innerText.includes(document.querySelector("[data-month]").dataset.month)'));
    await call('Emulation.setDeviceMetricsOverride',{width:320,height:740,deviceScaleFactor:1,mobile:false});
    try{ assert(await ev('document.documentElement.scrollWidth <= innerWidth')); }
    finally{ await call('Emulation.setDeviceMetricsOverride',{width:1440,height:900,deviceScaleFactor:1,mobile:false}); }
    await capture('qa-dashboard-months');
  });
  await test('QA-admin-activity-event-and-date-filters',async()=>{
    await as('Admin','/AdminOperations/Activity');
    const event=await ev('Array.from(document.querySelectorAll("[data-activity-event] option")).find(o=>o.value)?.value');
    assert(event,'activity event options are populated');
    const today=new Date().toISOString().slice(0,10);
    await go('/AdminOperations/Activity?'+new URLSearchParams({eventType:event,from:today,to:today}));
    await has('.record-card');
    assert(await ev(`[...document.querySelectorAll(".record-card .record-name > span:last-child")].every(e=>e.firstChild.textContent.trim()===${q(event)})`));
    await go('/AdminOperations/Activity?from=2030-01-02&to=2030-01-01');
    await words('The end date must be on or after the start date.');
    assert.equal(await ev('document.querySelectorAll(".record-card").length'),0);
  });
  await test('QA-sign-in-mobile-primary-action-above-fold',async()=>{
    await logout();
    await go('/Account/Login');
    for(const width of [320,360,390]){
      await call('Emulation.setDeviceMetricsOverride',{width,height:740,deviceScaleFactor:1,mobile:false});
      assert(await ev('document.querySelector("#password-sign-in button[type=submit]").getBoundingClientRect().bottom<=innerHeight && document.documentElement.scrollWidth<=innerWidth'),'mobile sign-in at '+width);
    }
    await call('Emulation.setDeviceMetricsOverride',{width:1440,height:900,deviceScaleFactor:1,mobile:false});
  });
}
