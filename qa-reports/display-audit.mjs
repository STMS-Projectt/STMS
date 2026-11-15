import {readFile,writeFile} from 'node:fs/promises';
import {join} from 'node:path';

// Runs on the disposable browser-test database AFTER the real procurement lifecycle.
// GET-only discovery; mutations and downloaded documents are deliberately excluded.
export async function displayAudit({go,wait,ev,login,logout,capture,call,evidence,language,resume}) {
  const q=JSON.stringify;
  const out={pages:[],modals:[],errors:[],coverage:[],started:new Date().toISOString()};
  const widths=[1440,768,390,320];
  const view=width=>call('Emulation.setDeviceMetricsOverride',{width,height:900,deviceScaleFactor:1,mobile:false});
  const settle=()=>ev('new Promise(r=>requestAnimationFrame(()=>requestAnimationFrame(()=>r(true))))');
  const save=()=>writeFile(join(evidence,'display-matrix.json'),JSON.stringify(out,null,2));
  const code=s=>s.replace(/[^a-z0-9]+/ig,'-').replace(/^-|-$/g,'').toLowerCase();
  const family=p=>p.replace(/\/\d+(?=\/|\?|$)/g,'/:id').replace(/([?&](?:id|invitationId|recordId|tenderId|purchaseRequisitionId|bidId|supplierProfileId)=)\d+/gi,'$1:id');
  const previous = resume ? JSON.parse(await readFile(join(resume,'display-matrix.json'),'utf8')) : null;
  const prior = new Map((previous ? [...previous.pages,...previous.modals] : []).filter(r=>r.checks.length===16 && !r.error && !r.blocked && r.checks.every(c=>!c.missing && c.scroll<=c.width+2)).map(r=>[r.role+':'+family(r.path),r]));
  const roles=[['Guest',null,['/','/Account/Login','/SupplierRegistration/Register','/Home/Privacy','/Account/AccessDenied']],
    ['Admin','/AdminDashboard',['/Profile','/Profile?tab=security','/Notifications']],
    ['RequestingDepartment','/PurchaseRequisitions/Dashboard',['/PurchaseRequisitions/Create','/Profile','/Notifications']],
    ['SourcingOfficer','/SourcingDashboard',['/RequisitionReviews/Pending','/Profile','/Notifications']],
    ['Supplier','/SupplierWorkspace',['/SupplierRegistration/RegistrationStatus?workspace=true','/Profile','/Notifications']],
    ['SourcingManager','/CommitteeReviews',['/Profile','/Notifications']],
    ['TechnicalCommittee','/CommitteeReviews',['/Profile','/Notifications']],
    ['FinancialCommittee','/CommitteeReviews',['/Profile','/Notifications']],
    ['TopCommittee','/EvaluationWorkflow',['/Profile','/Notifications']]];
  const safe=p=>!/(Download|Logout|Preferences|Photo|Attachment|Generate|QrImage|AiRecommendation)/i.test(p) && !/\.(pdf|svg|png|jpg)$/i.test(p);
  const links=selector=>ev(`[...document.querySelectorAll(${q(selector+' a[href]')})].filter(a=>a.origin===location.origin && !a.hasAttribute('download') && a.pathname!==location.pathname || a.origin===location.origin && a.search && a.search!==location.search).map(a=>({path:a.pathname+a.search,modal:a.hasAttribute('data-workspace-dialog')}))`);
  // Collect review candidates, not claimed automatic proof of wording/visual quality.
  const inspect=selector=>ev(`(() => {
    const root=document.querySelector(${q(selector)}); if(!root)return {missing:true};
    const visible=e=>e.checkVisibility({checkVisibilityCSS:true,checkOpacity:true}) && e.getBoundingClientRect().width>0;
    const latin=[],contrast=[];
    const color=s=>{const v=s.match(/[\\d.]+/g)?.map(Number);return v?.length>=3?[...v.slice(0,3),v[3]??1]:[0,0,0,0]};
    const mix=(a,b)=>a.slice(0,3).map((v,i)=>v*a[3]+b[i]*(1-a[3]));
    const bg=e=>{let c=[0,0,0,0];for(let n=e;n;n=n.parentElement){const s=getComputedStyle(n),x=color(s.backgroundColor);c=[...mix(c,x),c[3]+x[3]*(1-c[3])];if(c[3]>=.99)break;}return mix(c,[255,255,255]);};
    const lum=c=>c.slice(0,3).map(v=>v/255).map(v=>v<=.04045?v/12.92:((v+.055)/1.055)**2.4).reduce((s,v,i)=>s+v*[.2126,.7152,.0722][i],0);
    for(const e of root.querySelectorAll('*')){
      if(!visible(e)||e.closest('script,style,svg,option'))continue;
      const direct=[...e.childNodes].filter(n=>n.nodeType===3).map(n=>n.textContent).join(' ').trim();
      const attrs=['placeholder','aria-label','title','data-tooltip'].map(a=>e.getAttribute(a)).filter(Boolean);
      for(const text of [direct,...attrs])if(/[a-z]{3,}/i.test(text) && !/[\\u0600-\\u06ff]/.test(text))latin.push({text:text.slice(0,260),tag:e.tagName,cls:e.className});
      if(direct && !e.closest('[disabled],.visually-hidden,.sr-only') && e.getBoundingClientRect().height>0){
        const s=getComputedStyle(e),b=bg(e),f=mix(color(s.color),b),l=[lum(b),lum(f)].sort((a,b)=>b-a),ratio=(l[0]+.05)/(l[1]+.05);
        const large=parseFloat(s.fontSize)>=24 || parseFloat(s.fontSize)>=18.66 && parseInt(s.fontWeight)>=700;
        if(ratio<(large?3:4.5))contrast.push({text:direct.slice(0,100),ratio:+ratio.toFixed(2),cls:e.className,color:s.color,bg:b.map(Math.round)});
      }
    }
    return {width:innerWidth,scroll:document.documentElement.scrollWidth,rootWidth:root.clientWidth,rootScroll:root.scrollWidth,
      lang:document.documentElement.lang,dir:document.documentElement.dir,theme:document.documentElement.dataset.bsTheme,
      heading:root.querySelector('h1,h2')?.textContent.trim(),latin:[...new Map(latin.map(x=>[x.text,x])).values()],contrast};
  })()`);
  async function check(path,role,modal=false){
    const row={role,path,modal,checks:[]};
    try{
      await view(1440);await go(path);
      row.effective=await ev('location.pathname+location.search');
      row.status=await ev('performance.getEntriesByType("navigation")[0]?.responseStatus');
      if(row.status>=400 && path!=='/Account/AccessDenied' || row.effective.startsWith('/Account/Login') && role!=='Guest'){row.blocked=true;return row;}
      row.links=(await links('#main')).filter(x=>safe(x.path));
      for(const lang of ['ar','en']){
        await language(lang);
        if(modal && await ev('!!document.querySelector("#workspace-dialog")')){
          // Exercise the real AJAX dialog loader; retain nested action links for discovery.
          await ev(`(() => {const a=document.createElement('a');a.href=${q(path)};a.dataset.workspaceDialog='details';a.textContent='Audit';document.querySelector('#main').append(a);a.click();a.remove()})()`);
          await wait('document.querySelector("#workspace-dialog")?.open && document.querySelector("#workspace-dialog [data-dialog-loader]").hidden && !!document.querySelector("#workspace-dialog [data-dialog-content]")?.children.length','audit modal '+path);
        }
        const selector=modal && await ev('!!document.querySelector("#workspace-dialog[open]")')?'#workspace-dialog':await ev('!!document.querySelector("#main")')?'#main':'main';
        // Reveal read-only sections without invoking AI/provider requests.
        await ev(`[...document.querySelectorAll(${q(selector+' details:not([data-ai-box])')})].forEach(d=>d.open=true)`);
        for(const theme of ['dark','light']){
          await ev(`localStorage.setItem('stms.theme',${q(theme)});document.documentElement.dataset.bsTheme=${q(theme)}`);
          await ev('new Promise(r=>setTimeout(()=>r(true),350))');
          for(const width of widths){
            await view(width);await settle();
            const m=await inspect(selector);
            // Wording/color candidates only once per language/theme, geometry at every width.
            if(width!==1440){delete m.latin;delete m.contrast;}
            row.checks.push(m);
          }
          if(lang==='ar' && theme==='dark'){
            await view(1440);await capture('display-'+code(role+'-'+path));
            if(!modal){await view(390);await capture('display-mobile-'+code(role+'-'+path));}
          }
        }
        if(selector==='#workspace-dialog')await ev('document.querySelector("#workspace-dialog [data-dialog-close]").click()');
      }
    }catch(e){row.error=String(e.message||e);}
    return row;
  }
  try{
    for(const [role,landing,extras] of roles){
      if (previous?.coverage.some(c=>c.role===role)) continue;
      await view(1440);
      if(await ev('!!document.querySelector("form[action*=Logout]")'))await logout();
      await language('en');
      if(landing){await login(role);await wait('!!document.querySelector("form[action*=Logout]") && document.readyState==="complete"','audit login '+role);await go(landing);}
      const nav=landing?await links('#workspace-nav'):[];
      const queue=[...(landing?[{path:landing}]:[]),...nav,...extras.map(path=>({path}))];
      const seen=new Set();
      for(let i=0;i<queue.length;i++){
        const item=queue[i];if(!safe(item.path))continue;
        const key=family(item.path);if(seen.has(key))continue;seen.add(key);
        if(prior.has(role+':'+key)) continue;
        const row=await check(item.path,role,!!item.modal);
        (item.modal?out.modals:out.pages).push(row);
        for(const link of row.links||[])if(!seen.has(family(link.path)))queue.push(link);
        await save();
        process.stdout.write('DISPLAY '+role+' '+item.path+' '+(row.error?'ERROR':row.blocked?'BLOCKED':'CHECKED')+'\n');
      }
      out.coverage.push({role,families:[...seen]});await save();
    }
  }finally{
    out.finished=new Date().toISOString();await save();await view(1440);
    await ev("localStorage.setItem('stms.theme','light');document.documentElement.dataset.bsTheme='light'");
    await language('en');
  }
  return out;
}
