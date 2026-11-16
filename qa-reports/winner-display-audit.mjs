import assert from 'node:assert/strict';

export async function verifyWinnerDisplay({go,wait,ev,click,login,logout,capture,viewport,language,test}){
  const signedIn='!!document.querySelector("form[action*=Logout]")';
  const as=async role=>{
    if(await ev(signedIn))await logout();
    await login(role);
    await wait(signedIn,role+' signed in');
  };
  if(process.env.STMS_WINNER_SKIP_SIDEBARS!=='1')await test('Winner-sidebars-all-roles',async()=>{
    for(const role of ['Admin','DepartmentSmoke','SourcingOfficer','Supplier','SourcingManager','TechnicalCommittee','FinancialCommittee','TopCommittee']){
      await as(role);
      await language('en');
      const sidebar='document.querySelector("#workspace-nav")';
      const text=sidebar+'.textContent';
      const routes='[...'+sidebar+'.querySelectorAll("a")].map(a=>a.pathname+a.search)';
      assert.doesNotMatch(await ev(text),/[\u0600-\u06ff]/,role);
      const links=await ev(routes);
      assert(links.length>1,role);
      await language('ar');
      assert.doesNotMatch(await ev(text),/[A-Za-z]+/,role);
      assert.deepEqual(await ev(routes),links,role);
    }
  });
  for(const role of ['TopCommittee','Admin'])await test('Winner-display-'+role,async()=>{
    await as(role);
    for(const lang of ['ar','en']){
      await language(lang);
      await go('/EvaluationWorkflow');
      const link='#main a[href^="/EvaluationWorkflow/Winner/"]';
      await wait('!!document.querySelector('+JSON.stringify(link)+')','winner action');
      await click(link);
      await wait('!!document.querySelector("#workspace-dialog[open] .ai-pick")','facts-only winner brief loaded');
      const title=await ev('document.querySelector("#workspace-dialog h1").textContent');
      assert.equal(title,lang==='ar'?'المورد الفائز':'Winning supplier');
      const dialogText=await ev('document.querySelector("#workspace-dialog").innerText');
      assert.match(dialogText,lang==='ar'?/البيانات المسجلة|أسباب التوصية/:/recorded facts|Why it won/);
      for(const theme of ['light','dark']){
        if(await ev('document.documentElement.dataset.bsTheme')!==theme){
          await ev('document.querySelector("[data-theme-toggle]").click()');
          await wait('document.documentElement.dataset.bsTheme==='+JSON.stringify(theme),theme);
        }
        await viewport(390);
        assert(await ev('document.documentElement.scrollWidth<=innerWidth && document.querySelector("#workspace-dialog").scrollWidth <= document.querySelector("#workspace-dialog").clientWidth+1'));
        await capture('winner-'+role+'-'+lang+'-'+theme);
        await viewport(1440);
      }
      if(lang==='en' && role==='Admin'){
        const pdf=await ev('(async()=>{const url=document.querySelector("#workspace-dialog a[href*=DownloadWinnerBrief]").href;const res=await fetch(url);return [res.status,res.headers.get("content-type"),(await res.text()).slice(0,5)];})()');
        assert.deepEqual(pdf,[200,'application/pdf','%PDF-']);
      }
      if(lang==='ar'){
        const printable=await ev('document.querySelector("#workspace-dialog a[href*=PrintWinnerBrief]").pathname');
        assert(printable);
        if(role==='TopCommittee'){
          await go(printable);
          assert.equal(await ev('document.documentElement.dir'),'rtl');
          assert.match(await ev('document.querySelector("main").innerText'),/أسباب التوصية/);
          await capture('winner-print-ar');
          await go('/EvaluationWorkflow');
        }
      }
      if(await ev('!!document.querySelector("#workspace-dialog[open]")'))await click('#workspace-dialog [data-dialog-close]');
    }
  });
}
