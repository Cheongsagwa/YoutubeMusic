/**
 * 플로팅 재생 바 아이콘 — 모양이 자연스럽게 바뀌는 SVG 아이콘.
 *
 * YT 아이콘(svg)은 숨기고 같은 칸에 직접 그린 SVG 를 넣는다. 색은 글자색(currentColor).
 *  · 재생 ↔ 일시정지 : 막대 두 개가 합쳐지며 삼각형이 되고, 삼각형이 갈라지며 막대가 된다 (CSS d 전환)
 *  · 이전 / 다음     : 누르면 새 삼각형이 옆에서 커지며 기존 것을 대신한다
 *  · 셔플            : 누르면 두 화살표 머리가 선의 처음부터 끝까지 다시 달려간다 (선은 그대로)
 *  · 반복            : 모드가 바뀌면 사각형은 그대로, 화살표가 사각형 둘레를 한 바퀴 돈다 (한 곡 반복이면 가운데 1)
 *  · 볼륨 ↔ 음소거   : 스피커 옆 물결이 사라지고 X 가 그려진다 (반대도 같은 방식)
 *  · 재생 화면 열기  : 아래 화살표 (YT 가 버튼을 돌려 열림 · 닫힘을 표현)
 */

const NS = 'http://www.w3.org/2000/svg';

/** 재생 · 일시정지 — 두 도형의 꼭짓점 수를 같게 맞춰 모양이 자연스럽게 이어진다 */
const PLAY_PAUSE = {
  pause: [
    'M6.5 5 L10.3 5 L10.3 19 L6.5 19 Z',
    'M13.7 5 L17.5 5 L17.5 19 L13.7 19 Z',
  ],
  play: [
    'M7.5 4.8 L13 8.3 L13 15.7 L7.5 19.2 Z',
    'M13 8.3 L19.2 12 L19.2 12 L13 15.7 Z',
  ],
};

/** 화살표 머리 — (0,0) 이 끝점, +x 방향을 가리킨다 */
const HEAD = 'M-2.6 -2.7 0 0-2.6 2.7';

const svg = (inner: string) => {
  const el = document.createElementNS(NS, 'svg');
  el.setAttribute('viewBox', '0 0 24 24');
  el.setAttribute('class', 'oneui-svg');
  el.innerHTML = inner;
  return el;
};

const ICON_HTML = {
  playpause: `<path class="pp-a" d="${PLAY_PAUSE.pause[0]}"/><path class="pp-b" d="${PLAY_PAUSE.pause[1]}"/>`,
  previous: `<rect class="bar" x="4.8" y="5.2" width="2.2" height="13.6" rx="1.1"/><g class="tri"><path d="M19.2 6.7v10.6c0 .9-1 1.4-1.7.9l-7.6-5.3c-.6-.4-.6-1.4 0-1.8l7.6-5.3c.7-.5 1.7 0 1.7.9z"/></g>`,
  next: `<rect class="bar" x="17" y="5.2" width="2.2" height="13.6" rx="1.1"/><g class="tri"><path d="M4.8 6.7v10.6c0 .9 1 1.4 1.7.9l7.6-5.3c.6-.4.6-1.4 0-1.8L6.5 5.8c-.7-.5-1.7 0-1.7.9z"/></g>`,
  shuffle: `<path class="line track ta" d="M3.6 18.6H6.4c4.8 0 6.4-13.2 11.2-13.2h2.8"/><path class="line track tb" d="M3.6 5.4H6.4c4.8 0 6.4 13.2 11.2 13.2h2.8"/><path class="line head ha0" d="${HEAD}"/><path class="line head hb0" d="${HEAD}"/><path class="line head ha" d="${HEAD}"/><path class="line head hb" d="${HEAD}"/>`,
  repeat: `<path class="line track loop" d="M7.4 5.2h9.2a3.8 3.8 0 0 1 3.8 3.8v6a3.8 3.8 0 0 1-3.8 3.8H7.4a3.8 3.8 0 0 1-3.8-3.8V9a3.8 3.8 0 0 1 3.8-3.8z"/><path class="line head hr" d="${HEAD}"/><path class="line one" d="M10.9 10.4l1.4-1v5.2"/>`,
  volume: `<g transform="translate(-0.7 0)"><path d="M4 9.6v4.8c0 .7.5 1.2 1.2 1.2h2.5l4.1 3.5c.7.6 1.7.1 1.7-.8V5.7c0-.9-1-1.4-1.7-.8L7.7 8.4H5.2C4.5 8.4 4 8.9 4 9.6z"/><path class="line wave w1" pathLength="1" d="M16.4 8.9a4.4 4.4 0 0 1 0 6.2"/><path class="line wave w2" pathLength="1" d="M18.8 6.5a7.8 7.8 0 0 1 0 11"/><path class="line x x1" pathLength="1" d="M16.6 9.6l4.8 4.8"/><path class="line x x2" pathLength="1" d="M21.4 9.6l-4.8 4.8"/></g>`,
  collapse: `<path class="line" d="M6 9.4 12 15.4l6-6"/>`,
};

