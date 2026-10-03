(() => {
 const list=document.querySelector('[data-insurance-list]'); if(!list)return;
 const status=document.querySelector('[data-insurance-status]'), refresh=document.querySelector('[data-insurance-refresh]');
 const vehicleList=document.querySelector('[data-insurance-vehicles]'), vehicleStatus=document.querySelector('[data-insurance-vehicles-status]'), vehicleCount=document.querySelector('[data-insurance-vehicle-count]');
 const labels={Active:'Versichert',Scheduled:'Verlängerung geplant',Expired:'Abgelaufen',Claimable:'Ersatz verfügbar',DestroyedPendingCheck:'Zerstörung wird geprüft',PaymentReview:'Zahlung: Adminprüfung',SpawnReview:'Ersatz: Adminprüfung',Claimed:'Ersatz angefordert',Cancelled:'Geschlossen'};
 let etag='',busy=false,stopped=false,timer,lastRows='',lastStatus='';
 let lastVehicles='',lastVehicleStatus='';
 const el=(tag,text,cls)=>{const n=document.createElement(tag);if(text!==undefined)n.textContent=text;if(cls)n.className=cls;return n;};
 const date=value=>value?new Date(value).toLocaleString('de-DE',{dateStyle:'short',timeStyle:'short'}):'–';
 function vehicleIconUrl(model){
  const name=String(model||'').trim().replace(/^Vehicle:/i,'').replace(/^(?:BPC_|BP_)/i,'').replace(/(?:_ES|_C)+$/i,'');
  return /^[A-Za-z0-9_]+$/.test(name)?'https://icons.gghost.games/icons/ICO_'+encodeURIComponent(name)+'.webp':'';
 }
 function appendVehicleIcon(header,model){
  const url=vehicleIconUrl(model);if(!url)return;
  const img=el('img');img.src=url;img.alt='';img.loading='lazy';img.decoding='async';
  img.addEventListener('error',()=>img.remove(),{once:true});header.append(img);
 }
 function renderVehicles(data){
  if(!vehicleList)return;
  const source=data.vehicles;
  if(!source||source.status==='unavailable'||!Array.isArray(source.items)){
   lastVehicleStatus='Fahrzeugdaten vorübergehend nicht verfügbar. Eine vorhandene Liste zeigt den letzten geladenen Stand.';vehicleStatus.textContent=lastVehicleStatus;return;
  }
  lastVehicleStatus=source.status==='profile_missing'?'Noch kein Spielerprofil im Datenstand der Homepage gefunden.':source.databaseAtUtc?'Fahrzeug-Datenstand: '+date(source.databaseAtUtc):'Fahrzeuge aus der Homepage-Datenbank.';
  vehicleStatus.textContent=lastVehicleStatus;
  const key=JSON.stringify([source.items,data.policies,Boolean(data.receivedAtUtc)]);if(key===lastVehicles)return;lastVehicles=key;
  vehicleCount.textContent='('+source.items.length+')';
  const nodes=source.items.map(v=>{
   const card=el('article',undefined,'insurance-card');
   const matching=data.policies.filter(p=>String(p.vehicleId)===String(v.id));
   const blocking=matching.filter(p=>['Active','Scheduled','PaymentReview','SpawnReview','DestroyedPendingCheck','Claimable'].includes(p.status));
   const policy=[...(blocking.length?blocking:matching)].sort((a,b)=>Date.parse(b.endUtc)-Date.parse(a.endUtc))[0];
   card.dataset.status=policy?.status||'Unknown';
   const header=el('header'),title=el('div');appendVehicleIcon(header,v.name);title.append(el('h3',v.name),el('small','Fahrzeug-ID: '+v.id));header.append(title);card.append(header);
   card.append(el('span',policy?(labels[policy.status]||policy.status):data.receivedAtUtc?'Kein Vertrag im übertragenen Stand':'Versicherungsstatus noch unbekannt','insurance-badge'));
   const details=el('dl');details.append(el('dt','Letzter Zugriff'),el('dd',v.lastAccess));
   if(policy){details.append(el('dt','Vertrag'),el('dd',policy.id));if(policy.status==='Active')details.append(el('dt','Versichert bis'),el('dd',date(policy.endUtc)));}card.append(details);
   if(!blocking.length&&/^[1-9]\d*$/.test(v.id)){
    const command='/versicherung '+v.id;card.append(el('code',command));const button=el('button','Preisanfrage kopieren');button.type='button';button.className='insurance-vehicle-copy';
    button.addEventListener('click',async()=>{try{await navigator.clipboard.writeText(command);button.textContent='Kopiert – ingame einfügen';}catch{button.textContent='Bitte Befehl oben markieren';}});card.append(button);
   }
   return card;
  });
  vehicleList.replaceChildren(...(nodes.length?nodes:[el('div',source.status==='profile_missing'?'Noch keine Fahrzeugdaten für dein Profil.':'Keine eigenen verschlossenen Fahrzeuge im Homepage-Datenstand.','insurance-empty')]));
 }
 function render(rows){
  const key=JSON.stringify(rows);if(key===lastRows)return;lastRows=key;const nodes=[];
  for(const p of [...rows].sort((a,b)=>new Date(b.startUtc)-new Date(a.startUtc))){
   const card=el('article',undefined,'insurance-card');card.dataset.status=p.status;const header=el('header');
   appendVehicleIcon(header,p.spawnClass||p.vehicleName);
   const title=el('div');title.append(el('h3',p.vehicleName),el('small',p.id+' · Fahrzeug '+p.vehicleId));header.append(title);card.append(header,el('span',labels[p.status]||p.status,'insurance-badge'));
   const details=el('dl');for(const [name,value] of [['Beginn',date(p.startUtc)],['Versichert bis',date(p.endUtc)],['Beitrag',Number(p.price).toLocaleString('de-DE')+' $'],['Laufzeit',p.weeks+' Wochen']])details.append(el('dt',name),el('dd',value));card.append(details);
   if(p.status==='Active'){const bar=el('progress');bar.max=100;bar.value=Math.max(0,Math.min(100,100*(Date.parse(p.endUtc)-Date.now())/(Date.parse(p.endUtc)-Date.parse(p.startUtc))));bar.setAttribute('aria-label','Verbleibende Versicherungszeit');card.append(bar);}
   if(p.status==='Claimable'){const code='/getrefund '+p.id;card.append(el('p','Ingame abholen:'),el('code',code));const copy=el('button','Befehl kopieren');copy.type='button';copy.addEventListener('click',async()=>{try{await navigator.clipboard.writeText(code);copy.textContent='Kopiert';}catch{copy.textContent='Bitte Befehl oben markieren';}});card.append(el('br'),copy);}
   nodes.push(card);
  }
  list.replaceChildren(...(nodes.length?nodes:[el('div','Du hast noch keine Fahrzeugversicherung. Schließe sie ingame mit /versicherung FAHRZEUGID ab.','insurance-empty')]));
 }
 async function load(force=false){
  if(busy||stopped||document.hidden&&!force)return;busy=true;refresh.disabled=true;clearTimeout(timer);
  const controller=new AbortController(),timeout=setTimeout(()=>controller.abort(),10000);
  try{const response=await fetch('api/insurance_data.php',{credentials:'same-origin',cache:'no-cache',headers:etag&&!force?{'If-None-Match':etag}:{},signal:controller.signal});
   if(response.status===401){stopped=true;throw new Error('Bitte erneut im Usercenter einloggen.');}
   if(response.status===304){status.textContent=lastStatus;if(vehicleStatus)vehicleStatus.textContent=lastVehicleStatus;return;}
   if(!response.ok)throw new Error('Aktualisierung nicht möglich. Die letzte Ansicht bleibt erhalten.');
   const data=await response.json();if(!data.ok||!Array.isArray(data.policies))throw new Error('Ungültige Antwort.');
   etag=response.headers.get('ETag')||'';render(data.policies);renderVehicles(data);
   lastStatus=data.receivedAtUtc?'Letzte Übertragung: '+date(data.receivedAtUtc):'Noch keine Daten von RedRaven übertragen.';status.textContent=lastStatus;
  }catch(error){status.textContent=error.name==='AbortError'?'Zeitüberschreitung. Automatischer neuer Versuch folgt.':error.message;if(vehicleStatus)vehicleStatus.textContent='Fahrzeugliste nicht aktualisiert. Angezeigte Daten können veraltet sein.';}
  finally{clearTimeout(timeout);busy=false;refresh.disabled=stopped;if(!stopped)timer=setTimeout(()=>load(),15000);}
 }
 refresh.addEventListener('click',()=>load(true));document.addEventListener('visibilitychange',()=>{if(!document.hidden)load();});load();
})();
