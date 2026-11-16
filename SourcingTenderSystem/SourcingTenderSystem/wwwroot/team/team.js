// Specialists Data
const team = [
  {
    name: 'Yaqeen Jawabreh',
    role: 'Product Owner',
    tag: 'Apparel Architecture',
    bio: 'Orchestrating bespoke fabric tenders, yarn quality lab-dips, and direct mill accreditation.',
    github: 'yaqeenjawabreh',
    linkedin: 'yaqeenjawabreh',
    photo: null
  },
  {
    name: 'Amneh Alhazaimeh',
    role: 'Scrum Master',
    tag: 'Agile Garment Pipeline',
    bio: 'Weaving cross-functional engineering sprints with factory production schedules and deadlines.',
    github: 'amneh-01',
    linkedin: 'amneh-alhazaimeh-a01a7a340',
    photo: 'amneh.png'
  },
  {
    name: 'Lujain Qaqaa',
    role: 'QA Engineer',
    tag: 'AQL 2.5 Quality Standards',
    bio: 'Verifying stitch count precision, textile tensile strength, and zero-defect platform releases.',
    github: 'Lujainqaqaa',
    linkedin: 'lujain-qaqaa-384514434',
    photo: 'lujain.png'
  },
  {
    name: 'Omar Alsmadi',
    role: 'Developer',
    tag: 'Full-Stack Lead',
    bio: 'Crafting robust core architectures, tender matching algorithms, and automated escrow contracts.',
    github: 'omarsk2003',
    linkedin: 'omarkamalalsmadi',
    photo: 'omar.png'
  },
  {
    name: 'Wessam Khraisat',
    role: 'Developer',
    tag: 'Backend & Systems',
    bio: 'Engineering high-throughput databases, supply chain APIs, and compliance auditing engines.',
    github: 'wessamkhraisat98',
    linkedin: 'divbux',
    photo: 'wessam.png'
  },
  {
    name: 'Eyad Mansur',
    role: 'Developer',
    tag: 'Frontend & UI/UX',
    bio: 'Tailoring fluid user experiences, 3D clothing swatches, and intuitive buyer journeys.',
    github: 'Eyad-03',
    linkedin: 'eyad-mansur-905446321',
    photo: 'eyad.png'
  },
  {
    name: 'Alaa Frihat',
    role: 'Developer',
    tag: 'Cloud & DevOps',
    bio: 'Maintaining uninterrupted cloud servers, continuous delivery looms, and zero-downtime deployments.',
    github: 'AlaaFrihat',
    linkedin: 'ala-a-frihat-8149a53b3',
    photo: 'alaa.png'
  }
];

const reducedMotion = matchMedia('(prefers-reduced-motion: reduce)');
let selected = 3;
let rotation = 0;
let isDived = false;
let scrollProgress = 0;
let lastTime = 0;
let manualLockUntil = 0;

const cluster = document.getElementById('cluster');
const orbitWorld = document.getElementById('orbitWorld');
const stitchedStage = document.getElementById('stitchedStage');
const introText = document.getElementById('introText');
const timelinePills = document.getElementById('timelinePills');
const stitchBarFill = document.getElementById('stitchBarFill');
const stitchStatusText = document.getElementById('stitchStatusText');
const modeToggleText = document.getElementById('modeToggleText');
const stitchBorderRect = document.getElementById('stitchBorderRect');
const sewingNeedle = document.getElementById('sewingNeedle');
const sewingScanline = document.getElementById('sewingScanline');
const stitchCounter = document.getElementById('stitchCounter');

function initials(name) {
  return name.split(' ').map(p => p[0]).join('');
}

const portraitBase = new URL('portraits/', document.currentScript.src);
function setPortrait(img, person, parent) {
  img.alt = person.name;
  img.onerror = () => parent.classList.add('no-photo');
  parent.classList.toggle('no-photo', !person.photo);
  if (person.photo) img.src = new URL(person.photo, portraitBase);
  else img.removeAttribute('src');
}
function buildImage(person, parent) {
  const img = document.createElement('img');
  setPortrait(img, person, parent);
  return img;
}

