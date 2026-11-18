const $ = id => document.getElementById(id);
let currentUrl = '';
let downloading = false;
let availableFormats = [];
let playlistVideos = [];
let playlistRunning = false;
let playlistCancelRequested = false;
function refreshFormats() {
  const selectedType = $('outputFormat').value;
  const previous = $('quality').value;
  $('quality').replaceChildren();
  for (const format of availableFormats) {
    const isMp3 = format.key.startsWith('mp3-') || format.key === 'tt-mp3' || format.key === 'ig-mp3';
    if ((selectedType === 'mp3') !== isMp3) continue;
    const option = document.createElement('option');
    option.value = format.key;
    option.textContent = format.label;
    $('quality').append(option);
  }
  if ([...$('quality').options].some(option => option.value === previous)) $('quality').value = previous;
  $('download').disabled = downloading || !$('permission').checked || !$('quality').value;
  $('downloadText').textContent = selectedType === 'mp3' ? 'Download audio' : 'Download video';
  if (!$('quality').value) message('No ' + selectedType.toUpperCase() + ' formats available. Check that FFmpeg is installed.');
  else message('');
}
$('outputFormat').onchange = refreshFormats;
const message = text => { $('message').textContent = text; };
const platformFor = url => { try { const host = new URL(url).hostname.toLowerCase(); if (host === 'instagram.com' || host === 'www.instagram.com') return 'INSTAGRAM'; if (host === 'tiktok.com' || host.endsWith('.tiktok.com')) return 'TIKTOK'; } catch {} return 'YOUTUBE'; };
async function jsonFetch(url, options) {
  const response = await fetch(url, options);
  const body = await response.json();
  if (!response.ok) throw new Error(body.error || 'Request failed');
  return body;
}
async function checkServer() {
  try { const result = await jsonFetch('/api/health'); $('serverState').textContent = result.ok ? 'Online' : 'Unavailable'; }
  catch { $('serverState').textContent = 'Unavailable'; }
}
checkServer();
async function findVideo() {
  if (downloading) return;
  $('find').disabled = true; $('details').hidden = true; $('playlistPanel').hidden = true; message('Finding video…');
  currentUrl = $('url').value.trim();
  if (!currentUrl) { message('Paste a video link first.'); $('find').disabled = false; return; }
  try {
    if (isPlaylistLink(currentUrl)) {
      await findPlaylist(currentUrl);
      return;
    }
    const info = await jsonFetch('/api/info', {method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({url:currentUrl})});
    $('thumbnail').src = info.thumbnail || '';
    $('title').textContent = info.title || 'Untitled video';
    $('author').textContent = info.author || 'Unknown creator';
    $('platformBadge').textContent = platformFor(currentUrl);
    availableFormats = info.formats || [];
    $('outputFormat').value = 'mp4';
    refreshFormats();
    $('details').hidden = false;
    $('progressBox').hidden = true;
    $('permission').checked = false; $('download').disabled = true;
    if (!availableFormats.length) message(info.notice || 'No downloadable formats available.');
  } catch(e) { message(e.message); }
  finally { $('find').disabled = false; }
}
$('find').onclick = findVideo;
$('url').addEventListener('keydown', event => { if (event.key === 'Enter') findVideo(); });
$('permission').onchange = () => { $('download').disabled = downloading || !$('permission').checked || !$('quality').value; };
$('download').onclick = async () => {
  if (downloading) return;
  downloading = true; $('download').disabled = true; $('find').disabled = true;
  $('progressBox').hidden = false; $('progress').style.width = '0%'; $('progressPercent').textContent = '0%';
  $('status').textContent = 'Starting download…'; message('');
  try {
    const {token} = await jsonFetch('/api/download',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({url:currentUrl,key:$('quality').value,permission:$('permission').checked})});
    while (true) {
      await new Promise(resolve => setTimeout(resolve,1100));
      const result = await jsonFetch('/api/status/'+encodeURIComponent(token));
      const progress = Math.min(100, Math.max(0, Number(result.progress) || 0));
      $('status').textContent = result.state + ' · ' + Math.round(progress) + '%';
      $('progress').style.width = progress + '%';
      $('progressPercent').textContent = Math.round(progress) + '%';
      if (result.state === 'Failed') throw new Error(result.error || 'Download failed');
      if (result.state === 'Ready') {
        window.location.href = '/api/file/'+encodeURIComponent(token);
        message('Download ready. Your MP4 or MP3 should save now.');
        break;
      }
    }
  } catch(e) { message(e.message); $('status').textContent = 'Download failed'; }
  finally { downloading = false; $('find').disabled = false; $('download').disabled = !$('permission').checked || !$('quality').value; }
};


