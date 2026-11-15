import {writeFile} from 'node:fs/promises';
import {join} from 'node:path';

// Read-only inspection against the browser suite's populated, disposable database.
export async function siteUxAudit({base,go,wait,click,ev,login,logout,capture,call,evidence}) {
  const q=JSON.stringify;
  const widths=[1440,1024,768,390,360,320];
  const view=(width,height=width<500?740:900)=>call('Emulation.setDeviceMetricsOverride',
    {width,height,deviceScaleFactor:1,mobile:false});
  const settle=()=>ev('new Promise(r=>requestAnimationFrame(()=>requestAnimationFrame(()=>r(true))))');
  const output={widths,pages:[],modals:[],interactions:[]};
  const selected=new Set(['/Home','/Account/Login','/SupplierRegistration/Register','/AdminDashboard',
    '/Employees','/PurchaseRequisitions/Dashboard','/SourcingDashboard','/Tenders','/Tenders/Index',
    '/SupplierWorkspace','/CommitteeReviews','/EvaluationWorkflow']);
  const roles=[
    ['Guest',null,['/','/Account/Login','/SupplierRegistration/Register','/Home/Privacy']],
    ['Admin','/AdminDashboard',['/Profile','/Profile?tab=security','/Notifications']],
    ['RequestingDepartment','/PurchaseRequisitions/Dashboard',['/PurchaseRequisitions/Create','/Notifications']],
    ['SourcingOfficer','/SourcingDashboard',['/RequisitionReviews/Pending','/Notifications']],
    ['Supplier','/SupplierWorkspace',['/SupplierRegistration/RegistrationStatus?workspace=true','/Notifications']],
    ['SourcingManager','/CommitteeReviews',[]],
    ['TechnicalCommittee','/CommitteeReviews',[]],
    ['FinancialCommittee','/CommitteeReviews',[]],
    ['TopCommittee','/EvaluationWorkflow',[]],
  ];
  const allowedModal=/\/(Details|RequestQr|Request|Tender|InvitationDetails|BidDetails|Criteria|Review|Items|Edit|Create|AddDocument|Categories|Decision|Weights|AiReport)(\/|$)/i;
  const save=()=>writeFile(join(evidence,'ux-matrix.json'),JSON.stringify(output,null,2));
  const errorText=e=>String(e?.message || e).split('\n')[0];
  const code=path=>path.replace(/[^a-z0-9]+/ig,'-').replace(/^-|-$/g,'').toLowerCase()||'home';
  const metrics=()=>ev(`(() => {
    const main=document.querySelector('main');
    const visible=e=>e.checkVisibility({checkVisibilityCSS:true}) && !e.closest('details:not([open])');
    const nameless=[...main?.querySelectorAll('button,a[href]')||[]].filter(e=>visible(e) &&
      ![e.innerText,e.getAttribute('aria-label'),e.getAttribute('aria-labelledby'),
         e.getAttribute('title'),e.querySelector('img')?.alt].some(s=>s?.trim())).length;
    return {w:innerWidth,scroll:document.documentElement.scrollWidth,
      main:main?.scrollWidth, h1:main?.querySelector('h1')?.innerText.trim()||null,
      tables:main?.querySelectorAll('table').length||0, cards:main?.querySelectorAll('.record-card').length||0,
      nameless, filter:!!main?.querySelector('[data-directory-filter],.dept-filter-dock,.directory-toolbar')};
  })()`);
  const modalMetrics=()=>ev(`(() => {
    const d=document.querySelector('#workspace-dialog[open]');
    if(!d)return null;
    const r=d.getBoundingClientRect(), c=d.querySelector('[data-dialog-content]');
    return {w:innerWidth, width:Math.round(r.width), x:Math.round(r.x), right:Math.round(r.right),
      scroll:c.scrollWidth, content:c.clientWidth, height:Math.round(r.height),
      contentHeight:c.scrollHeight, visibleHeight:c.clientHeight,
      focus:d.contains(document.activeElement), close:!!d.querySelector('[data-dialog-close]')};
  })()`);
  const modalActions=async()=>{
    const links=await ev(`[...document.querySelectorAll('#main a[data-workspace-dialog][href]')].map(a=>({
      path:a.pathname+a.search, name:(a.getAttribute('aria-label')||a.getAttribute('title')||a.innerText).trim()
    }))`);
    const seen=new Set();
    return links.filter(a=>{
      if(!allowedModal.test(a.path.split('?')[0]))return false;
      const type=a.path.replace(/\/\d+(?=\/|\?|$)/g,'/:id');
      if(seen.has(type))return false;
      seen.add(type);return true;
    }).slice(0,3);
  };
  async function checkModal(role,route,action){
    const item={role,route,...action,checks:[]};
    try{
      await view(1440);
      await ev(`(() => {
        const trigger=[...document.querySelectorAll('#main a[data-workspace-dialog]')].find(a=>a.pathname+a.search===${q(action.path)});
        window.stmsAuditTrigger=trigger;trigger.focus({preventScroll:true});trigger.click();
      })()`);
      await wait('!!document.querySelector("#workspace-dialog[open] [data-dialog-content]")?.children.length && document.querySelector("#workspace-dialog [data-dialog-loader]").hidden','audit dialog loaded');
      await settle();
      item.samePage=await ev('location.pathname+location.search')===route;
      item.heading=await ev('document.querySelector("#workspace-dialog [data-dialog-content] h1, #workspace-dialog [data-dialog-content] h2")?.innerText.trim() || document.querySelector("#workspace-dialog-title")?.innerText.trim()');
      for(const width of [1440,390,320]){
        await view(width);await settle();item.checks.push(await modalMetrics());
      }
      if(item.checks.some(x=>x && (x.x < -1 || x.right > x.w+1 || x.scroll > x.content+2)))
        await capture('modal-overflow-'+code(role)+'-'+code(action.path));
      await call('Input.dispatchKeyEvent',{type:'keyDown',key:'Escape',code:'Escape',windowsVirtualKeyCode:27});
      await call('Input.dispatchKeyEvent',{type:'keyUp',key:'Escape',code:'Escape',windowsVirtualKeyCode:27});
      await wait('!document.querySelector("#workspace-dialog").open && !document.querySelector("#workspace-dialog [data-dialog-content]").children.length','Escape closes modal');
      item.escape=true;
      item.focusReturned=await ev('document.activeElement===window.stmsAuditTrigger');
    }catch(e){
      item.error=errorText(e);
      await go(route).catch(()=>{});
    }
    output.modals.push(item);
  }
  for(const [role,landing,extras] of roles){
    try{
      await view(1440);
      if(await ev('!!document.querySelector("form[action*=Logout]")'))await logout();
      if(landing){
        await login(role);
        await wait(`location.pathname.startsWith(${q(landing)}) && !!document.querySelector('form[action*=Logout]')`,role+' audit landing');
      }
      const side=landing?await ev(`[...document.querySelectorAll('#workspace-nav a.side-link')].map(a=>a.pathname+a.search)`):[];
      for(const path of new Set([...(landing?[landing]:[]),...side,...extras])){
        const row={role,path,checks:[]};
        try{
          await view(1440);
          await go(path);
          row.effective=await ev('location.pathname+location.search');
          row.status=await ev('performance.getEntriesByType("navigation")[0]?.responseStatus || null');
          for(const width of widths){
            await view(width);await settle();
            const m=await metrics();row.checks.push(m);
            if((selected.has(path)||path==='/') &&
               (width===1440||width===390) && (!['TechnicalCommittee','FinancialCommittee'].includes(role)))
              await capture('ux-'+code(role)+'-'+code(path)+'-'+width);
            if(m.scroll>m.w+1)
              await capture('overflow-'+code(role)+'-'+code(path)+'-'+width);
          }
          if(landing && row.checks[0].h1 && row.effective===path){
            for(const action of await modalActions())await checkModal(role,path,action);
          }
        }catch(e){row.error=errorText(e);}
        output.pages.push(row);
      }
      if(role==='Admin'){
        await view(1440);await go('/Employees');
        const control='.dept-filter-dock.is-in-view .dept-dock-button[aria-haspopup]';
        await ev('document.querySelector(".dept-directory-heading").scrollIntoView({block:"center",behavior:"instant"})');
        await call('Input.dispatchMouseEvent',{type:'mouseMoved',x:0,y:0});
        const found=await ev(`!!document.querySelector(${q(control)})`);
        const interaction={id:'employee-filter-keyboard',found};
        if(found){
          await ev(`document.querySelector(${q(control)}).focus()`);
          await call('Input.dispatchKeyEvent',{type:'keyDown',key:'Enter',code:'Enter',windowsVirtualKeyCode:13,
            nativeVirtualKeyCode:13,text:'\r',unmodifiedText:'\r'});
          await call('Input.dispatchKeyEvent',{type:'keyUp',key:'Enter',code:'Enter',windowsVirtualKeyCode:13});
          await settle();
          interaction.expanded=await ev(`document.querySelector(${q(control)}).getAttribute('aria-expanded')`);
        }
        output.interactions.push(interaction);
      }
    }catch(e){output.interactions.push({id:'role-audit',role,error:errorText(e)});}
    await save();
  }
  await view(1440);
}