// Populate Orbit Buttons
team.forEach((person, index) => {
  const btn = document.createElement('button');
  btn.type = 'button';
  btn.className = 'orbit-photo';
  btn.setAttribute('aria-label', `Select ${person.name}`);

  const img = buildImage(person, btn);
  btn.appendChild(img);

  const initSpan = document.createElement('span');
  initSpan.className = 'initials';
  initSpan.textContent = initials(person.name);
  btn.appendChild(initSpan);

  btn.addEventListener('click', () => {
    if (window.teamGoToMember) window.teamGoToMember(index);
    else choose(index, true);
  });
  cluster.appendChild(btn);

  // Timeline dot
  const dot = document.createElement('button');
  dot.type = 'button';
  dot.className = 'timeline-dot';
  dot.setAttribute('aria-label', `Select ${person.name}`);
  dot.addEventListener('click', () => {
    choose(index, true);
  });
  timelinePills.appendChild(dot);
});

// Layout Orbit in 3D circle
function layoutOrbit() {
  const size = orbitWorld.clientWidth;
  const radius = size * 0.38;
  const buttons = [...cluster.children];

  buttons.forEach((btn, index) => {
    const angle = rotation + (index * Math.PI * 2 / team.length) - Math.PI / 2;
    const x = (size / 2) + Math.cos(angle) * radius - (btn.offsetWidth / 2);
    const y = (size / 2) + Math.sin(angle) * radius - (btn.offsetHeight / 2);

    btn.style.left = `${x}px`;
    btn.style.top = `${y}px`;
    btn.style.zIndex = String(10 + Math.round((Math.sin(angle) + 1) * 5));
  });
}

// Select a member & prepare their text for the stitch reveal
function choose(index, manual = false) {
  selected = (index + team.length) % team.length;
  const person = team[selected];

  document.getElementById('spotlightIndex').textContent = `${String(selected + 1).padStart(2, '0')} / 07`;
  document.getElementById('stitchedRole').textContent = person.role;
  document.getElementById('stitchedTag').textContent = person.tag;
  document.getElementById('stitchedBio').textContent = person.bio;
  document.getElementById('githubPatch').href = `https://github.com/${person.github}`;
  document.getElementById('linkedinPatch').href = `https://www.linkedin.com/in/${person.linkedin}/`;

  // Render Name with individual stitchable letter spans
  const nameContainer = document.getElementById('stitchedName');
  nameContainer.innerHTML = '';
  [...person.name].forEach((ch) => {
    const span = document.createElement('span');
    span.className = 'stitched-char';
    span.textContent = ch === ' ' ? '\u00A0' : ch;
    nameContainer.appendChild(span);
  });

  // Update Photo
  const wrapper = document.querySelector('.portrait-cutout-wrapper');
  wrapper.classList.remove('no-photo');
  const spotlightImg = document.getElementById('spotlightImg');
  const initialsEl = document.getElementById('spotlightInitials');
  initialsEl.textContent = initials(person.name);

  setPortrait(spotlightImg, person, wrapper);

  // Update active state
  [...cluster.children].forEach((btn, i) => btn.classList.toggle('active', i === selected));
  [...timelinePills.children].forEach((dot, i) => dot.classList.toggle('active', i === selected));

  if (manual) {
    manualLockUntil = performance.now() + 5000;
  }
}