// Playlist queue: reuses the existing tested single-video endpoints and audio selection.
function isPlaylistLink(url) {
  try {
    const u = new URL(url);
    return ['youtube.com','www.youtube.com','m.youtube.com','music.youtube.com'].includes(u.hostname)
      && !!u.searchParams.get('list');
  } catch { return false; }
}
function updatePlaylistSelection() {
  const selected = [...document.querySelectorAll('.playlist-check:checked')].length;
  $('playlistCount').textContent = `${selected} of ${playlistVideos.length} selected`;
  $('playlistStart').disabled = playlistRunning || selected === 0 || !$('playlistPermission').checked;
}
async function findPlaylist(url) {
  $('playlistStatus').textContent = 'Loading playlist…';
  const data = await jsonFetch('/api/playlist', {method:'POST',
    headers:{'Content-Type':'application/json'},body:JSON.stringify({url})});
  playlistVideos = data.videos || [];
  $('playlistHeading').textContent = `Playlist · ${playlistVideos.length} videos${data.truncated?' (first 200)':''}`;
  $('playlistItems').replaceChildren();
  playlistVideos.forEach((video,index)=>{
    const row=document.createElement('label');
    row.className='playlist-row';
    const check=document.createElement('input');
    check.type='checkbox'; check.checked=true; check.className='playlist-check';
    check.dataset.index=String(index); check.addEventListener('change',updatePlaylistSelection);
    const img=document.createElement('img');img.src=video.thumbnail;img.alt='';
    const text=document.createElement('span');text.textContent=`${index+1}. ${video.title}`;
    row.append(check,img,text);
    $('playlistItems').append(row);
  });
  $('playlistPermission').checked=false;
  $('playlistPanel').hidden=false;
  $('playlistPercent').textContent=`0 / ${playlistVideos.length}`;
  $('playlistProgress').style.width='0%';
  $('playlistCurrentBox').hidden=true;
  $('playlistCurrentProgress').style.width='0%';
  $('playlistCurrentPercent').textContent='0%';
  $('playlistStatus').textContent=playlistVideos.length ? 'Choose videos and confirm permission.' : 'No videos found.';
  updatePlaylistSelection();
}
$('playlistSelectAll').onclick=()=>{
  document.querySelectorAll('.playlist-check').forEach(x=>x.checked=true);
  updatePlaylistSelection();
};
$('playlistSelectNone').onclick=()=>{
  document.querySelectorAll('.playlist-check').forEach(x=>x.checked=false);
  updatePlaylistSelection();
};
$('playlistPermission').onchange=updatePlaylistSelection;
$('playlistCancel').onclick=()=>{playlistCancelRequested=true;$('playlistStatus').textContent='Stopping after the current video…';};
function selectPlaylistFormat(formats, type, maxHeight) {
  const options=formats.filter(f=>type==='mp3'?f.key.startsWith('mp3-'):!f.key.startsWith('mp3-'));
  if (!options.length) return null;
  if (type==='mp3') return options[0]; // smart-audio backend lists best audio first
  const separate=options.filter(f=>f.key.startsWith('sep-') && f.height<=maxHeight);
  const direct=options.filter(f=>f.key.startsWith('mux-') && f.height<=maxHeight);
  const candidates=separate.length?separate:direct;
  return candidates.sort((a,b)=>b.height-a.height)[0] || options[0];
}
function setPlaylistProgress(completed, total, currentPercent, stage, title) {
  const percent=Math.min(100,Math.max(0,Number(currentPercent)||0));
  const overall=total ? Math.round((completed+percent/100)/total*100) : 0;
  $('playlistCurrentBox').hidden=false;
  $('playlistCurrentLabel').textContent=`Video ${completed+1} of ${total}: ${title}`;
  $('playlistCurrentPercent').textContent=`${Math.round(percent)}%`;
  $('playlistCurrentProgress').style.width=`${percent}%`;
  $('playlistCurrentStage').textContent=stage || 'Preparing…';
  $('playlistProgress').style.width=`${overall}%`;
  $('playlistPercent').textContent=`${completed} / ${total} complete · ${overall}% overall`;
}
async function waitForJob(token, onProgress) {
  // Backend already exposes actual stream download progress and FFmpeg stage.
  // Poll immediately, then once per second. Processing stages may hold at 85/90%.
  for (let i=0;i<1800;i++) {
    const result=await jsonFetch('/api/status/'+encodeURIComponent(token));
    onProgress(result);
    if (result.state==='Failed') throw new Error(result.error || 'Download failed');
    if (result.state==='Ready') return;
    await new Promise(resolve=>setTimeout(resolve,1000));
  }
  throw new Error('Download timed out.');
}
$('playlistStart').onclick=async()=>{
  if (playlistRunning) return;
  const indices=[...document.querySelectorAll('.playlist-check:checked')].map(x=>Number(x.dataset.index));
  if (!indices.length || !$('playlistPermission').checked) return;
  playlistRunning=true;playlistCancelRequested=false;
  $('playlistStart').disabled=true;$('playlistCancel').disabled=false;$('find').disabled=true;
  const type=$('playlistFormat').value;
  const height=$('playlistQuality').value==='best'?99999:Number($('playlistQuality').value);
  let done=0, failed=0;
  $('playlistCurrentBox').hidden=false;
  try {
    for (const index of indices) {
      if (playlistCancelRequested) break;
      const video=playlistVideos[index];
      $('playlistStatus').textContent=`Processing ${done+1} of ${indices.length}`;
      setPlaylistProgress(done,indices.length,0,'Fetching video information…',video.title);
      try {
        const info=await jsonFetch('/api/info',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({url:video.url})});
        const selected=selectPlaylistFormat(info.formats||[],type,height);
        if (!selected) throw new Error(`No ${type.toUpperCase()} option`);
        const {token}=await jsonFetch('/api/download',{method:'POST',headers:{'Content-Type':'application/json'},
          body:JSON.stringify({url:video.url,key:selected.key,permission:true})});
        await waitForJob(token, result=>setPlaylistProgress(done,indices.length,result.progress,result.state,video.title));
        // This uses the existing /api/file route, preserving its temp cleanup.
        // WebView2/Windows may require enabling multiple automatic downloads.
        const link=document.createElement('a');
        link.href='/api/file/'+encodeURIComponent(token);
        link.download='';document.body.append(link);link.click();link.remove();
      } catch (error) {
        failed++;
        console.error('Playlist item failed:',video.title,error);
        $('playlistStatus').textContent=`Skipped ${video.title}: ${error.message}`;
      }
      done++;
      $('playlistPercent').textContent=`${done} / ${indices.length} complete · ${Math.round(done/indices.length*100)}% overall`;
      $('playlistProgress').style.width=`${Math.round(done/indices.length*100)}%`;
      if (failed && done===indices.length) $('playlistCurrentStage').textContent='Finished with skipped items';
      await new Promise(resolve=>setTimeout(resolve,400));
    }
    $('playlistStatus').textContent=`Queue finished: ${done-failed} started downloads, ${failed} failed${playlistCancelRequested?', stopped early':''}. Check Downloads.`;
    $('playlistCurrentBox').hidden=true;
  } finally {
    playlistRunning=false;$('playlistCancel').disabled=true;$('find').disabled=false;
    updatePlaylistSelection();
  }
};


