import assert from 'node:assert/strict';
export async function verifyDisplayFixes({go,wait,ev,click,set,login,logout,capture,viewport,language,test,focus}) {
  const q=JSON.stringify;
  const as=async role=>{
    if(await ev('!!document.querySelector("form[action*=Logout]")'))await logout();
    await login(role);
    await wait('!!document.querySelector("form[action*=Logout]")',role);
    await language('ar');
    if(await ev('document.documentElement.dataset.bsTheme!=="dark"'))await click('[data-theme-toggle]');
  };
  const open=async selector=>{
    await click(selector);
    await wait('!!document.querySelector("#workspace-dialog[open] [data-dialog-content] h1")','record modal');
  };
  const close=()=>click('#workspace-dialog [data-dialog-close]');
  if(focus==='home'){
    await test('AR-dark-home-visible',async()=>{
      await go('/');
      await language('ar');
      if(await ev('document.documentElement.dataset.bsTheme!=="dark"'))await click('[data-theme-toggle]');
      await wait('getComputedStyle(document.querySelector("#loader")).visibility==="hidden"','store loader cleared');
      await capture('ar-dark-home-after-loader');
    });
    return;
  }
  await test('AR-status-filters',async()=>{
    await as('Admin');
    for(const path of ['/Employees','/Departments']){
      await go(path);
      await wait('!!document.querySelector("[data-status-choice=inactive]")','status controls');
      const control=await ev('document.querySelector("[data-status-choice=inactive]").textContent');
      assert.match(control,/[\u0600-\u06ff]/);
      for(const key of ['inactive','active','']){
        await click('[data-status-choice="'+key+'"]');
        await wait(`document.querySelector('[data-status-choice=${q(key)}]').getAttribute('aria-pressed')==='true' && [...document.querySelectorAll('[data-record][data-status]')].some(r=>r.dataset.status==='active') && [...document.querySelectorAll('[data-record][data-status=active]')].some(r=>!r.hidden)===${key!=='inactive'}`,key||'all');
      }
    }
  });
  await test('AR-MVC-tender-history',async()=>{
    await as('Admin');await go('/AdminOperations/Requests');
    const paths=await ev('[...document.querySelectorAll("a[href*=RequestDetails]")].map(a=>a.pathname)');
    let link;
    for(const p of paths){
      await go(p);
      link=await ev('document.querySelector("a[href^=\x27/AdminOperations/Tender/\x27]")?.pathname');
      if(link)break;
    }
    assert.match(link,/^\/AdminOperations\/Tender\/\d+$/);
    await open('a[href="'+link+'"]');await close();
  });
  await test('AR-dark-QR',async()=>{
    await as('Admin');
    for(const [register,action] of [['Requests','RequestQr'],['Tenders','TenderQr']]){
      await go('/AdminOperations/'+register);
      await open('a[href*="/'+action+'/"]');
      await wait('document.querySelector(".record-qr img")?.complete','QR image');
      try{
        await viewport(320);
        await wait('document.documentElement.scrollWidth<=320 && document.querySelector(".record-qr").scrollWidth<=document.querySelector(".record-qr").clientWidth+1','QR fits mobile');
        await capture('ar-dark-'+action+'-320');
      }finally{await viewport(1440);await close();}
    }
  });
  await test('AR-dark-contrast',async()=>{
    const ratio=(selector,bg)=>ev(`(()=>{const n=document.querySelector(${q(selector)});const rgb=s=>s.match(/\\d+/g).slice(0,3).map(Number);const lum=a=>a.map(x=>x/255).map(x=>x<=.04045?x/12.92:((x+.055)/1.055)**2.4).reduce((s,x,i)=>s+x*[.2126,.7152,.0722][i],0);const f=lum(rgb(getComputedStyle(n).color)),b=lum(${q(bg)});return (Math.max(f,b)+.05)/(Math.min(f,b)+.05);})()`);
    await as('Admin');await go('/Suppliers');
    await open('a[href*="/Suppliers/Details/"][href*="overview"]');
    await wait('!!document.querySelector("#workspace-dialog a[href*=Suspend]")','suspend button');
    assert(await ratio('#workspace-dialog a[href*=Suspend]',[27,41,54])>=4.5);
    await capture('ar-dark-suspend');await close();await logout();
    await go('/');await language('ar');
    assert(await ratio('.hero h1 em',[26,41,50])>=3);
    await capture('ar-dark-home-accent');
  });
  await test('AR-criterion-edit-persists-both-types',async()=>{
    await as('SourcingOfficer');await go('/Tenders');
    const href=await ev('[...document.querySelectorAll("a[href*=\x27/Tenders/Criteria/\x27]")].map(a=>a.pathname)');
    let found=false;
    for(const p of href){
      await go(p);if(await ev('!!document.querySelector("[data-add-criterion]")')){found=true;break;}
    }
    assert(found,'Draft tender available for edit');
    const path=await ev('location.pathname');
    await set('[data-add-criterion] [name=Name]','معيار تدقيق');
    await set('[data-add-criterion] [name=WeightPercent]','25');
    await click('[data-add-criterion] button[type=submit]');
    const form='form:has(input[name=edit]):has(input[name=Name][value="معيار تدقيق"])';
    await wait('!!document.querySelector('+q(form)+')','criterion created');
    for(const type of ['Financial','Technical']){
      await ev('document.querySelector('+q(form)+').closest("details").open=true');
      const select=form+' select[name=Type]';
      assert.match(await ev('document.querySelector('+q(select+' option[value="'+type+'"]')+').textContent'),/[\u0600-\u06ff]/);
      await set(select,type);await click(form+' button[type=submit]');
      await wait('document.querySelector('+q(select+' option[selected]')+')?.value==='+q(type),'saved option '+type);
      await go(path);
      assert.equal(await ev('document.querySelector('+q(select)+').value'),type);
    }
    await language('en');
  });
}
