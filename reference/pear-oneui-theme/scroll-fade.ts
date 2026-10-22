/**
 * 버스앱처럼, 스크롤할 내용이 더 있으면 그 쪽 끝을 그라데이션으로 부드럽게 흐린다.
 * (위로 더 있으면 위 44px, 아래로 더 있으면 아래 56px — Tokens.Sheet.topFadeHeight / fadeHeight)
 * 가로로 넘기는 칩 줄은 좌우 32px.
 *
 * 값은 CSS 변수(--oneui-fade-*)로 넘기고, style.css 에서 @property 로 등록해 부드럽게 바뀐다.
 */

/** 표시는 class 가 아니라 data 속성으로 — YT(Polymer)가 탭을 바꿀 때 class 를 통째로 다시 써서 지워지던 것 */
const ATTR = 'data-oneui-fade';

const TOP = 44;
const BOTTOM = 56;
const SIDE = 32;
/** 메인 화면 가로 목록(다시 듣기 등)은 조금 넓게 */
const SHELF_SIDE = 56;

/** 그라데이션을 줄 스크롤 상자들 */
const TARGETS = [
  'ytmusic-player-page #tab-renderer',
  // 재생 화면 실시간 가사 — 가사 플러그인이 자체 가상 목록 안에서 스크롤한다
  '#synced-lyrics-container .synced-lyrics-vlist',
  '#guide-content #sections, #guide-renderer #sections, #guide-content',
  'ytmusic-search-box #suggestion-list',
  'ytmusic-menu-popup-renderer tp-yt-paper-listbox',
  'ytmusic-chip-cloud-renderer #chips',
  'ytmusic-carousel #items',
].join(',');

/** 가로로 넘기는 줄인지 (칩 줄 · 메인 가로 목록) */
const isRowScroller = (el: HTMLElement) =>
  el.id === 'chips' || (el.id === 'items' && el.parentElement?.closest('ytmusic-carousel') !== null);

const update = (el: HTMLElement) => {
  // 가로 그라데이션은 가로로 넘기는 칩 줄에만 (사이드바 같은 곳의 미세한 가로 넘침은 무시)
  const isRow = isRowScroller(el);
  const vertical = !isRow && el.scrollHeight > el.clientHeight + 2;
  const horizontal = isRow && el.scrollWidth > el.clientWidth + 2;
  if (!vertical && !horizontal) {
    if (el.hasAttribute(ATTR)) el.removeAttribute(ATTR);
    return;
  }
  if (horizontal && !vertical) {
    if (el.getAttribute(ATTR) !== 'x') el.setAttribute(ATTR, 'x');
    const side = el.id === 'items' ? SHELF_SIDE : SIDE;
    const left = el.scrollLeft > 1 ? side : 0;
    const right = el.scrollLeft + el.clientWidth < el.scrollWidth - 1 ? side : 0;
    el.style.setProperty('--oneui-fade-start', `${left}px`);
    el.style.setProperty('--oneui-fade-end', `${right}px`);
  } else {
    if (el.getAttribute(ATTR) !== 'y') el.setAttribute(ATTR, 'y');
    const top = el.scrollTop > 1 ? TOP : 0;
    const bottom = el.scrollTop + el.clientHeight < el.scrollHeight - 1 ? BOTTOM : 0;
    el.style.setProperty('--oneui-fade-start', `${top}px`);
    el.style.setProperty('--oneui-fade-end', `${bottom}px`);
  }
};

/**
 * 메인 화면(앱 전체 스크롤)은 스크롤 상자 안에 상단 바 · 재생 바가 함께 들어 있어서 마스크를 쓰면 그것까지 흐려진다.
 * 그래서 배경(앨범 색)과 똑같이 칠한 고정 덮개를 상단 바 아래 · 재생 바 위에 깔고,
 * 덮개를 그라데이션 마스크로 흐려서 내용이 배경 속으로 자연스럽게 사라지게 한다.
 */
const setupPageFade = () => {
  const top = document.createElement('div');
  top.className = 'oneui-page-fade oneui-page-fade-top';
  const bottom = document.createElement('div');
  bottom.className = 'oneui-page-fade oneui-page-fade-bottom';
  document.body.append(top, bottom);
  const update = () => {
    const layout = document.querySelector<HTMLElement>('ytmusic-app-layout');
    if (!layout) return;
    const atTop = layout.scrollTop <= 1;
    const atBottom = layout.scrollTop + layout.clientHeight >= layout.scrollHeight - 1;
    top.classList.toggle('show', !atTop);
    bottom.classList.toggle('show', !atBottom);
  };
  return { update, remove: () => { top.remove(); bottom.remove(); } };
};

export const startScrollFade = () => {
  const page = setupPageFade();
  const scan = () => document.querySelectorAll<HTMLElement>(TARGETS).forEach(update);
  const onScroll = (e: Event) => {
    const el = e.target;
    if (el instanceof HTMLElement && el.tagName === 'YTMUSIC-APP-LAYOUT') page.update();
    if (el instanceof HTMLElement && el.hasAttribute(ATTR)) update(el);
  };
  document.addEventListener('scroll', onScroll, { capture: true, passive: true });

  // 가로 줄에서 마우스 휠(세로)을 굴리면 옆으로 넘긴다 (재생 화면 칩 줄 등)
  const onWheel = (e: WheelEvent) => {
    if (Math.abs(e.deltaX) > Math.abs(e.deltaY)) return;
    const target = e.target instanceof Element ? e.target : null;
    const row = target?.closest<HTMLElement>('ytmusic-chip-cloud-renderer #chips');
    if (!row || row.scrollWidth <= row.clientWidth + 2) return;
    e.preventDefault();
    row.scrollBy({ left: e.deltaY * 1.2, behavior: 'smooth' });
  };
  document.addEventListener('wheel', onWheel, { capture: true, passive: false });
  // 내용이 늘거나 화면이 바뀌어도 따라가도록 가끔 다시 훑는다 (대상이 몇 개뿐이라 가볍다)
  const timer = window.setInterval(() => {
    scan();
    page.update();
  }, 700);
  scan();
  page.update();
  return () => {
    document.removeEventListener('scroll', onScroll, { capture: true });
    document.removeEventListener('wheel', onWheel, { capture: true });
    clearInterval(timer);
    page.remove();
    document.querySelectorAll<HTMLElement>(`[${ATTR}]`).forEach((el) => {
      el.removeAttribute(ATTR);
      el.style.removeProperty('--oneui-fade-start');
      el.style.removeProperty('--oneui-fade-end');
    });
  };
};