type Name = keyof typeof ICON_HTML;

/** 아이콘 모양을 바꾸면 올린다 — 이미 들어가 있는 옛 SVG 를 새것으로 갈아 끼운다 */
const ICON_VERSION = '4';

/** 아이콘 칸(yt-icon)에 SVG 를 한 번만 넣는다 */
const mount = (host: Element | null, name: Name) => {
  if (!(host instanceof HTMLElement)) return null;
  let el = host.querySelector<SVGSVGElement>(':scope > svg.oneui-svg');
  if (!el || el.dataset.name !== name || el.dataset.v !== ICON_VERSION) {
    el?.remove();
    el = svg(ICON_HTML[name]) as SVGSVGElement;
    el.dataset.name = name;
    el.dataset.v = ICON_VERSION;
    host.appendChild(el);
    host.classList.add('oneui-svg-host');
    if (name === 'repeat') setRepeat(el, REPEAT_HOME);
    if (name === 'shuffle') setShuffle(el);
  }
  return el;
};


/** 화살표 머리를 선 위 거리 d 지점에 놓고, 진행 방향으로 돌린다 */
const placeHead = (track: SVGPathElement, head: Element, d: number, wrap = false, scale = 1, opacity = 1) => {
  const len = track.getTotalLength();
  const at = wrap ? ((d % len) + len) % len : Math.min(len, Math.max(0, d));
  const a = track.getPointAtLength(at);
  const b = track.getPointAtLength(Math.min(len, at + 0.3));
  const c = track.getPointAtLength(Math.max(0, at - 0.3));
  const deg = (Math.atan2(b.y - c.y, b.x - c.x) * 180) / Math.PI;
  head.setAttribute(
    'transform',
    `translate(${a.x.toFixed(2)} ${a.y.toFixed(2)}) rotate(${deg.toFixed(1)}) scale(${scale.toFixed(3)})`,
  );
  (head as SVGElement).style.opacity = opacity.toFixed(3);
};

const ease = (t: number) => (t < 0.5 ? 4 * t * t * t : 1 - Math.pow(-2 * t + 2, 3) / 2);

/** 0→1 진행을 duration 동안 부드럽게 흘려 보낸다 (같은 대상에 다시 걸면 앞의 것은 멈춘다) */
const running = new WeakMap<Element, number>();
const tween = (key: Element, duration: number, frame: (k: number) => void, delay = 0) => {
  cancelAnimationFrame(running.get(key) ?? 0);
  const start = performance.now() + delay;
  const step = (now: number) => {
    const t = Math.min(1, Math.max(0, (now - start) / duration));
    frame(ease(t));
    if (t < 1) running.set(key, requestAnimationFrame(step));
  };
  running.set(key, requestAnimationFrame(step));
};

/** 반복: 사각형 둘레에서 화살표 머리 앞에 작은 틈을 두고, 머리 위치 p 에 맞춰 선을 그린다 */
const REPEAT_GAP = 3.6;
const REPEAT_HOME = 8.4;
const setRepeat = (el: SVGSVGElement, p: number) => {
  const loop = el.querySelector<SVGPathElement>('.loop');
  const head = el.querySelector('.hr');
  if (!loop || !head) return;
  const len = loop.getTotalLength();
  loop.style.strokeDasharray = `${len - REPEAT_GAP} ${REPEAT_GAP}`;
  loop.style.strokeDashoffset = `${-(p + REPEAT_GAP)}`;
  placeHead(loop, head, p, true);
};
const spinRepeat = (el: SVGSVGElement) => {
  const loop = el.querySelector<SVGPathElement>('.loop');
  if (!loop) return;
  const len = loop.getTotalLength();
  tween(el, 760, (k) => setRepeat(el, REPEAT_HOME + len * k));
};

/**
 * 셔플: 선은 그대로. 누르면 끝에 있던 화살표 머리는 그 자리에서 작아지며 사라지고,
 * 새 머리가 선의 시작에서 작게 피어나 끝까지 달려가 자리를 잡는다.
 * k = 새 머리의 진행(0~1), 1 이면 쉬는 상태
 */