// Universal is an isolated tab. Existing YouTube/TikTok/playlist handlers stay unchanged.
let universalCurrentUrl = '';
let universalDownloading = false;
function switchSection(section) {
  const universal = section === 'universal';
  $('downloaderPage').hidden = universal;
  $('universalPage').hidden = !universal;
  $('navDownloader').classList.toggle('active', !universal);
  $('navUniversal').classList.toggle('active', universal);
  $('navDownloader').setAttribute('aria-current', universal ? 'false' : 'page');
  $('navUniversal').setAttribute('aria-current', universal ? 'page' : 'false');
  $('currentSection').textContent = universal ? 'Universal' : 'Downloader';
  window.scrollTo(0, 0);
}
$('navDownloader').onclick = () => switchSection('downloader');
$('navUniversal').onclick = () => switchSection('universal');
const universalMessage = text => { $('universalMessage').textContent = text; };
$('universalFormat').onchange = () => {
  const mp3 = $('universalFormat').value === 'mp3';
  $('universalQuality').value = mp3 ? 'universal-mp3' : 'universal-mp4';
  $('universalDownloadText').textContent = mp3 ? 'Download audio' : 'Download video';
};
$('universalQuality').onchange = () => {
  $('universalFormat').value = $('universalQuality').value === 'universal-mp3' ? 'mp3' : 'mp4';
  $('universalDownloadText').textContent = $('universalFormat').value === 'mp3' ? 'Download audio' : 'Download video';
};
$('universalPermission').onchange = () => {
  $('universalDownload').disabled = universalDownloading || !$('universalPermission').checked;
};
async function findUniversal() {
  if (universalDownloading) return;
  universalCurrentUrl = $('universalUrl').value.trim();
  $('universalDetails').hidden = true;
  $('universalFind').disabled = true;
  universalMessage('Looking up media with yt-dlp…');
  try {
    const info = await jsonFetch('/api/universal/info', {method:'POST',
      headers:{'Content-Type':'application/json'},body:JSON.stringify({url:universalCurrentUrl})});
    $('universalTitle').textContent = info.title || 'Untitled media';
    $('universalAuthor').textContent = info.author || 'Unknown creator';
    $('universalThumbnail').src = info.thumbnail || '';
    $('universalPlatform').textContent = new URL(universalCurrentUrl).hostname.toUpperCase();
    $('universalFormat').value = 'mp4';
    $('universalQuality').value = 'universal-mp4';
    $('universalDownloadText').textContent = 'Download video';
    $('universalPermission').checked = false;
    $('universalDownload').disabled = true;
    $('universalProgressBox').hidden = true;
    $('universalDetails').hidden = false;
    universalMessage('');
  } catch (error) { universalMessage(error.message); }
  finally { $('universalFind').disabled = false; }
}
$('universalFind').onclick = findUniversal;
$('universalUrl').addEventListener('keydown', event => { if (event.key === 'Enter') findUniversal(); });
$('universalDownload').onclick = async () => {
  if (universalDownloading || !$('universalPermission').checked) return;
  universalDownloading = true;
  $('universalFind').disabled = true;
  $('universalDownload').disabled = true;
  $('universalProgressBox').hidden = false;
  $('universalProgress').style.width = '0%';
  $('universalPercent').textContent = '0%';
  $('universalStatus').textContent = 'Starting…';
  universalMessage('');
  try {
    const {token} = await jsonFetch('/api/universal/download', {method:'POST',
      headers:{'Content-Type':'application/json'},
      body:JSON.stringify({url:universalCurrentUrl,key:$('universalQuality').value,permission:true})});
    await waitForJob(token, result => {
      const percent = Math.max(0, Math.min(100, Number(result.progress) || 0));
      $('universalProgress').style.width = percent + '%';
      $('universalPercent').textContent = Math.round(percent) + '%';
      $('universalStatus').textContent = result.state + ' · ' + Math.round(percent) + '%';
    });
    window.location.href = '/api/file/' + encodeURIComponent(token);
    universalMessage('Download ready. Your file should save now.');
  } catch (error) { universalMessage(error.message); $('universalStatus').textContent = 'Download failed'; }
  finally {
    universalDownloading = false;
    $('universalFind').disabled = false;
    $('universalDownload').disabled = !$('universalPermission').checked;
  }
};
