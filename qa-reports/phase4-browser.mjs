import {lifecycle} from './phase6-lifecycle.mjs';
import assert from 'node:assert/strict';
import {spawn} from 'node:child_process';
import {mkdtemp, mkdir, readFile, rm, writeFile} from 'node:fs/promises';
import {tmpdir} from 'node:os';
import {join} from 'node:path';

const [, , base, exe, evidence] = process.argv;
const profile = await mkdtemp(join(tmpdir(), 'STMS-Chrome-'));
const chrome = spawn(exe, ['--headless=new','--disable-gpu','--no-first-run','--disable-background-networking',
  '--remote-debugging-port=0','--user-data-dir='+profile,'about:blank'], {stdio:'ignore',windowsHide:true});
const sleep = ms => new Promise(r=>setTimeout(r,ms));
async function until(fn, description, seconds=12) {
  let last;
  for (const end=Date.now()+seconds*1000; Date.now()<end;) {
    try { if (last=await fn()) return last; } catch(e) { last=e; }
    await sleep(100);
  }
  throw new Error('Timed out: '+description+' '+(last || ''));
}
let ws;
const errors=[], results=[], http=[];
try {
  const port=await until(async()=> (await readFile(join(profile,'DevToolsActivePort'),'utf8')).split(/\r?\n/)[0],'Chrome DevTools port');
  const [tab]=await (await fetch('http://127.0.0.1:'+port+'/json/list')).json();
  ws=new WebSocket(tab.webSocketDebuggerUrl);
  await new Promise((resolve,reject)=>{ws.onopen=resolve;ws.onerror=reject;});
  let id=0;
  const pending=new Map();
  ws.onmessage=({data})=>{
    const msg=JSON.parse(data);
    if(msg.id){const waiter=pending.get(msg.id);pending.delete(msg.id);if(waiter)msg.error ? waiter.reject(new Error(msg.error.message)) : waiter.resolve(msg.result);}
    if(msg.method==='Network.responseReceived' && msg.params.response.url.startsWith(new URL(base).origin)) { const u=new URL(msg.params.response.url); if(!/\.(js|css|svg|woff2?|png|ico)$/.test(u.pathname)) http.push({path:u.pathname,status:msg.params.response.status}); }
    if(msg.method==='Runtime.exceptionThrown') errors.push(msg.params.exceptionDetails.exception?.description ?? msg.params.exceptionDetails.text);
  };
  const call=(method,params={})=>new Promise((resolve,reject)=>{pending.set(++id,{resolve,reject});ws.send(JSON.stringify({id,method,params}));});
  const ev=async expression=>{
    const r=await call('Runtime.evaluate',{expression,awaitPromise:true,returnByValue:true,userGesture:true});
    if(r.exceptionDetails) throw Error(r.exceptionDetails.exception?.description ?? r.exceptionDetails.text);
    return r.result.value;
  };
  const go=async path=>{
    const url=new URL(path,base).href;
    const before=await ev('performance.timeOrigin');
    await call('Page.navigate',{url});
    await until(()=>ev('performance.timeOrigin !== '+before+' && document.readyState === "complete" && location.origin === '+JSON.stringify(new URL(base).origin)),path);
  };
  const wait=(expression,label)=>until(()=>ev(expression),label);
  const click=s=>ev('document.querySelector('+JSON.stringify(s)+').click()');
  const set=(s,value)=>ev('document.querySelector('+JSON.stringify(s)+').value='+JSON.stringify(value));
  const capture=async name=>writeFile(join(evidence,name+'.png'),Buffer.from((await call('Page.captureScreenshot',{format:'png'})).data,'base64'));
  async function test(id, fn) {
    try { await fn(); results.push({id,status:'PASS'}); }
    catch(error) { results.push({id,status:'FAIL',reason:error.message, page:await ev('location.pathname').catch(()=>null), statusText:await ev('document.querySelector("[data-bio-status]")?.textContent').catch(()=>null)}); await capture(id+'-failure').catch(()=>{}); }
  }
  await mkdir(evidence,{recursive:true});
  await call('Network.enable'); await call('Network.setCacheDisabled',{cacheDisabled:true});
  await call('Page.enable');await call('Runtime.enable');
  await call('WebAuthn.enable');
  await call('WebAuthn.addVirtualAuthenticator',{options:{protocol:'ctap2',transport:'internal',hasResidentKey:true,hasUserVerification:true,hasPrf:true,isUserVerified:true,automaticPresenceSimulation:true}});
  await call('Emulation.setDeviceMetricsOverride',{width:1440,height:900,deviceScaleFactor:1,mobile:false});
  const password='Synthetic-Browser!2046';
  async function login(role, secret=password) {
    if(!await ev('location.pathname==="/Account/Login" && document.readyState==="complete"')) await go('/Account/Login');
    await wait('!!document.querySelector("#password-sign-in")','login form');
    await ev('document.querySelector("#password-sign-in").closest("details").open=true');
    await set('#Email',role+'@example.test'); await set('#Password',secret);
    await click('#password-sign-in button[type=submit]');
  }
  async function logout(){
    await wait('document.readyState==="complete" && !!document.querySelector("form[action*=Logout] button")','sign out control');
    await click('form[action*=Logout] button');
    await wait('location.pathname==="/Account/Login" && document.readyState==="complete" && !!document.querySelector("#password-sign-in")','logout');
  }
  await test('BR01-invalid-password',async()=>{
    await login('RequestingDepartment','wrong-password');
    await wait('document.body.innerText.includes("Unable to sign in")','invalid login feedback');
  });
  await test('BR02-real-passkey',async()=>{
    await login('RequestingDepartment');
    await wait('location.pathname.includes("BiometricVerify") && !!document.querySelector("[data-phone-button]")','passkey onboarding');
    await wait('document.readyState==="complete" && performance.getEntriesByType("resource").some(e=>e.name.includes("biometrics.js"))','passkey script');
    await sleep(250);
    await click('[data-phone-button]');
    await wait('location.pathname.includes("BiometricSetup") && document.body.innerText.includes("Continue to workspace")','registered passkey');
    await go('/PurchaseRequisitions/Dashboard');
    await wait('location.pathname.includes("Dashboard") && !!document.querySelector("[data-workspace-frame]")','department workspace');
    await logout();
    await wait('document.readyState==="complete" && performance.getEntriesByType("resource").some(e=>e.name.includes("biometrics.js"))','login passkey script');
    await sleep(250);
    await click('[data-phone-button]');
    await wait('location.pathname.includes("/PurchaseRequisitions/Dashboard")','discoverable passkey login');
  });
  await test('BR03-real-modal-draft',async()=>{
    if (!await ev('!!document.querySelector("form[action*=Logout]")')) {
      await login('DepartmentSmoke');
      await wait('location.pathname.includes("Dashboard") && !!document.querySelector("[data-workspace-frame]")','password-authenticated department');
    }
    await go('/PurchaseRequisitions/MyRequests');
    await wait('!!document.querySelector("a[data-workspace-dialog][href*=Create]")','request listing');
    await ev('window.phase4Marker=123');
    const trigger = 'a[data-workspace-dialog][href*=Create]';
    await ev('document.querySelector('+JSON.stringify(trigger)+').focus()');
    await click(trigger);
    await wait('document.querySelector("#workspace-dialog").open','open for Escape');
    await call('Input.dispatchKeyEvent',{type:'keyDown',key:'Escape',code:'Escape',windowsVirtualKeyCode:27});
    await call('Input.dispatchKeyEvent',{type:'keyUp',key:'Escape',code:'Escape',windowsVirtualKeyCode:27});
    await wait('!document.querySelector("#workspace-dialog").open && document.activeElement.matches('+JSON.stringify(trigger)+')','Escape restores focus');
    await click('a[data-workspace-dialog][href*=Create]');
    await wait('document.querySelector("#workspace-dialog").open && !!document.querySelector("#workspace-dialog input[name=Title]")','create dialog');
    await capture('department-draft-modal');
    await set('#workspace-dialog input[name=Title]','Phase 4 browser draft');
    await set('#workspace-dialog textarea[name=Reason]','Synthetic browser test');
    await set('#workspace-dialog input[name=TargetDate]',new Date(Date.now()+86400000*5).toISOString().slice(0,10));
    await click('#workspace-dialog button[type=submit]');
    await wait('document.body.innerText.includes("Phase 4 browser draft") && !document.querySelector("#workspace-dialog").open','draft saved');
    assert.equal(await ev('window.phase4Marker'),123);
  });
  await test('BR04-responsive-smoke',async()=>{
    await go('/PurchaseRequisitions/MyRequests');
    await wait('!!document.querySelector(".request-filters")','request listing');
    for (const width of [1440,1024,768,390,320]) {
      await call('Emulation.setDeviceMetricsOverride',{width,height:900,deviceScaleFactor:1,mobile:false});
      await sleep(80);
      assert(await ev('document.documentElement.scrollWidth <= window.innerWidth + 1'),'Horizontal overflow at '+width);
      if(width===390||width===1440)await capture('department-requests-'+width);
    }
    await call('Emulation.setDeviceMetricsOverride',{width:1440,height:900,deviceScaleFactor:1,mobile:false});
  });
  await test('BR05-voice-ajax-binding',async()=>{
    await go('/PurchaseRequisitions/Dashboard');
    await wait('location.pathname.endsWith("/Dashboard")','department dashboard');
    await ev('window.speechStarts=0; window.SpeechRecognition=class{start(){window.speechStarts++;}};');
    for (let i=0;i<2;i++){
      await click('a[href*="/PurchaseRequisitions/MyRequests"]');
      await wait('!!document.querySelector(".request-filters [data-request-voice-search]")','voice control');
      await click('[data-request-voice-search]');
      await click('#workspace-nav a.brand');
      await wait('location.pathname.endsWith("/Dashboard") && !document.querySelector(".request-filters")','AJAX dashboard');
    }
    assert.equal(await ev('window.speechStarts'),2);
  });
  await test('BR06-department-submit',async()=>{
    await go('/PurchaseRequisitions/MyRequests');
    await click('a[href*="/PurchaseRequisitions/Details/"]');
    await wait('!!document.querySelector("a[href*=RequisitionItems]")','request details');
    for(const name of ['Synthetic item A','Synthetic item B']){
      await click('a[data-workspace-dialog][href*="/RequisitionItems/Create"]');
      await wait('!!document.querySelector("#workspace-dialog input[name=ItemName]")','item modal');
      await set('#workspace-dialog input[name=ItemName]',name);
      await set('#workspace-dialog input[name=Quantity]','2');
      await set('#workspace-dialog input[name=Unit]','each');
      await set('#workspace-dialog textarea[name=Specifications]','Synthetic specifications');
      await click('#workspace-dialog button[type=submit]');
      await wait('document.body.innerText.includes('+JSON.stringify(name)+') && !document.querySelector("#workspace-dialog").open','saved item');
    }
    await click('form[action*="/Submit/"] button[type=submit]');
    await wait('document.body.innerText.includes("Under Review") && !document.querySelector("a[href*=RequisitionItems]")','submitted and read-only');
    await capture('department-submitted');
  });
  const rolePaths={Admin:'/AdminDashboard',SourcingOfficer:'/SourcingDashboard',Supplier:'/SupplierWorkspace',SourcingManager:'/CommitteeReviews',TechnicalCommittee:'/CommitteeReviews',FinancialCommittee:'/CommitteeReviews',TopCommittee:'/EvaluationWorkflow'};
  for(const [role,path] of Object.entries(rolePaths)){
    await test('BR-login-'+role,async()=>{
      if(await ev('!!document.querySelector("form[action*=Logout]")'))await logout();
      await login(role);
      await wait('location.pathname.startsWith('+JSON.stringify(path)+') && document.readyState==="complete" && !!document.querySelector("form[action*=Logout]")',role+' landing');
      if(role==='Supplier'){
        await go('/Tenders/Index');
        await wait('location.pathname.includes("AccessDenied") || document.body.innerText.includes("Access denied")','supplier direct URL denied');
      }
      await logout();
    });
  }
  await lifecycle({go,wait,click,set,ev,login,logout,capture,test,resetSession:async()=>{await call('Page.navigate',{url:'about:blank'}); await wait('location.href==="about:blank"','blank between users'); await call('Storage.clearDataForOrigin',{origin:new URL(base).origin,storageTypes:'cookies'}); await call('Network.clearBrowserCookies'); await go('/Account/Login');}});
  await writeFile(join(evidence,'results.json'),JSON.stringify({results,errors,http},null,2));
  process.stdout.write(JSON.stringify({results,errors})+'\n');
  if(results.some(r=>r.status==='FAIL')) process.exitCode=1;
} finally {
  ws?.close();
  chrome.kill();
  await sleep(400);
  await rm(profile,{recursive:true,force:true,maxRetries:3}).catch(()=>{});
}