const setShuffle = (el: SVGSVGElement, ka = 1, kb = 1) => {
  const pairs: [string, string, string, number][] = [
    ['.ta', '.ha', '.ha0', ka],
    ['.tb', '.hb', '.hb0', kb],
  ];
  for (const [t, h, h0, k] of pairs) {
    const track = el.querySelector<SVGPathElement>(t);
    const head = el.querySelector(h);
    const old = el.querySelector(h0);
    if (!track || !head || !old) continue;
    const len = track.getTotalLength();
    const grow = Math.min(1, k / 0.35);
    const fade = 1 - Math.min(1, k / 0.3);
    placeHead(track, head, len * k, false, 0.35 + 0.65 * grow, grow);
    placeHead(track, old, len, false, 0.4 + 0.6 * fade, k >= 1 ? 0 : fade);
  }
};
const runShuffle = (el: SVGSVGElement) => {
  let ka = 0;
  let kb = 0;
  const a = el.querySelector('.ta');
  const b = el.querySelector('.tb');
  if (!a || !b) return;
  setShuffle(el, 0, 0);
  // 셔플은 켜고 끄는 모드가 아니라 '한 번 섞기' 동작이라, 섞는 동안만 밝게
  el.classList.add('active');
  window.setTimeout(() => el.classList.remove('active'), 900);
  tween(a, 700, (k) => { ka = k; setShuffle(el, ka, kb); });
  tween(b, 700, (k) => { kb = k; setShuffle(el, ka, kb); }, 90);
};

/** 같은 애니메이션을 다시 재생 */
const replay = (el: Element | null | undefined, cls: string) => {
  if (!el) return;
  el.classList.remove(cls);
  void (el as HTMLElement).getBoundingClientRect();
  el.classList.add(cls);
};

export const startBarIcons = () => {
  let lastRepeat: string | null = null;

  const apply = () => {
    const bar = document.querySelector('ytmusic-player-bar');
    if (!bar) return;
    const video = document.querySelector('video');
    const playing = !!video && !video.paused && !video.ended;

    const pp = mount(bar.querySelector('#play-pause-button yt-icon'), 'playpause');
    if (pp) {
      const s = playing ? 'pause' : 'play';
      if (pp.dataset.state !== s) {
        pp.dataset.state = s;
        pp.querySelector('.pp-a')?.setAttribute('d', PLAY_PAUSE[s][0]);
        pp.querySelector('.pp-b')?.setAttribute('d', PLAY_PAUSE[s][1]);
      }
    }
    mount(bar.querySelector('.previous-button yt-icon'), 'previous');
    mount(bar.querySelector('.next-button yt-icon'), 'next');
    mount(bar.querySelector('.shuffle yt-icon'), 'shuffle');
    mount(bar.querySelector('.toggle-player-page-button yt-icon'), 'collapse');

    const rp = mount(bar.querySelector('.repeat yt-icon'), 'repeat');
    const mode = bar.getAttribute('repeat-mode');
    if (rp) {
      rp.classList.toggle('mode-one', mode === 'ONE');
      rp.classList.toggle('mode-off', mode === 'NONE');
      if (lastRepeat !== null && lastRepeat !== mode) spinRepeat(rp);
      lastRepeat = mode;
    }

    const vol = mount(bar.querySelector('.volume yt-icon'), 'volume');
    const api = document.querySelector<HTMLElement & { isMuted?: () => boolean; getVolume?: () => number }>('#movie_player');
    const muted = !!api?.isMuted?.() || api?.getVolume?.() === 0 || !!video?.muted || video?.volume === 0;
    vol?.classList.toggle('muted', muted);
  };

  // 누르면 바로 반응하는 애니메이션 (상태가 없는 버튼)
  const onClick = (e: MouseEvent) => {
    const t = e.target instanceof Element ? e.target : null;
    const bar = t?.closest('ytmusic-player-bar');
    if (!bar) return;
    if (t?.closest('.next-button')) replay(bar.querySelector('.next-button svg.oneui-svg'), 'swap');
    else if (t?.closest('.previous-button'))
      replay(bar.querySelector('.previous-button svg.oneui-svg'), 'swap');
    else if (t?.closest('.shuffle')) {
      const sh = bar.querySelector<SVGSVGElement>('.shuffle svg.oneui-svg');
      if (sh) runShuffle(sh);
    }
  };

  let scheduled = 0;
  const schedule = () => {
    if (scheduled) return;
    scheduled = window.setTimeout(() => {
      scheduled = 0;
      apply();
    }, 60);
  };
  const media = ['play', 'pause', 'ended', 'volumechange', 'loadeddata', 'playing'];
  const onMedia = (e: Event) => {
    if (e.target instanceof HTMLVideoElement) schedule();
  };
  media.forEach((ev) => document.addEventListener(ev, onMedia, true));
  document.addEventListener('click', onClick, true);
  const observer = new MutationObserver(schedule);
  const bar = document.querySelector('ytmusic-player-bar');
  if (bar) observer.observe(bar, { attributes: true, attributeFilter: ['repeat-mode'], childList: true, subtree: true });
  const timer = window.setInterval(apply, 1000); // YT 가 아이콘 칸을 새로 만들 때 대비
  apply();

  return () => {
    media.forEach((ev) => document.removeEventListener(ev, onMedia, true));
    document.removeEventListener('click', onClick, true);
    observer.disconnect();
    clearInterval(timer);
    clearTimeout(scheduled);
    document.querySelectorAll('svg.oneui-svg').forEach((el) => el.remove());
    document.querySelectorAll('.oneui-svg-host').forEach((el) => el.classList.remove('oneui-svg-host'));
  };
};