// STITCH-BY-STITCH SIMULATION ENGINE (حركة الخياطة غرزة بغرزة)
function runStitchWeaving(sewProgress) {
  // sewProgress: 0.0 to 1.0 (based on scroll position inside spotlight)
  stitchBarFill.style.width = `${Math.round(sewProgress * 100)}%`;
  const stitchCount = Math.round(sewProgress * 1840);
  stitchCounter.textContent = `STITCHES: ${stitchCount} / 1840 (${Math.round(sewProgress * 100)}%)`;

  // 1. Weave the Border Stitch (SVG stroke-dashoffset)
  const totalLength = 2600; // Approx perimeter length
  const offset = totalLength * (1 - sewProgress);
  stitchBorderRect.style.strokeDashoffset = String(offset);

  // 2. Weave the Portrait from Top to Bottom
  const scanTopPercent = sewProgress * 100;
  sewingScanline.style.top = `${scanTopPercent}%`;
  const spotlightImg = document.getElementById('spotlightImg');
  spotlightImg.style.clipPath = `polygon(0 0, 100% 0, 100% ${scanTopPercent}%, 0 ${scanTopPercent}%)`;

  // 3. Move the Needle along the Stitch Line
  const cardRect = document.getElementById('labelCard').getBoundingClientRect();
  if (cardRect.width > 0) {
    // Needle orbits the card perimeter as it stitches
    let needleX = 20, needleY = 20;
    if (sewProgress < 0.25) {
      // Top edge
      needleX = (sewProgress / 0.25) * cardRect.width;
      needleY = 10;
    } else if (sewProgress < 0.5) {
      // Right edge
      needleX = cardRect.width - 10;
      needleY = ((sewProgress - 0.25) / 0.25) * cardRect.height;
    } else if (sewProgress < 0.75) {
      // Bottom edge
      needleX = cardRect.width - (((sewProgress - 0.5) / 0.25) * cardRect.width);
      needleY = cardRect.height - 10;
    } else {
      // Left edge
      needleX = 10;
      needleY = cardRect.height - (((sewProgress - 0.75) / 0.25) * cardRect.height);
    }
    sewingNeedle.style.left = `${needleX}px`;
    sewingNeedle.style.top = `${needleY}px`;
  }

  // 4. Stitch each letter of the Name one-by-one!
  const charSpans = document.querySelectorAll('.stitched-char');
  const charsToSew = Math.floor(sewProgress * (charSpans.length + 1));
  charSpans.forEach((charEl, idx) => {
    if (idx < charsToSew) {
      charEl.classList.add('sewn');
    } else {
      charEl.classList.remove('sewn');
    }
  });
}

// Master Zoom & Fly-Through handler
function applyZoom(prog) {
  // Phase 1: Zoom through the ring
  const scale = 1 + (prog * 3.2);
  const translateZ = prog * 500;
  const opacity = Math.max(0, 1 - (prog * 1.18));

  orbitWorld.style.transform = `scale(${scale}) translateZ(${translateZ}px)`;
  orbitWorld.style.opacity = String(opacity);

  introText.style.opacity = String(Math.max(0, 1 - (prog * 2)));
  introText.style.transform = `translateX(-50%) translateY(${-prog * 35}px)`;

  if (prog >= 0.82) {
    stitchedStage.classList.add('active');
    stitchStatusText.textContent = "Scroll Up to Exit ⇞";
    modeToggleText.textContent = "Overview ↺";
    isDived = true;

  } else {
    stitchedStage.classList.remove('active');
    stitchStatusText.textContent = "Scroll to Sew In ↡";
    modeToggleText.textContent = "Sew in 3D ✁";
    isDived = false;
  }
}

function setDive(diveIn) {
  isDived = diveIn;
  if (isDived && window.teamGoToMember) window.teamGoToMember(selected);
  else window.scrollTo({ top: 0, behavior: reducedMotion.matches ? 'instant' : 'smooth' });
}

document.querySelector('.stitch-progress-hud').addEventListener('click', () => setDive(!isDived));

// Arrow Buttons

// Keyboard
window.addEventListener('keydown', (e) => {
  if (e.altKey || e.ctrlKey || e.metaKey || e.target.closest?.('a, button, input, select, textarea')) return;
  if (['ArrowRight', 'ArrowDown', 'ArrowLeft', 'ArrowUp'].includes(e.key)) e.preventDefault();
  if (e.key === 'ArrowRight' || e.key === 'ArrowDown') window.teamGoToMember?.((selected + 1) % team.length);
  if (e.key === 'ArrowLeft' || e.key === 'ArrowUp') window.teamGoToMember?.((selected - 1 + team.length) % team.length);
  if (e.key === 'Escape') setDive(false);
});

// Gentle rotation when in ring overview
function animate(now) {
  if (!lastTime) lastTime = now;
  const dt = Math.min(now - lastTime, 40);
  lastTime = now;

  if (!reducedMotion.matches && scrollProgress < 0.3) {
    rotation += dt * 0.00032;
    layoutOrbit();
  }

  requestAnimationFrame(animate);
}

window.addEventListener('resize', layoutOrbit);

// Initialize
choose(3);
layoutOrbit();
requestAnimationFrame(animate);


/* Scroll-driven weaving of the entire profile panel, starting lower right. */
(() => {
  const card = document.getElementById('labelCard');
  const canvas = document.getElementById('cardWeaveCanvas');
  const counter = document.getElementById('stitchCounter');
  const bar = document.getElementById('stitchBarFill');
  const context = canvas.getContext('2d', { alpha: true });
  let width = 0, height = 0, cells = [], lastCount = -1;
  let pending = false, requested = 0;
  const clamp = (n, min, max) => Math.max(min, Math.min(max, n));

  function measure() {
    width = Math.max(1, Math.round(card.clientWidth));
    height = Math.max(1, Math.round(card.clientHeight));
    const dpr = Math.min(devicePixelRatio || 1, 2);
    canvas.width = Math.round(width * dpr);
    canvas.height = Math.round(height * dpr);
    context.setTransform(dpr, 0, 0, dpr, 0, 0);
    const step = width < 500 ? 8 : 10;
    const rows = Math.ceil(height / step);
    const cols = Math.ceil(width / step);
    cells = [];
    for (let row = rows - 1; row >= 0; row--) {
      const rightToLeft = (rows - 1 - row) % 2 === 0;
      for (let n = 0; n < cols; n++) {
        const col = rightToLeft ? cols - 1 - n : n;
        cells.push({ x: col * step, y: row * step, size: step, row, col });
      }
    }
    lastCount = -1;
    schedule(requested);
  }

  function stitch(cell) {
    const { x, y, size, row, col } = cell;
    const sx = x + size / 2, sy = y + size / 2;
    const shade = (row + col) % 3;
    context.fillStyle = shade === 0 ? '#112637' : shade === 1 ? '#102334' : '#142a3a';
    context.fillRect(x, y, size + .3, size + .3);

    // Paired yarn passes make the panel read as fabric rather than a wipe.
    context.lineCap = 'round';
    context.strokeStyle = 'rgba(1,9,18,.68)';
    context.lineWidth = 2.8;
    context.beginPath();
    context.moveTo(x + 1.2, sy + 1.2);
    context.quadraticCurveTo(sx, sy + 2.4, x + size - 1, sy + 1.2);
    context.stroke();
    context.strokeStyle = shade === 0 ? '#227e6a' : shade === 1 ? '#1c6c60' : '#276f68';
    context.lineWidth = 1.5;
    context.beginPath();
    context.moveTo(x + 1.2, sy);
    context.quadraticCurveTo(sx, sy - 1.3, x + size - 1, sy);
    context.stroke();
    context.strokeStyle = 'rgba(203,249,224,.19)';
    context.lineWidth = .65;
    context.beginPath();
    context.moveTo(sx - .9, y + 1);
    context.quadraticCurveTo(sx + 1.5, sy, sx - .9, y + size - 1);
    context.stroke();
    context.fillStyle = 'rgba(202,245,226,.19)';
    context.beginPath(); context.arc(sx, sy, .75, 0, Math.PI * 2); context.fill();
  }

  function drawNeedle(point, previous) {
    if (!point) return;
    const x = point.x + point.size / 2, y = point.y + point.size / 2;
    context.save();
    if (previous && Math.abs(previous.y - point.y) < point.size * 2) {
      const px = previous.x + previous.size / 2;
      const py = previous.y + previous.size / 2;
      context.strokeStyle = '#a2e9c7';
      context.lineWidth = 1.4;
      context.beginPath(); context.moveTo(px, py);
      context.bezierCurveTo(px + 7, py - 8, x + 18, y - 15, x + 1, y);
      context.stroke();
    }
    context.translate(x, y);
    context.rotate(-.29);
    context.shadowColor = '#000b'; context.shadowBlur = 7; context.shadowOffsetX = 2;
    const metal = context.createLinearGradient(-4, -31, 3, 2);
    metal.addColorStop(0, '#586c76');
    metal.addColorStop(.45, '#f5ffff');
    metal.addColorStop(1, '#7f989d');
    context.strokeStyle = metal;
    context.lineWidth = 2.6;
    context.lineCap = 'round';
    context.beginPath(); context.moveTo(-4, -30); context.lineTo(0, 0); context.stroke();
    context.shadowBlur = 0;
    context.strokeStyle = '#344d58'; context.lineWidth = .8;
    context.beginPath(); context.ellipse(-3.4, -24.5, 1.2, 2.6, -.2, 0, Math.PI * 2); context.stroke();
    context.restore();
  }

  function paint(progress) {
    if (!width || !height) return;
    const p = clamp(progress, 0, 1);
    const count = Math.floor(cells.length * p);
    if (count !== lastCount) {
      lastCount = count;
      context.clearRect(0, 0, width, height);
      context.save();
      context.beginPath();
      context.roundRect(0, 0, width, height, 23);
      context.clip();
      for (let i = 0; i < count; i++) stitch(cells[i]);
      context.restore();
      if (count < cells.length) drawNeedle(cells[count], cells[count - 1]);
      counter.textContent = `STITCHES  ${count.toLocaleString()} / ${cells.length.toLocaleString()}`;
    }
    const reveal = clamp((p - .42) / .27, 0, 1);
    card.style.setProperty('--content-reveal', reveal.toFixed(3));
    const chars = document.querySelectorAll('.stitched-char');
    const visible = Math.floor(clamp((p - .56) / .39, 0, 1) * (chars.length + 1));
    chars.forEach((char, index) => char.classList.toggle('sewn', index < visible));
    bar.style.width = `${Math.round(p * 100)}%`;
  }

  function schedule(progress) {
    requested = clamp(progress, 0, 1);
    if (pending) return;
    pending = true;
    requestAnimationFrame(() => { pending = false; paint(requested); });
  }

  runStitchWeaving = schedule;
  let entryDistance = 0;
  let segmentDistance = 0;
  let sewDistance = 0;
  let targetY = scrollY;
  let visualY = scrollY;
  let lastFrame = 0;

  function configureScroll() {
    entryDistance = innerHeight * 1.05;
    segmentDistance = innerHeight * 1.55;
    sewDistance = innerHeight * .88;
    document.body.style.height = `${Math.ceil(entryDistance + team.length * segmentDistance + innerHeight)}px`;
    targetY = scrollY;
  }

  function updateScene(now) {
    const elapsed = lastFrame ? Math.min(now - lastFrame, 64) : 16;
    lastFrame = now;
    const ease = reducedMotion.matches ? 1 : 1 - Math.exp(-elapsed / 105);
    visualY += (targetY - visualY) * ease;
    if (Math.abs(targetY - visualY) < .4) visualY = targetY;

    const entrance = clamp(visualY / entryDistance, 0, 1);
    scrollProgress = entrance;
    applyZoom(entrance);

    if (visualY < entryDistance) {
      schedule(0);
    } else {
      const relative = visualY - entryDistance;
      const slot = Math.min(team.length - 1, Math.floor(relative / segmentDistance));
      const member = (slot + 3) % team.length;
      if (selected !== member) choose(member);
      const within = relative - slot * segmentDistance;
      schedule(reducedMotion.matches ? 1 : clamp(within / sewDistance, 0, 1));
    }
    requestAnimationFrame(updateScene);
  }

  window.teamGoToMember = (index) => {
    const slot = ((index - 3) % team.length + team.length) % team.length;
    window.scrollTo({ top: entryDistance + slot * segmentDistance + sewDistance * .12, behavior: 'smooth' });
  };
  window.addEventListener('scroll', () => { targetY = scrollY; }, { passive: true });
  window.addEventListener('resize', configureScroll);
  configureScroll();
  requestAnimationFrame(updateScene);
  new ResizeObserver(measure).observe(card);
  measure();
})();
